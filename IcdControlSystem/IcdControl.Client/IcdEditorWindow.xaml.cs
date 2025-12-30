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
using System.Text.Json.Serialization;

namespace IcdControl.Client
{
    public class ValidationResult
    {
        [JsonPropertyName("isValid")]
        public bool IsValid { get; set; }
        
        [JsonPropertyName("errors")]
        public List<string> Errors { get; set; } = new List<string>();
        
        [JsonPropertyName("warnings")]
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public partial class IcdEditorWindow : Window
    {
        private string _icdId;
        private Icd _icd;
        private bool _isLoading = true;
        private BaseField _selectedField;

        private Point _dragStartPoint;
        private TreeViewItem _draggedItemContainer;
        private BaseField _draggedData;

        // Undo/Redo
        private Stack<string> _undoStack = new Stack<string>();
        private Stack<string> _redoStack = new Stack<string>();
        private BaseField _copiedField;

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
                VersionTxt.Text = FormatVersion(_icd.Version);

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

            // Create a new context menu instance to avoid event handler duplication
            var msgContextMenu = new ContextMenu();
            var msgMenuItem = new MenuItem { Header = "Add New Item", FontWeight = FontWeights.Bold };
            msgMenuItem.Click += AddMessageBtn_Click;
            msgContextMenu.Items.Add(msgMenuItem);
            msgRoot.ContextMenu = msgContextMenu;

            if (_icd.Messages != null)
                foreach (var m in _icd.Messages) msgRoot.Items.Add(CreateTreeItem(m));
            IcdTree.Items.Add(msgRoot);

            var structRoot = new TreeViewItem
            {
                Header = "Structs",
                FontWeight = FontWeights.Bold,
                Tag = "StructsRoot",
                AllowDrop = true
            };
            structRoot.DragOver += Tree_DragOver;
            structRoot.Drop += Tree_Drop;

            // Create a new context menu instance to avoid event handler duplication
            var structContextMenu = new ContextMenu();
            var structMenuItem = new MenuItem { Header = "Add New Item", FontWeight = FontWeights.Bold };
            structMenuItem.Click += AddStructBtn_Click;
            structContextMenu.Items.Add(structMenuItem);
            structRoot.ContextMenu = structContextMenu;

            if (_icd.Structs != null)
                foreach (var s in _icd.Structs) structRoot.Items.Add(CreateTreeItem(s));
            IcdTree.Items.Add(structRoot);
        }

        private TreeViewItem CreateTreeItem(BaseField field)
        {
            var item = new TreeViewItem { Tag = field };
            UpdateTreeHeader(item, field);
            
            // Create context menu based on field type
            if (field is Struct st && !string.IsNullOrEmpty(st.StructType))
            {
                // Nested struct (instance) - cannot be modified, it's just a reference
                // Only allow deletion
                var nestedContextMenu = new ContextMenu();
                var deleteItem = new MenuItem { Header = "Delete", Foreground = System.Windows.Media.Brushes.Red };
                deleteItem.Click += Ctx_Delete_Click;
                nestedContextMenu.Items.Add(deleteItem);
                item.ContextMenu = nestedContextMenu;
            }
            else
            {
                // Regular struct or message - allow adding both fields and structs
                item.ContextMenu = (ContextMenu)Resources["ItemContextMenu"];
            }

            item.AllowDrop = true;
            item.PreviewMouseLeftButtonDown += Tree_PreviewMouseLeftButtonDown;
            item.MouseMove += Tree_MouseMove;
            item.DragOver += Tree_DragOver;
            item.Drop += Tree_Drop;

            // Only show children if it's not a nested struct (nested structs should be empty)
            if (field is Struct s && s.Fields != null && string.IsNullOrEmpty(s.StructType))
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
            SaveState();
            var newMsg = new Message { Name = "New_Message" };
            if (_icd.Messages == null) _icd.Messages = new List<Message>();
            
            // Ensure unique name at root level
            newMsg.Name = GetUniqueRootName(_icd.Messages.Select(m => m.Name).ToList(), newMsg.Name);
            
            _icd.Messages.Add(newMsg);
            RefreshTree();
            RestoreSelection(newMsg);
        }

        private void AddStructBtn_Click(object sender, RoutedEventArgs e)
        {
            SaveState();
            var newStruct = new Struct { Name = "New_Struct" };
            if (_icd.Structs == null) _icd.Structs = new List<Struct>();
            
            // Ensure unique name at root level
            newStruct.Name = GetUniqueRootName(_icd.Structs.Select(s => s.Name).ToList(), newStruct.Name);
            
            _icd.Structs.Add(newStruct);
            RefreshTree();
            RestoreSelection(newStruct);
        }

        private string GetUniqueRootName(List<string> existingNames, string baseName)
        {
            if (existingNames == null || !existingNames.Contains(baseName))
                return baseName;
            
            int counter = 2;
            while (true)
            {
                string candidate = $"{baseName}_{counter}";
                if (!existingNames.Contains(candidate))
                    return candidate;
                counter++;
            }
        }

        private void Ctx_AddField_Click(object sender, RoutedEventArgs e)
        {
            if (IcdTree.SelectedItem is TreeViewItem item && item.Tag is Struct parentStruct)
            {
                // Check if this is a nested struct (has StructType) - nested structs cannot be modified
                if (!string.IsNullOrEmpty(parentStruct.StructType))
                {
                    MessageBox.Show("Nested structs (struct instances) are references to existing structs and cannot be modified. They must remain empty.", 
                        "Invalid Operation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                SaveState();
                var newField = new DataField { Name = "new_field", Type = "uint32_t", SizeInBits = 32 };
                
                // Ensure unique name - check in current level and all nested levels
                newField.Name = GetUniqueNameInContext(parentStruct, newField.Name);
                
                if (parentStruct.Fields == null)
                    parentStruct.Fields = new List<BaseField>();
                
                parentStruct.Fields.Add(newField);
                RefreshTree();
                RestoreSelection(parentStruct);
            }
        }

        private void Ctx_AddStruct_Click(object sender, RoutedEventArgs e)
        {
            if (IcdTree.SelectedItem is TreeViewItem item && item.Tag is Struct parentStruct)
            {
                // Check if this is a nested struct (has StructType) - nested structs cannot contain other structs
                if (!string.IsNullOrEmpty(parentStruct.StructType))
                {
                    MessageBox.Show("Nested structs (struct instances) cannot contain other structs. Only data fields are allowed.", 
                        "Invalid Operation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                SaveState();
                var newStruct = new Struct { Name = "nested_struct" };
                
                // Ensure unique name - check in current level and all nested levels
                newStruct.Name = GetUniqueNameInContext(parentStruct, newStruct.Name);
                
                if (parentStruct.Fields == null)
                    parentStruct.Fields = new List<BaseField>();
                
                parentStruct.Fields.Add(newStruct);
                RefreshTree();
                RestoreSelection(parentStruct);
            }
        }

        // Get unique name checking all nested levels
        private string GetUniqueNameInContext(Struct parentStruct, string baseName)
        {
            if (parentStruct.Fields == null) return baseName;
            
            // First check current level
            var currentLevelNames = parentStruct.Fields.Select(f => f.Name).ToHashSet();
            if (!currentLevelNames.Contains(baseName))
            {
                // Also check nested levels
                if (!HasNameInNested(parentStruct.Fields, baseName))
                    return baseName;
            }
            
            // Name exists, find unique one
            int counter = 2;
            while (true)
            {
                string candidate = $"{baseName}_{counter}";
                if (!currentLevelNames.Contains(candidate) && !HasNameInNested(parentStruct.Fields, candidate))
                    return candidate;
                counter++;
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
                SaveState();
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
            
            // Parse and format version
            if (double.TryParse(VersionTxt.Text, out var v))
            {
                _icd.Version = v;
            }
            
            // Ensure version is always decimal format (at least one digit after decimal point)
            _icd.Version = EnsureDecimalVersion(_icd.Version);

            ApiClient.EnsureAuthHeader();
            _icd.Messages ??= new List<Message>();
            _icd.Structs ??= new List<Struct>();

            // Validate before saving
            var validationErrors = ValidateIcd(_icd);
            if (validationErrors.Count > 0)
            {
                MessageBox.Show("Cannot save: Validation errors found:\n\n" + string.Join("\n", validationErrors), 
                    "Validation Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Ensure all names are unique
            EnsureUniqueNames(_icd);

            // Increment version automatically
            _icd.Version = IncrementVersion(_icd.Version);
            VersionTxt.Text = FormatVersion(_icd.Version);

            try
            {
                var res = await ApiClient.Client.PostAsJsonAsync("api/icd/save", _icd);
                if (res.IsSuccessStatusCode)
                {
                    SaveState(); // Save state after successful save
                    RefreshTree(); // Refresh to show updated names
                    MessageBox.Show($"Saved successfully! Version updated to {FormatVersion(_icd.Version)}");
                }
                else
                {
                    var details = await res.Content.ReadAsStringAsync();
                    MessageBox.Show($"Save failed: {res.ReasonPhrase}\n{details}");
                }
            }
            catch (Exception ex) { MessageBox.Show("Network Error: " + ex.Message); }
        }

        // Format version to always show at least one decimal place
        private string FormatVersion(double version)
        {
            // Check if version is a whole number
            if (version == Math.Floor(version))
            {
                return version.ToString("F1"); // Always show .0
            }
            
            // Count decimal places
            string versionStr = version.ToString("G");
            if (versionStr.Contains('.'))
            {
                return versionStr;
            }
            
            return version.ToString("F1");
        }

        // Ensure version has at least one decimal place
        private double EnsureDecimalVersion(double version)
        {
            // If version is a whole number, add .0
            if (version == Math.Floor(version))
            {
                return version; // Already whole number, will be formatted as X.0
            }
            return version;
        }

        // Increment version: increment rightmost digit, if 9 then increment left digit
        // Examples: 1.1 -> 1.2, 1.9 -> 2.0, 1.15 -> 1.16, 1.99 -> 2.00
        private double IncrementVersion(double version)
        {
            // Convert to string with enough precision
            string versionStr = version.ToString("F10").TrimEnd('0');
            if (!versionStr.Contains('.'))
            {
                versionStr += ".0";
            }
            
            string[] parts = versionStr.Split('.');
            string integerPart = parts[0];
            string decimalPart = parts[1];
            
            // Convert to integers for manipulation
            if (int.TryParse(integerPart, out int intValue) && int.TryParse(decimalPart, out int decimalValue))
            {
                // Get the rightmost digit of decimal part
                int lastDigit = decimalValue % 10;
                int restOfDecimal = decimalValue / 10;
                
                if (lastDigit < 9)
                {
                    // Simple increment: 1.1 -> 1.2, 1.15 -> 1.16
                    decimalValue++;
                }
                else
                {
                    // Last digit is 9, need to increment the digit before it
                    if (restOfDecimal > 0)
                    {
                        // 1.19 -> 1.20, 1.99 -> 2.00
                        restOfDecimal++;
                        decimalValue = restOfDecimal * 10;
                        
                        // If decimal part overflows (e.g., 1.99 -> 2.00)
                        if (decimalValue >= 100)
                        {
                            intValue++;
                            decimalValue = 0;
                        }
                    }
                    else
                    {
                        // 1.9 -> 2.0, 0.9 -> 1.0
                        intValue++;
                        decimalValue = 0;
                    }
                }
                
                // Reconstruct version
                return double.Parse($"{intValue}.{decimalValue}");
            }
            
            // Fallback: simple increment by 0.1
            return version + 0.1;
        }

        private List<string> ValidateIcd(Icd icd)
        {
            var errors = new List<string>();

            // Validate messages
            if (icd.Messages != null)
            {
                foreach (var msg in icd.Messages)
                {
                    if (string.IsNullOrWhiteSpace(msg.Name))
                        errors.Add($"Message has no name");

                    // Check if message has fields
                    if (msg.Fields == null || msg.Fields.Count == 0)
                    {
                        errors.Add($"Message '{msg.Name}' is empty (no fields). Please add at least one field.");
                    }
                    else
                    {
                        // Validate nested fields
                        ValidateFields(msg.Fields, errors, $"Message '{msg.Name}'");
                    }
                }
            }

            // Validate structs
            if (icd.Structs != null)
            {
                foreach (var st in icd.Structs)
                {
                    if (string.IsNullOrWhiteSpace(st.Name))
                        errors.Add($"Struct has no name");

                    // Check if struct has fields
                    if (st.Fields == null || st.Fields.Count == 0)
                    {
                        errors.Add($"Struct '{st.Name}' is empty (no fields). Please add at least one field.");
                    }
                    else
                    {
                        // Validate nested fields
                        ValidateFields(st.Fields, errors, $"Struct '{st.Name}'");
                    }
                }
            }

            return errors;
        }

        private void ValidateFields(List<BaseField> fields, List<string> errors, string context)
        {
            if (fields == null) return;

            var fieldNames = new HashSet<string>();
            foreach (var field in fields)
            {
                if (string.IsNullOrWhiteSpace(field.Name))
                {
                    errors.Add($"{context}: Field has no name");
                }
                else if (fieldNames.Contains(field.Name))
                {
                    errors.Add($"{context}: Duplicate field name '{field.Name}'. Field names must be unique.");
                }
                else
                {
                    fieldNames.Add(field.Name);
                }

                if (field is DataField df)
                {
                    if (string.IsNullOrWhiteSpace(df.Type))
                        errors.Add($"{context}: Field '{field.Name}' has no type");
                    if (df.SizeInBits < 0)
                        errors.Add($"{context}: Field '{field.Name}' has invalid size");
                }
                else if (field is Struct nested)
                {
                    // Nested structs (with StructType) can be empty - they are just references
                    // Only regular structs need to have fields
                    if (string.IsNullOrEmpty(nested.StructType))
                    {
                        // Regular struct - must have fields
                        if (nested.Fields == null || nested.Fields.Count == 0)
                        {
                            errors.Add($"{context}: Struct '{nested.Name}' is empty (no fields). Please add at least one field.");
                        }
                        else
                        {
                            ValidateFields(nested.Fields, errors, $"{context}.{nested.Name}");
                        }
                    }
                    else
                    {
                        // Nested struct instance (reference) - can be empty, but if it has fields, validate them
                        if (nested.Fields != null && nested.Fields.Count > 0)
                        {
                            ValidateFields(nested.Fields, errors, $"{context}.{nested.Name}");
                        }
                    }
                }
            }
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

        private async void VersionsBtn_Click(object sender, RoutedEventArgs e)
        {
            var win = new VersionsWindow(_icdId);
            win.Owner = this;
            if (win.ShowDialog() == true)
            {
                // Reload ICD if rollback was performed
                await ReloadIcd();
            }
        }

        private async System.Threading.Tasks.Task ReloadIcd()
        {
            try
            {
                _icd = await ApiClient.Client.GetFromJsonAsync<Icd>($"api/icd/{_icdId}");
                if (_icd == null) throw new Exception("ICD not found");

                NameTxt.Text = _icd.Name;
                VersionTxt.Text = FormatVersion(_icd.Version);

                RefreshTree();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to reload ICD: " + ex.Message);
            }
        }

        private async void CommentsBtn_Click(object sender, RoutedEventArgs e)
        {
            var win = new CommentsWindow(_icdId);
            win.Owner = this;
            win.ShowDialog();
        }

        private async void HistoryBtn_Click(object sender, RoutedEventArgs e)
        {
            var win = new HistoryWindow(_icdId);
            win.Owner = this;
            win.ShowDialog();
        }

        private void VersionTxt_LostFocus(object sender, RoutedEventArgs e)
        {
            // When user leaves the version field, ensure it's in decimal format
            if (double.TryParse(VersionTxt.Text, out var v))
            {
                _icd.Version = EnsureDecimalVersion(v);
                VersionTxt.Text = FormatVersion(_icd.Version);
            }
        }

        private async void ValidateBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _icd.Name = NameTxt.Text;
                if (double.TryParse(VersionTxt.Text, out var v)) 
                {
                    _icd.Version = EnsureDecimalVersion(v);
                    VersionTxt.Text = FormatVersion(_icd.Version);
                }

                var response = await ApiClient.Client.PostAsJsonAsync($"api/icd/{_icdId}/validate", new { });
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<ValidationResult>();
                    if (result != null)
                    {
                        var errors = result.Errors ?? new List<string>();
                        var warnings = result.Warnings ?? new List<string>();

                        string message = "";
                        if (errors.Count > 0)
                        {
                            message += "Errors:\n" + string.Join("\n", errors) + "\n\n";
                        }
                        if (warnings.Count > 0)
                        {
                            message += "Warnings:\n" + string.Join("\n", warnings);
                        }
                        if (string.IsNullOrEmpty(message))
                        {
                            message = "Validation passed! No errors or warnings.";
                        }

                        MessageBox.Show(message, "Validation Results", MessageBoxButton.OK, 
                            errors.Count > 0 ? MessageBoxImage.Error : MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Validation failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

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
                    // Check if target is a nested struct - nested structs cannot be modified
                    if (!string.IsNullOrEmpty(targetStruct.StructType))
                    {
                        MessageBox.Show("Nested structs (struct instances) are references to existing structs and cannot be modified. They must remain empty.", 
                            "Invalid Operation", MessageBoxButton.OK, MessageBoxImage.Warning);
                        e.Handled = true;
                        return;
                    }
                    
                    if (_draggedData is Struct draggedStructDef && _icd.Structs.Contains(draggedStructDef))
                    {
                        string baseName = draggedStructDef.Name.ToLower() + "_instance";
                        var newInstance = new Struct
                        {
                            Name = GetUniqueName(targetStruct.Fields, baseName),
                            StructType = draggedStructDef.Name,
                            Fields = new List<BaseField>() // Nested structs must be empty
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
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "unnamed";
            
            // Check if name already exists in this level
            if (!fields.Any(f => f.Name == baseName)) return baseName;
            
            // Also check in all nested structs recursively
            if (HasNameInNested(fields, baseName))
            {
                int i = 2;
                while (true)
                {
                    string candidate = $"{baseName}_{i}";
                    if (!fields.Any(f => f.Name == candidate) && !HasNameInNested(fields, candidate))
                        return candidate;
                    i++;
                }
            }
            
            // If name exists only in current level, add number
            int num = 2;
            while (true)
            {
                string candidate = $"{baseName}_{num}";
                if (!fields.Any(f => f.Name == candidate) && !HasNameInNested(fields, candidate))
                    return candidate;
                num++;
            }
        }

        private bool HasNameInNested(List<BaseField> fields, string name)
        {
            if (fields == null) return false;
            foreach (var field in fields)
            {
                if (field.Name == name) return true;
                if (field is Struct s && s.Fields != null)
                {
                    if (HasNameInNested(s.Fields, name)) return true;
                }
            }
            return false;
        }

        // Check for duplicate names across all levels
        private void EnsureUniqueNames(Icd icd)
        {
            // Check root level messages
            if (icd.Messages != null)
            {
                var messageNames = new HashSet<string>();
                foreach (var msg in icd.Messages)
                {
                    if (string.IsNullOrWhiteSpace(msg.Name))
                        msg.Name = "New_Message";
                    
                    string originalName = msg.Name;
                    int counter = 1;
                    while (messageNames.Contains(msg.Name))
                    {
                        msg.Name = $"{originalName}_{counter}";
                        counter++;
                    }
                    messageNames.Add(msg.Name);
                    
                    // Ensure unique names in message fields
                    if (msg.Fields != null)
                        EnsureUniqueNamesInFields(msg.Fields);
                }
            }

            // Check root level structs
            if (icd.Structs != null)
            {
                var structNames = new HashSet<string>();
                foreach (var st in icd.Structs)
                {
                    if (string.IsNullOrWhiteSpace(st.Name))
                        st.Name = "New_Struct";
                    
                    string originalName = st.Name;
                    int counter = 1;
                    while (structNames.Contains(st.Name))
                    {
                        st.Name = $"{originalName}_{counter}";
                        counter++;
                    }
                    structNames.Add(st.Name);
                    
                    // Ensure unique names in struct fields
                    if (st.Fields != null)
                        EnsureUniqueNamesInFields(st.Fields);
                }
            }
        }

        private void EnsureUniqueNamesInFields(List<BaseField> fields)
        {
            if (fields == null) return;
            var fieldNames = new HashSet<string>();
            
            foreach (var field in fields)
            {
                if (string.IsNullOrWhiteSpace(field.Name))
                {
                    if (field is DataField) field.Name = "new_field";
                    else if (field is Struct) field.Name = "nested_struct";
                }
                
                string originalName = field.Name;
                int counter = 1;
                while (fieldNames.Contains(field.Name))
                {
                    field.Name = $"{originalName}_{counter}";
                    counter++;
                }
                fieldNames.Add(field.Name);
                
                // Recursively check nested structs
                if (field is Struct nested && nested.Fields != null)
                    EnsureUniqueNamesInFields(nested.Fields);
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

        // ---------------------------------------------------------
        // Undo/Redo
        // ---------------------------------------------------------
        private void SaveState()
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(_icd);
                _undoStack.Push(json);
                _redoStack.Clear(); // Clear redo when new action is performed
                if (_undoStack.Count > 50) // Limit stack size
                {
                    var temp = new Stack<string>();
                    for (int i = 0; i < 40; i++) temp.Push(_undoStack.Pop());
                    _undoStack = temp;
                }
            }
            catch { }
        }

        private void UndoBtn_Click(object sender, RoutedEventArgs e) => Undo();
        private void RedoBtn_Click(object sender, RoutedEventArgs e) => Redo();

        private void Undo()
        {
            if (_undoStack.Count == 0) return;
            try
            {
                var current = System.Text.Json.JsonSerializer.Serialize(_icd);
                _redoStack.Push(current);
                var previous = _undoStack.Pop();
                _icd = System.Text.Json.JsonSerializer.Deserialize<Icd>(previous);
                RefreshTree();
            }
            catch { }
        }

        private void Redo()
        {
            if (_redoStack.Count == 0) return;
            try
            {
                var current = System.Text.Json.JsonSerializer.Serialize(_icd);
                _undoStack.Push(current);
                var next = _redoStack.Pop();
                _icd = System.Text.Json.JsonSerializer.Deserialize<Icd>(next);
                RefreshTree();
            }
            catch { }
        }

        // ---------------------------------------------------------
        // Copy/Paste
        // ---------------------------------------------------------
        private void CopyBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedField != null)
            {
                _copiedField = CloneField(_selectedField);
                MessageBox.Show("Item copied to clipboard.", "Copy", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Please select an item to copy.", "Copy", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void PasteBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_copiedField == null)
            {
                MessageBox.Show("No item copied. Please copy an item first.", "Paste", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_selectedField is Struct targetStruct)
            {
                // Check if target is a nested struct - nested structs cannot be modified
                if (!string.IsNullOrEmpty(targetStruct.StructType))
                {
                    MessageBox.Show("Nested structs (struct instances) are references to existing structs and cannot be modified. They must remain empty.", 
                        "Invalid Operation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                SaveState();
                var pasted = CloneField(_copiedField);
                pasted.Name = GetUniqueName(targetStruct.Fields, pasted.Name);
                targetStruct.Fields.Add(pasted);
                RefreshTree();
                RestoreSelection(targetStruct);
            }
            else if (IcdTree.SelectedItem is TreeViewItem item && item.Tag is Struct parentStruct)
            {
                // Check if target is a nested struct - nested structs cannot be modified
                if (!string.IsNullOrEmpty(parentStruct.StructType))
                {
                    MessageBox.Show("Nested structs (struct instances) are references to existing structs and cannot be modified. They must remain empty.", 
                        "Invalid Operation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                SaveState();
                var pasted = CloneField(_copiedField);
                pasted.Name = GetUniqueName(parentStruct.Fields, pasted.Name);
                parentStruct.Fields.Add(pasted);
                RefreshTree();
                RestoreSelection(parentStruct);
            }
            else
            {
                MessageBox.Show("Please select a struct or message to paste into.", "Paste", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private BaseField CloneField(BaseField field)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(field);
            return System.Text.Json.JsonSerializer.Deserialize<BaseField>(json, new System.Text.Json.JsonSerializerOptions
            {
                Converters = { new IcdControl.Models.BaseFieldJsonConverter() }
            });
        }

        // ---------------------------------------------------------
        // Preview
        // ---------------------------------------------------------
        private async void PreviewBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Save current state temporarily
                var tempJson = System.Text.Json.JsonSerializer.Serialize(_icd);
                var tempIcd = System.Text.Json.JsonSerializer.Deserialize<Icd>(tempJson);

                // Update with current values
                if (double.TryParse(VersionTxt.Text, out double version))
                    tempIcd.Version = version;
                tempIcd.Name = NameTxt.Text;

                // Generate preview
                var previewJson = System.Text.Json.JsonSerializer.Serialize(tempIcd);
                var response = await ApiClient.Client.PostAsJsonAsync("api/icd/preview", new { Icd = tempIcd });
                if (response.IsSuccessStatusCode)
                {
                    var cHeader = await response.Content.ReadAsStringAsync();
                    var previewWin = new PreviewWindow(cHeader);
                    previewWin.Owner = this;
                    previewWin.ShowDialog();
                }
                else
                {
                    // Fallback: generate locally
                    var cHeader = GenerateCHeaderLocal(tempIcd);
                    var previewWin = new PreviewWindow(cHeader);
                    previewWin.Owner = this;
                    previewWin.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to generate preview: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GenerateCHeaderLocal(Icd icd)
        {
            // Simplified local C header generation
            var sb = new System.Text.StringBuilder();
            string safeName = MakeSafeName(icd.Name).ToUpper();
            sb.AppendLine($"#ifndef {safeName}_H");
            sb.AppendLine($"#define {safeName}_H");
            sb.AppendLine();
            sb.AppendLine($"/* Generated ICD Header: {icd.Name} */");
            sb.AppendLine($"/* Version: {icd.Version} */");
            sb.AppendLine();
            sb.AppendLine("#include <stdint.h>");
            sb.AppendLine("#include <stdbool.h>");
            sb.AppendLine();

            if (icd.Structs != null)
            {
                foreach (var st in icd.Structs)
                {
                    sb.AppendLine($"typedef struct {{");
                    if (st.Fields != null)
                        foreach (var f in st.Fields.OfType<DataField>())
                            sb.AppendLine($"    {f.Type} {MakeSafeName(f.Name)};");
                    sb.AppendLine($"}} {MakeSafeName(st.Name)}_t;");
                    sb.AppendLine();
                }
            }

            if (icd.Messages != null)
            {
                foreach (var msg in icd.Messages)
                {
                    sb.AppendLine($"typedef struct {{");
                    if (msg.Fields != null)
                        foreach (var f in msg.Fields.OfType<DataField>())
                            sb.AppendLine($"    {f.Type} {MakeSafeName(f.Name)};");
                    sb.AppendLine($"}} {MakeSafeName(msg.Name)}_t;");
                    sb.AppendLine();
                }
            }

            sb.AppendLine($"#endif /* {safeName}_H */");
            return sb.ToString();
        }

        private string MakeSafeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "unknown";
            return new string(name.Select(c => (char.IsLetterOrDigit(c) || c == '_') ? c : '_').ToArray());
        }

        // ---------------------------------------------------------
        // Keyboard Shortcuts
        // ---------------------------------------------------------
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                switch (e.Key)
                {
                    case Key.Z:
                        Undo();
                        e.Handled = true;
                        break;
                    case Key.Y:
                        Redo();
                        e.Handled = true;
                        break;
                    case Key.C:
                        CopyBtn_Click(sender, e);
                        e.Handled = true;
                        break;
                    case Key.V:
                        PasteBtn_Click(sender, e);
                        e.Handled = true;
                        break;
                    case Key.S:
                        Save_Click(sender, e);
                        e.Handled = true;
                        break;
                }
            }
        }

    }
}