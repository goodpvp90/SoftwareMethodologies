using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media; // Required for VisualTreeHelper
using IcdControl.Models;

namespace IcdControl.Client
{
    public partial class IcdEditorWindow : Window
    {
        private string _icdId;
        private Icd _icd;
        private bool _isLoading = true;

        // Track selected field for property editing
        private BaseField _selectedField;

        public IcdEditorWindow(string icdId)
        {
            InitializeComponent();
            _icdId = icdId;
            Loaded += OnLoaded;

            PropTypeCombo.ItemsSource = new string[] { "uint32_t", "int32_t", "float", "double", "uint8_t", "uint16_t", "bool", "byte" };
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _icd = await ApiClient.Client.GetFromJsonAsync<Icd>($"api/icd/{_icdId}");
                if (_icd == null) throw new Exception("ICD not found");

                NameTxt.Text = _icd.Name;
                VersionTxt.Text = _icd.Version.ToString();

                RefreshTree();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load ICD: " + ex.Message);
                Close();
            }
            finally
            {
                _isLoading = false;
            }
        }

        // ---------------------------------------------------------
        // 1. Tree Management
        // ---------------------------------------------------------

        private void RefreshTree()
        {
            IcdTree.Items.Clear();

            // Messages Root
            var msgRoot = new TreeViewItem { Header = "Messages", FontWeight = FontWeights.Bold, Tag = "MessagesRoot" };
            msgRoot.ContextMenu = new ContextMenu();
            var addMsg = new MenuItem { Header = "Add New Message" };
            addMsg.Click += AddMessageBtn_Click;
            msgRoot.ContextMenu.Items.Add(addMsg);

            if (_icd.Messages != null)
            {
                foreach (var m in _icd.Messages) msgRoot.Items.Add(CreateTreeItem(m));
            }
            IcdTree.Items.Add(msgRoot);

            // Structs Root
            var structRoot = new TreeViewItem { Header = "Structs", FontWeight = FontWeights.Bold, Tag = "StructsRoot" };
            structRoot.ContextMenu = new ContextMenu();
            var addStruct = new MenuItem { Header = "Add New Struct" };
            addStruct.Click += AddStructBtn_Click;
            structRoot.ContextMenu.Items.Add(addStruct);

            if (_icd.Structs != null)
            {
                foreach (var s in _icd.Structs) structRoot.Items.Add(CreateTreeItem(s));
            }
            IcdTree.Items.Add(structRoot);
        }

        private TreeViewItem CreateTreeItem(BaseField field)
        {
            // Display name + struct type if it exists
            string header = field.Name;
            if (field is Struct st && !string.IsNullOrEmpty(st.StructType))
            {
                header += $" ({st.StructType})";
            }

            var item = new TreeViewItem { Header = header, Tag = field };

            // Add Context Menu
            var ctx = new ContextMenu();

            if (field is Struct)
            {
                var addF = new MenuItem { Header = "Add Field" };
                addF.Click += Ctx_AddField_Click;
                ctx.Items.Add(addF);

                var addS = new MenuItem { Header = "Add Nested Struct" };
                addS.Click += Ctx_AddStruct_Click;
                ctx.Items.Add(addS);

                ctx.Items.Add(new Separator());
            }

            var del = new MenuItem { Header = "Delete", Foreground = Brushes.Red };
            del.Click += Ctx_Delete_Click;
            ctx.Items.Add(del);

            item.ContextMenu = ctx;

            // Recursively add children
            if (field is Struct s && s.Fields != null)
            {
                foreach (var child in s.Fields) item.Items.Add(CreateTreeItem(child));
            }
            return item;
        }

        // --- NEW HELPER: Updates the visual header of a tree item ---
        private void UpdateTreeHeader(TreeViewItem item, BaseField field)
        {
            if (item == null || field == null) return;

            string header = field.Name;

            // If it's a struct with a linked type, append it
            if (field is Struct st && !string.IsNullOrEmpty(st.StructType))
            {
                header += $" ({st.StructType})";
            }

            item.Header = header;
        }

        // ---------------------------------------------------------
        // 2. Add / Delete Logic (With Validation)
        // ---------------------------------------------------------

        private void AddMessageBtn_Click(object sender, RoutedEventArgs e)
        {
            var newMsg = new Message { Name = "New_Message", Description = "New Message" };
            if (_icd.Messages == null) _icd.Messages = new List<Message>();
            _icd.Messages.Add(newMsg);
            RefreshTree();
        }

        private void AddStructBtn_Click(object sender, RoutedEventArgs e)
        {
            var newStruct = new Struct { Name = "New_Struct" };
            if (_icd.Structs == null) _icd.Structs = new List<Struct>();
            _icd.Structs.Add(newStruct);
            RefreshTree();
        }

        private void Ctx_Delete_Click(object sender, RoutedEventArgs e)
        {
            if (IcdTree.SelectedItem is TreeViewItem item && item.Tag is BaseField field)
            {
                // --- VALIDATION: Prevent deleting structs that are in use ---
                if (field is Struct structToDelete)
                {
                    // Check if this is a root definition
                    bool isRoot = _icd.Structs.Contains(structToDelete);
                    if (isRoot)
                    {
                        if (IsStructUsed(structToDelete.Name))
                        {
                            MessageBox.Show($"Cannot delete '{structToDelete.Name}' because it is being used inside other Messages or Structs.",
                                            "Deletion Blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                    }
                }
                // -----------------------------------------------------------

                RemoveField(_icd, field);
                RefreshTree();
                EditorPanel.Visibility = Visibility.Hidden;
            }
        }

        private bool RemoveField(object context, BaseField toRemove)
        {
            if (context is Icd root)
            {
                if (root.Messages.Remove(toRemove as Message)) return true;
                if (root.Structs.Remove(toRemove as Struct)) return true;
                foreach (var m in root.Messages) if (RemoveField(m, toRemove)) return true;
                foreach (var s in root.Structs) if (RemoveField(s, toRemove)) return true;
            }
            else if (context is Struct s)
            {
                if (s.Fields.Contains(toRemove))
                {
                    s.Fields.Remove(toRemove);
                    return true;
                }
                foreach (var sub in s.Fields.OfType<Struct>())
                {
                    if (RemoveField(sub, toRemove)) return true;
                }
            }
            return false;
        }

        // Helper: Check if a struct name is used anywhere in the system
        private bool IsStructUsed(string structName)
        {
            foreach (var msg in _icd.Messages)
            {
                if (CheckFieldsForUsage(msg.Fields, structName)) return true;
            }
            foreach (var st in _icd.Structs)
            {
                if (st.Name == structName) continue; // Don't check itself
                if (CheckFieldsForUsage(st.Fields, structName)) return true;
            }
            return false;
        }

        private bool CheckFieldsForUsage(List<BaseField> fields, string targetStructName)
        {
            if (fields == null) return false;
            foreach (var f in fields)
            {
                if (f is Struct st)
                {
                    if (st.StructType == targetStructName) return true;
                    if (CheckFieldsForUsage(st.Fields, targetStructName)) return true;
                }
            }
            return false;
        }

        private void Ctx_AddField_Click(object sender, RoutedEventArgs e)
        {
            if (IcdTree.SelectedItem is TreeViewItem item && item.Tag is Struct parentStruct)
            {
                var newField = new DataField { Name = "new_field", Type = "uint32_t", SizeInBits = 32 };
                parentStruct.Fields.Add(newField);
                RefreshTree();

                // Note: RefreshTree rebuilds the tree, so 'item' is disconnected. 
                // We don't re-select automatically here to keep it simple.
            }
        }

        private void Ctx_AddNewItem_Click(object sender, RoutedEventArgs e) { }

        private void Ctx_AddStruct_Click(object sender, RoutedEventArgs e)
        {
            if (IcdTree.SelectedItem is TreeViewItem item && item.Tag is Struct parentStruct)
            {
                var newStruct = new Struct { Name = "nested_struct" };
                parentStruct.Fields.Add(newStruct);
                RefreshTree();
            }
        }

        // ---------------------------------------------------------
        // 3. Property Editing & Circular Dependency Protection
        // ---------------------------------------------------------

        private void IcdTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem item && item.Tag is BaseField field)
            {
                _isLoading = true;
                _selectedField = field;
                EditorPanel.Visibility = Visibility.Visible;
                PropNameTxt.Text = field.Name;
                DebugInfoTxt.Text = $"Type: {field.GetType().Name}, ID: {field.Id}";

                if (field is DataField df)
                {
                    FieldPropsPanel.Visibility = Visibility.Visible;
                    StructLinkPanel.Visibility = Visibility.Collapsed;
                    PropTypeCombo.SelectedItem = df.Type;
                    PropIsUnionChk.Visibility = Visibility.Collapsed;
                }
                else if (field is Struct s)
                {
                    FieldPropsPanel.Visibility = Visibility.Collapsed;

                    // Distinguish between Root Struct (Definition) and Nested Struct (Instance)
                    bool isRoot = _icd.Structs.Contains(s) || _icd.Messages.Contains(s as Message);

                    if (isRoot)
                    {
                        // Root definition: Can set Union, cannot link to another struct
                        StructLinkPanel.Visibility = Visibility.Collapsed;
                        PropIsUnionChk.Visibility = Visibility.Visible;
                        PropIsUnionChk.IsChecked = s.IsUnion;
                    }
                    else
                    {
                        // Nested Instance: Can link to a definition, cannot change Union status (inherits)
                        StructLinkPanel.Visibility = Visibility.Visible;
                        UpdateStructLinkList(item); // Circular check happens here
                        StructLinkCombo.SelectedItem = s.StructType;

                        PropIsUnionChk.Visibility = Visibility.Collapsed;
                    }
                }
                _isLoading = false;
            }
            else
            {
                EditorPanel.Visibility = Visibility.Hidden;
            }
        }

        // Populate the combo box, filtering out any struct that would cause a loop
        private void UpdateStructLinkList(TreeViewItem currentItem)
        {
            string rootParentName = GetRootParentName(currentItem);
            var validStructs = new List<string>();

            foreach (var candidate in _icd.Structs)
            {
                // 1. Direct recursion check
                if (candidate.Name == rootParentName) continue;

                // 2. Indirect recursion check
                if (IsStructReferencing(candidate.Name, rootParentName, new HashSet<string>())) continue;

                validStructs.Add(candidate.Name);
            }

            StructLinkCombo.ItemsSource = validStructs.OrderBy(n => n).ToList();
        }

        // Check if checkingStructName eventually points to targetStructName
        private bool IsStructReferencing(string checkingStructName, string targetStructName, HashSet<string> visited)
        {
            if (visited.Contains(checkingStructName)) return false;
            visited.Add(checkingStructName);

            var definition = _icd.Structs.FirstOrDefault(s => s.Name == checkingStructName);
            if (definition == null || definition.Fields == null) return false;

            foreach (var field in definition.Fields)
            {
                if (field is Struct st)
                {
                    if (st.StructType == targetStructName) return true;
                    if (!string.IsNullOrEmpty(st.StructType))
                    {
                        if (IsStructReferencing(st.StructType, targetStructName, visited)) return true;
                    }
                }
            }
            return false;
        }

        // Walk up the visual tree to find which Root Struct/Message we are editing
        private string GetRootParentName(TreeViewItem item)
        {
            DependencyObject curr = item;
            while (curr != null)
            {
                if (curr is TreeViewItem tvi && tvi.Tag is BaseField bf)
                {
                    // If this item is directly in the Structs or Messages list of the ICD
                    if (_icd.Structs.Contains(bf) || _icd.Messages.Contains(bf as Message))
                        return bf.Name;
                }
                curr = VisualTreeHelper.GetParent(curr);
            }
            return null;
        }

        // --- UPDATED: PropNameTxt_TextChanged calls UpdateTreeHeader ---
        private void PropNameTxt_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isLoading && _selectedField != null)
            {
                _selectedField.Name = PropNameTxt.Text;

                // Real-time Visual Update
                if (IcdTree.SelectedItem is TreeViewItem selectedItem)
                {
                    UpdateTreeHeader(selectedItem, _selectedField);
                }
            }
        }

        private void PropTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoading && _selectedField is DataField df && PropTypeCombo.SelectedItem is string type)
            {
                df.Type = type;
                // Note: We don't show type in header currently, but if we did, we'd call UpdateTreeHeader here too.
            }
        }

        // --- UPDATED: StructLinkCombo calls UpdateTreeHeader ---
        private void StructLinkCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoading && _selectedField is Struct s && StructLinkCombo.SelectedItem is string linkedName)
            {
                s.StructType = linkedName;

                // Real-time Visual Update (to show the new type in parens)
                if (IcdTree.SelectedItem is TreeViewItem selectedItem)
                {
                    UpdateTreeHeader(selectedItem, s);
                }
            }
        }

        private void PropIsUnionChk_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isLoading && _selectedField is Struct s) s.IsUnion = PropIsUnionChk.IsChecked == true;
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            _icd.Name = NameTxt.Text;
            if (double.TryParse(VersionTxt.Text, out var v)) _icd.Version = v;

            ApiClient.EnsureAuthHeader();

            _icd.Messages ??= new List<Message>();
            _icd.Structs ??= new List<Struct>();

            try
            {
                var res = await ApiClient.Client.PostAsJsonAsync("api/icd/save", _icd);
                if (res.IsSuccessStatusCode)
                {
                    MessageBox.Show("Saved successfully!");
                }
                else
                {
                    var details = await res.Content.ReadAsStringAsync();
                    if (res.StatusCode == HttpStatusCode.Forbidden)
                    {
                        MessageBox.Show("You don't have permission to save changes for this ICD.");
                        return;
                    }
                    MessageBox.Show($"Save failed: {(int)res.StatusCode} {res.ReasonPhrase}\n{details}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Network Error: " + ex.Message);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}