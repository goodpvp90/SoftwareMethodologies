using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using IcdControl.Models;
using IcdControl.Client.Models;

namespace IcdControl.Client
{
    public partial class IcdEditorWindow : Window
    {
        private readonly string _id;

        // Static HttpClient
        private static readonly HttpClient SharedClient = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:5273/")
        };

        private Icd _icd;

        // Track selection
        private object _selectedDataObject;
        private TreeViewItem _selectedTreeItem;

        public IcdEditorWindow(string id)
        {
            _id = id;
            InitializeComponent();

            // Initialize primitive types combo
            PropTypeCombo.ItemsSource = new List<string> { "int", "double", "float", "string", "bool", "uint", "byte" };

            Loaded += OnLoaded;
        }

        // ---------------------------------------------------------
        // 1. Loading Data
        // ---------------------------------------------------------
        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _icd = await SharedClient.GetFromJsonAsync<Icd>($"api/icd/{_id}");

                if (_icd == null)
                {
                    MessageBox.Show("ICD not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Close();
                    return;
                }

                NameTxt.Text = _icd.Name;
                VersionTxt.Text = _icd.Version.ToString();

                if (_icd.Messages == null) _icd.Messages = new List<Message>();
                if (_icd.Structs == null) _icd.Structs = new List<Struct>();

                BuildTree();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        // ---------------------------------------------------------
        // 2. Tree Construction
        // ---------------------------------------------------------
        private void BuildTree()
        {
            IcdTree.Items.Clear();

            // --- Root: Messages ---
            var msgRootItem = new TreeViewItem
            {
                Header = "Messages",
                Tag = "Root_Messages",
                ContextMenu = (ContextMenu)IcdTree.Resources["RootContextMenu"]
            };

            foreach (var msg in _icd.Messages)
            {
                msgRootItem.Items.Add(BuildStructNode(msg));
            }

            // --- Root: Structs ---
            var structRootItem = new TreeViewItem
            {
                Header = "Structs",
                Tag = "Root_Structs",
                ContextMenu = (ContextMenu)IcdTree.Resources["RootContextMenu"]
            };

            foreach (var st in _icd.Structs)
            {
                structRootItem.Items.Add(BuildStructNode(st));
            }

            IcdTree.Items.Add(msgRootItem);
            IcdTree.Items.Add(structRootItem);
        }

        private TreeViewItem BuildStructNode(Struct st)
        {
            string header = st.Name;
            if (!string.IsNullOrEmpty(st.StructType))
            {
                header += $" ({st.StructType})";
            }

            var item = new TreeViewItem
            {
                Header = header,
                Tag = st,
                ContextMenu = (ContextMenu)IcdTree.Resources["ItemContextMenu"]
            };

            if (st.Fields != null)
            {
                foreach (var f in st.Fields)
                {
                    if (f is DataField df)
                    {
                        var fieldItem = new TreeViewItem
                        {
                            Header = $"{df.Name} : {df.Type}",
                            Tag = df,
                            ContextMenu = (ContextMenu)IcdTree.Resources["ItemContextMenu"]
                        };
                        item.Items.Add(fieldItem);
                    }
                    else if (f is Struct nested)
                    {
                        item.Items.Add(BuildStructNode(nested));
                    }
                }
            }
            return item;
        }

        // ---------------------------------------------------------
        // 3. Selection & Editing Pane Logic
        // ---------------------------------------------------------
        private void IcdTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            _selectedTreeItem = e.NewValue as TreeViewItem;

            if (_selectedTreeItem == null)
            {
                EditorPanel.Visibility = Visibility.Hidden;
                return;
            }

            _selectedDataObject = _selectedTreeItem.Tag;

            if (_selectedDataObject is Struct st)
            {
                EditorPanel.Visibility = Visibility.Visible;
                PropNameTxt.Text = st.Name;

                // Hide DataField specific panels
                FieldPropsPanel.Visibility = Visibility.Collapsed;

                // Check if this is a Root Struct or a Nested Struct
                bool isRoot = _icd.Structs.Contains(st) || _icd.Messages.Contains(st as Message);

                if (!isRoot)
                {
                    // --- Nested Struct Logic ---
                    StructLinkPanel.Visibility = Visibility.Visible;

                    // Populate with available Structs
                    UpdateStructLinkList();
                    StructLinkCombo.SelectedItem = st.StructType;

                    // FIX: Hide Union option for nested structs (instances)
                    PropIsUnionChk.Visibility = Visibility.Collapsed;

                    DebugInfoTxt.Text = "Selected: Nested Struct (Instance)";
                }
                else
                {
                    // --- Root Definition Logic ---
                    StructLinkPanel.Visibility = Visibility.Collapsed;

                    // FIX: Show Union option only for definitions
                    PropIsUnionChk.Visibility = Visibility.Visible;
                    PropIsUnionChk.IsChecked = st.IsUnion;

                    DebugInfoTxt.Text = "Selected: Root Struct Definition";
                }
            }
            else if (_selectedDataObject is DataField df)
            {
                EditorPanel.Visibility = Visibility.Visible;
                PropNameTxt.Text = df.Name;

                // Show Field props
                FieldPropsPanel.Visibility = Visibility.Visible;
                PropTypeCombo.SelectedItem = df.Type;

                // Hide Struct props
                StructLinkPanel.Visibility = Visibility.Collapsed;
                PropIsUnionChk.Visibility = Visibility.Collapsed;

                DebugInfoTxt.Text = "Selected: Data Field";
            }
            else
            {
                EditorPanel.Visibility = Visibility.Hidden;
            }
        }

        private void UpdateStructLinkList()
        {
            var structNames = _icd.Structs.Select(s => s.Name).OrderBy(n => n).ToList();
            StructLinkCombo.ItemsSource = structNames;
        }

        // --- Real-time Updates ---

        private void PropNameTxt_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_selectedDataObject == null || _selectedTreeItem == null) return;
            if (EditorPanel.Visibility != Visibility.Visible) return;

            string newName = PropNameTxt.Text;

            if (_selectedDataObject is Struct st) st.Name = newName;
            else if (_selectedDataObject is DataField df) df.Name = newName;

            UpdateTreeHeader(_selectedTreeItem, _selectedDataObject);
        }

        private void PropTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_selectedDataObject is DataField df && PropTypeCombo.SelectedItem is string selectedType)
            {
                df.Type = selectedType;
                UpdateTreeHeader(_selectedTreeItem, df);
            }
        }

        private void StructLinkCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_selectedDataObject is Struct st && StructLinkCombo.SelectedItem is string linkedStructName)
            {
                st.StructType = linkedStructName;
                UpdateTreeHeader(_selectedTreeItem, st);
            }
        }

        private void PropIsUnionChk_Changed(object sender, RoutedEventArgs e)
        {
            if (_selectedDataObject is Struct st)
            {
                st.IsUnion = PropIsUnionChk.IsChecked ?? false;
            }
        }

        private void UpdateTreeHeader(TreeViewItem item, object data)
        {
            if (data is Struct st)
            {
                string header = st.Name;
                if (!string.IsNullOrEmpty(st.StructType)) header += $" ({st.StructType})";
                item.Header = header;
            }
            else if (data is DataField df)
            {
                item.Header = $"{df.Name} : {df.Type}";
            }
        }

        // ---------------------------------------------------------
        // 4. Context Menu Actions
        // ---------------------------------------------------------

        private void Ctx_AddField_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTreeItem == null || !(_selectedDataObject is Struct parentStruct))
            {
                MessageBox.Show("Please select a Struct.");
                return;
            }

            var newField = new DataField { Name = "NewField", Type = "int" };
            if (parentStruct.Fields == null) parentStruct.Fields = new List<BaseField>();
            parentStruct.Fields.Add(newField);

            var newItem = new TreeViewItem
            {
                Header = "NewField : int",
                Tag = newField,
                ContextMenu = (ContextMenu)IcdTree.Resources["ItemContextMenu"]
            };

            _selectedTreeItem.Items.Add(newItem);
            _selectedTreeItem.IsExpanded = true;
        }

        private void Ctx_AddStruct_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTreeItem == null || !(_selectedDataObject is Struct parentStruct))
            {
                MessageBox.Show("Please select a Struct.");
                return;
            }

            var newNested = new Struct
            {
                Name = "NewNestedStruct",
                Fields = new List<BaseField>()
            };

            if (parentStruct.Fields == null) parentStruct.Fields = new List<BaseField>();
            parentStruct.Fields.Add(newNested);

            var newItem = BuildStructNode(newNested);

            _selectedTreeItem.Items.Add(newItem);
            _selectedTreeItem.IsExpanded = true;
        }

        private void Ctx_AddNewItem_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTreeItem == null) return;
            string tag = _selectedTreeItem.Tag as string;

            if (tag == "Root_Messages")
            {
                var newMsg = new Message { Name = "NewMessage", Fields = new List<BaseField>() };
                _icd.Messages.Add(newMsg);
                _selectedTreeItem.Items.Add(BuildStructNode(newMsg));
            }
            else if (tag == "Root_Structs")
            {
                var newStruct = new Struct { Name = "NewStruct", Fields = new List<BaseField>() };
                _icd.Structs.Add(newStruct);
                _selectedTreeItem.Items.Add(BuildStructNode(newStruct));
            }

            _selectedTreeItem.IsExpanded = true;
        }

        private void Ctx_Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTreeItem == null) return;

            // Validation: Prevent deleting used structs
            if (_selectedDataObject is Struct structToDelete)
            {
                bool isRootStruct = _icd.Structs.Contains(structToDelete);
                if (isRootStruct)
                {
                    if (IsStructUsed(structToDelete.Name))
                    {
                        MessageBox.Show($"Cannot delete '{structToDelete.Name}' because it is used inside other items.",
                                        "Deletion Blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
            }

            // Remove from UI
            var parentItem = ItemsControl.ItemsControlFromItemContainer(_selectedTreeItem);
            if (parentItem != null)
            {
                parentItem.Items.Remove(_selectedTreeItem);
            }

            // Remove from Model Lists
            if (_selectedDataObject is Message msg) _icd.Messages.Remove(msg);
            else if (_selectedDataObject is Struct st && _icd.Structs.Contains(st)) _icd.Structs.Remove(st);

            EditorPanel.Visibility = Visibility.Hidden;
            _selectedDataObject = null;
            _selectedTreeItem = null;
        }

        private bool IsStructUsed(string structName)
        {
            foreach (var msg in _icd.Messages)
            {
                if (CheckFieldsForUsage(msg.Fields, structName)) return true;
            }
            foreach (var st in _icd.Structs)
            {
                if (st.Name == structName) continue;
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

        // ---------------------------------------------------------
        // 5. Save & Close
        // ---------------------------------------------------------
        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(VersionTxt.Text, out double ver))
            {
                MessageBox.Show("Invalid version number.");
                return;
            }

            _icd.Name = NameTxt.Text;
            _icd.Version = ver;

            try
            {
                var response = await SharedClient.PostAsJsonAsync("api/icd/save", _icd);

                if (response.IsSuccessStatusCode)
                    MessageBox.Show("Saved successfully!");
                else
                    MessageBox.Show($"Save failed: {response.ReasonPhrase}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}