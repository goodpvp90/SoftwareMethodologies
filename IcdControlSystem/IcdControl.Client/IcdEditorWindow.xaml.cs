using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using IcdControl.Models;

namespace IcdControl.Client
{
    public partial class IcdEditorWindow : Window
    {
        private string _icdId;
        private Icd _icd;
        private bool _isLoading = true;
        private BaseField _selectedField;

        private Point _dragStartPoint;
        private TreeViewItem _draggedItemContainer;
        private BaseField _draggedData;

        public IcdEditorWindow(string icdId)
        {
            InitializeComponent();
            _icdId = icdId;
            Loaded += OnLoaded;

            // Updated Types List
            PropTypeCombo.ItemsSource = new string[]
            {
                "uint8_t", "int8_t",
                "uint16_t", "int16_t",
                "uint32_t", "int32_t",
                "uint64_t", "int64_t",
                "float", "double",
                "bool", "char"
            };
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

            var msgRoot = new TreeViewItem
            {
                Header = "Messages",
                FontWeight = FontWeights.Bold,
                Tag = "MessagesRoot",
                ContextMenu = (ContextMenu)Resources["RootContextMenu"],
                AllowDrop = true
            };
            msgRoot.DragOver += Tree_DragOver;
            msgRoot.Drop += Tree_Drop;

            ((MenuItem)msgRoot.ContextMenu.Items[0]).Click += AddMessageBtn_Click;

            if (_icd.Messages != null)
                foreach (var m in _icd.Messages) msgRoot.Items.Add(CreateTreeItem(m));
            IcdTree.Items.Add(msgRoot);

            var structRoot = new TreeViewItem
            {
                Header = "Structs",
                FontWeight = FontWeights.Bold,
                Tag = "StructsRoot",
                ContextMenu = (ContextMenu)Resources["RootContextMenu"],
                AllowDrop = true
            };
            structRoot.DragOver += Tree_DragOver;
            structRoot.Drop += Tree_Drop;

            ((MenuItem)structRoot.ContextMenu.Items[0]).Click += AddStructBtn_Click;

            if (_icd.Structs != null)
                foreach (var s in _icd.Structs) structRoot.Items.Add(CreateTreeItem(s));
            IcdTree.Items.Add(structRoot);
        }

        private TreeViewItem CreateTreeItem(BaseField field)
        {
            var item = new TreeViewItem { Tag = field };
            UpdateTreeHeader(item, field);
            item.ContextMenu = (ContextMenu)Resources["ItemContextMenu"];

            item.AllowDrop = true;
            item.PreviewMouseLeftButtonDown += Tree_PreviewMouseLeftButtonDown;
            item.MouseMove += Tree_MouseMove;
            item.DragOver += Tree_DragOver;
            item.Drop += Tree_Drop;

            if (field is Struct s && s.Fields != null)
            {
                foreach (var child in s.Fields) item.Items.Add(CreateTreeItem(child));
            }
            return item;
        }

        private void UpdateTreeHeader(TreeViewItem item, BaseField field)
        {
            if (item == null || field == null) return;
            string header = field.Name;

            if (field is DataField df)
            {
                header += $" : {df.Type}";

                // Show bits ONLY if custom size
                int stdSize = GetStandardSize(df.Type);
                if (df.SizeInBits > 0 && df.SizeInBits != stdSize)
                {
                    header += $" ({df.SizeInBits}b)";
                }
            }
            else if (field is Struct st && !string.IsNullOrEmpty(st.StructType))
            {
                header += $" ({st.StructType})";
            }
            item.Header = header;
        }

        private void RestoreSelection(BaseField target)
        {
            if (target == null) return;
            var item = FindTreeViewItem(IcdTree.Items, target);
            if (item != null)
            {
                var parent = item.Parent as TreeViewItem;
                while (parent != null)
                {
                    parent.IsExpanded = true;
                    parent = parent.Parent as TreeViewItem;
                }
                item.IsExpanded = true;
                item.IsSelected = true;
                item.Focus();
            }
        }

        private TreeViewItem FindTreeViewItem(ItemCollection items, BaseField target)
        {
            foreach (var obj in items)
            {
                if (obj is TreeViewItem item)
                {
                    if (item.Tag == target) return item;
                    var found = FindTreeViewItem(item.Items, target);
                    if (found != null) return found;
                }
            }
            return null;
        }

        // ---------------------------------------------------------
        // 2. Add / Delete Logic
        // ---------------------------------------------------------

        private void AddMessageBtn_Click(object sender, RoutedEventArgs e)
        {
            var newMsg = new Message { Name = "New_Message" };
            if (_icd.Messages == null) _icd.Messages = new List<Message>();
            newMsg.Name = GetUniqueName(_icd.Messages.Cast<BaseField>().ToList(), newMsg.Name);
            _icd.Messages.Add(newMsg);
            RefreshTree();
            RestoreSelection(newMsg);
        }

        private void AddStructBtn_Click(object sender, RoutedEventArgs e)
        {
            var newStruct = new Struct { Name = "New_Struct" };
            if (_icd.Structs == null) _icd.Structs = new List<Struct>();
            newStruct.Name = GetUniqueName(_icd.Structs.Cast<BaseField>().ToList(), newStruct.Name);
            _icd.Structs.Add(newStruct);
            RefreshTree();
            RestoreSelection(newStruct);
        }

        private void Ctx_AddField_Click(object sender, RoutedEventArgs e)
        {
            if (IcdTree.SelectedItem is TreeViewItem item && item.Tag is Struct parentStruct)
            {
                var newField = new DataField { Name = "new_field", Type = "uint32_t", SizeInBits = 32 };
                newField.Name = GetUniqueName(parentStruct.Fields, newField.Name);
                parentStruct.Fields.Add(newField);
                RefreshTree();
                RestoreSelection(parentStruct);
            }
        }

        private void Ctx_AddStruct_Click(object sender, RoutedEventArgs e)
        {
            if (IcdTree.SelectedItem is TreeViewItem item && item.Tag is Struct parentStruct)
            {
                var newStruct = new Struct { Name = "nested_struct" };
                newStruct.Name = GetUniqueName(parentStruct.Fields, newStruct.Name);
                parentStruct.Fields.Add(newStruct);
                RefreshTree();
                RestoreSelection(parentStruct);
            }
        }

        private void Ctx_AddNewItem_Click(object sender, RoutedEventArgs e)
        {
            if (IcdTree.SelectedItem is TreeViewItem item)
            {
                string tag = item.Tag as string;
                if (tag == "MessagesRoot") AddMessageBtn_Click(sender, e);
                if (tag == "StructsRoot") AddStructBtn_Click(sender, e);
            }
        }

        private void Ctx_Delete_Click(object sender, RoutedEventArgs e)
        {
            if (IcdTree.SelectedItem is TreeViewItem item && item.Tag is BaseField field)
            {
                if (field is Struct structToDelete)
                {
                    bool isRoot = _icd.Structs.Contains(structToDelete);
                    if (isRoot && IsStructUsed(structToDelete.Name))
                    {
                        MessageBox.Show("Cannot delete struct in use.", "Blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
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

        private bool IsStructUsed(string structName)
        {
            foreach (var msg in _icd.Messages) if (CheckFieldsForUsage(msg.Fields, structName)) return true;
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
        // 3. Property Editing
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
                    PropSizeTxt.Text = df.SizeInBits.ToString();

                    // --- UPDATED: Allow size editing ONLY for unsigned types ---
                    PropSizeTxt.IsEnabled = IsUnsignedType(df.Type);

                    PropIsUnionChk.Visibility = Visibility.Collapsed;
                }
                else if (field is Struct s)
                {
                    FieldPropsPanel.Visibility = Visibility.Collapsed;
                    bool isRoot = _icd.Structs.Contains(s) || _icd.Messages.Contains(s as Message);

                    if (isRoot)
                    {
                        StructLinkPanel.Visibility = Visibility.Collapsed;
                        PropIsUnionChk.Visibility = Visibility.Visible;
                        PropIsUnionChk.IsChecked = s.IsUnion;
                    }
                    else
                    {
                        StructLinkPanel.Visibility = Visibility.Visible;
                        UpdateStructLinkList(item);
                        StructLinkCombo.SelectedItem = s.StructType;
                        PropIsUnionChk.Visibility = Visibility.Collapsed;
                    }
                }
                _isLoading = false;
            }
            else EditorPanel.Visibility = Visibility.Hidden;
        }

        private void UpdateStructLinkList(TreeViewItem currentItem = null)
        {
            string rootParentName = currentItem != null ? GetRootParentName(currentItem) : null;
            var validStructs = new List<string>();

            foreach (var candidate in _icd.Structs)
            {
                if (rootParentName != null)
                {
                    if (candidate.Name == rootParentName) continue;
                    if (IsStructReferencing(candidate.Name, rootParentName, new HashSet<string>())) continue;
                }
                validStructs.Add(candidate.Name);
            }
            StructLinkCombo.ItemsSource = validStructs.OrderBy(n => n).ToList();
        }

        private bool IsStructReferencing(string checkingStructName, string targetStructName, HashSet<string> visited)
        {
            if (visited.Contains(checkingStructName)) return false;
            visited.Add(checkingStructName);
            var definition = _icd.Structs.FirstOrDefault(s => s.Name == checkingStructName);
            if (definition == null || definition.Fields == null) return false;

            foreach (var field in definition.Fields)
            {
                if (field is Struct st && st.StructType == targetStructName) return true;
                if (field is Struct st2 && !string.IsNullOrEmpty(st2.StructType))
                    if (IsStructReferencing(st2.StructType, targetStructName, visited)) return true;
            }
            return false;
        }

        private string GetRootParentName(TreeViewItem item)
        {
            DependencyObject curr = item;
            while (curr != null)
            {
                if (curr is TreeViewItem tvi && tvi.Tag is BaseField bf)
                {
                    if (_icd.Structs.Contains(bf) || _icd.Messages.Contains(bf as Message)) return bf.Name;
                }
                curr = VisualTreeHelper.GetParent(curr);
            }
            return null;
        }

        private void PropNameTxt_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isLoading && _selectedField != null)
            {
                _selectedField.Name = PropNameTxt.Text;
                if (IcdTree.SelectedItem is TreeViewItem selectedItem) UpdateTreeHeader(selectedItem, _selectedField);
            }
        }

        // --- UPDATED LOGIC FOR TYPES ---
        private void PropTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoading && _selectedField is DataField df && PropTypeCombo.SelectedItem is string type)
            {
                df.Type = type;

                int stdSize = GetStandardSize(type);
                bool isUnsigned = IsUnsignedType(type);

                // If standard size found, update it
                if (stdSize > 0)
                {
                    df.SizeInBits = stdSize;
                    PropSizeTxt.Text = stdSize.ToString();
                }

                // Enable bits only if unsigned
                PropSizeTxt.IsEnabled = isUnsigned;

                if (IcdTree.SelectedItem is TreeViewItem selectedItem) UpdateTreeHeader(selectedItem, df);
            }
        }

        private void PropSizeTxt_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isLoading && _selectedField is DataField df)
            {
                if (int.TryParse(PropSizeTxt.Text, out int size))
                {
                    df.SizeInBits = size;
                    if (IcdTree.SelectedItem is TreeViewItem selectedItem) UpdateTreeHeader(selectedItem, df);
                }
            }
        }

        // --- Helpers for Types ---
        private int GetStandardSize(string type)
        {
            if (string.IsNullOrEmpty(type)) return 32;
            type = type.ToLower();
            if (type.Contains("8")) return 8;
            if (type.Contains("16")) return 16;
            if (type.Contains("32")) return 32;
            if (type.Contains("64")) return 64;
            if (type == "bool") return 8;
            if (type == "char") return 8;
            if (type == "float") return 32;
            if (type == "double") return 64;
            return 32;
        }

        // New Helper: Only allows bitfields for unsigned integers
        private bool IsUnsignedType(string type)
        {
            if (string.IsNullOrEmpty(type)) return false;
            type = type.ToLower();
            return type.StartsWith("uint") || type == "byte";
        }

        private void StructLinkCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoading && _selectedField is Struct s && StructLinkCombo.SelectedItem is string linkedName)
            {
                s.StructType = linkedName;
                if (IcdTree.SelectedItem is TreeViewItem selectedItem) UpdateTreeHeader(selectedItem, s);
            }
        }

        private void PropIsUnionChk_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isLoading && _selectedField is Struct s) s.IsUnion = PropIsUnionChk.IsChecked == true;
        }

        // ---------------------------------------------------------
        // 4. Save & Export
        // ---------------------------------------------------------

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
                if (res.IsSuccessStatusCode) MessageBox.Show("Saved successfully!");
                else
                {
                    var details = await res.Content.ReadAsStringAsync();
                    MessageBox.Show($"Save failed: {res.ReasonPhrase}\n{details}");
                }
            }
            catch (Exception ex) { MessageBox.Show("Network Error: " + ex.Message); }
        }

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ApiClient.EnsureAuthHeader();
                var response = await ApiClient.Client.GetAsync($"api/icd/{_icdId}/export");

                if (!response.IsSuccessStatusCode)
                {
                    MessageBox.Show("Export failed: " + response.ReasonPhrase);
                    return;
                }

                var cHeaderContent = await response.Content.ReadAsStringAsync();
                var dialog = new SaveFileDialog
                {
                    FileName = $"{_icd.Name}_v{_icd.Version}.h",
                    DefaultExt = ".h",
                    Filter = "C Header Files (*.h)|*.h|All Files (*.*)|*.*"
                };

                if (dialog.ShowDialog() == true)
                {
                    System.IO.File.WriteAllText(dialog.FileName, cHeaderContent);
                    MessageBox.Show($"Exported successfully to:\n{dialog.FileName}");
                }
            }
            catch (Exception ex) { MessageBox.Show($"Error exporting file: {ex.Message}"); }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        // ---------------------------------------------------------
        // 5. Drag & Drop Logic
        // ---------------------------------------------------------

        private void Tree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TreeViewItem item)
            {
                _dragStartPoint = e.GetPosition(null);
                _draggedItemContainer = item;
                _draggedData = item.Tag as BaseField;
            }
        }

        private void Tree_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggedItemContainer != null && _draggedData != null)
            {
                var currentPoint = e.GetPosition(null);
                if (Math.Abs(currentPoint.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(currentPoint.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    DragDrop.DoDragDrop(_draggedItemContainer, _draggedData, DragDropEffects.Copy | DragDropEffects.Move);
                }
            }
        }

        private void Tree_DragOver(object sender, DragEventArgs e)
        {
            if (sender is TreeViewItem targetItem && targetItem.Tag is BaseField)
            {
                if (_draggedData == targetItem.Tag)
                {
                    e.Effects = DragDropEffects.None;
                    e.Handled = true;
                    return;
                }

                e.Effects = DragDropEffects.Copy | DragDropEffects.Move;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
            }
        }

        private void Tree_Drop(object sender, DragEventArgs e)
        {
            if (sender is TreeViewItem targetItem && targetItem.Tag is BaseField targetField)
            {
                if (targetField is Struct targetStruct && IsDroppedOnHeader(targetItem, e))
                {
                    if (_draggedData is Struct draggedStructDef && _icd.Structs.Contains(draggedStructDef))
                    {
                        string baseName = draggedStructDef.Name.ToLower() + "_instance";
                        var newInstance = new Struct
                        {
                            Name = GetUniqueName(targetStruct.Fields, baseName),
                            StructType = draggedStructDef.Name
                        };
                        targetStruct.Fields.Add(newInstance);
                        RefreshTree();
                        RestoreSelection(targetStruct);
                    }
                    else if (_draggedData is DataField draggedField)
                    {
                        string baseName = draggedField.Name;
                        var copy = new DataField
                        {
                            Name = GetUniqueName(targetStruct.Fields, baseName),
                            Type = draggedField.Type,
                            SizeInBits = draggedField.SizeInBits
                        };
                        targetStruct.Fields.Add(copy);
                        RefreshTree();
                        RestoreSelection(targetStruct);
                    }
                }
                else
                {
                    var targetParent = FindParentField(_icd, targetField);
                    var sourceParent = FindParentField(_icd, _draggedData);

                    if (targetParent != null && sourceParent == targetParent)
                    {
                        var point = e.GetPosition(targetItem);
                        bool insertAfter = point.Y > (targetItem.ActualHeight / 2);

                        targetParent.Fields.Remove(_draggedData);
                        int targetIndex = targetParent.Fields.IndexOf(targetField);

                        if (insertAfter) targetIndex++;

                        if (targetIndex >= targetParent.Fields.Count)
                            targetParent.Fields.Add(_draggedData);
                        else
                            targetParent.Fields.Insert(targetIndex, _draggedData);

                        RefreshTree();
                        RestoreSelection(targetParent);
                    }
                }
                e.Handled = true;
            }
        }

        private string GetUniqueName(List<BaseField> fields, string baseName)
        {
            if (fields == null) return baseName;
            if (!fields.Any(f => f.Name == baseName)) return baseName;
            int i = 2;
            while (true)
            {
                string candidate = $"{baseName}_{i}";
                if (!fields.Any(f => f.Name == candidate)) return candidate;
                i++;
            }
        }

        private bool IsDroppedOnHeader(TreeViewItem item, DragEventArgs e) => true;

        private Struct FindParentField(object context, BaseField child)
        {
            if (context is Icd root)
            {
                foreach (var m in root.Messages)
                {
                    if (m.Fields.Contains(child)) return m;
                    var res = FindParentField(m, child);
                    if (res != null) return res;
                }
                foreach (var s in root.Structs)
                {
                    if (s.Fields.Contains(child)) return s;
                    var res = FindParentField(s, child);
                    if (res != null) return res;
                }
            }
            else if (context is Struct s)
            {
                foreach (var sub in s.Fields.OfType<Struct>())
                {
                    if (sub.Fields.Contains(child)) return sub;
                    var res = FindParentField(sub, child);
                    if (res != null) return res;
                }
            }
            return null;
        }
    }
}