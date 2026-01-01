using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Globalization;
using Microsoft.Win32;
using System.IO;
using System.Text;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections;
using IcdControl.Models;

namespace IcdControl.Client
{
    // =========================================================
    // 1. CONVERTERS
    // =========================================================
    public class TypeToIconConverter : IValueConverter
    {
        private static readonly List<string> Primitives = new List<string>
        { "uint8", "int8", "uint16", "int16", "uint32", "int32", "uint64", "int64", "float", "double", "bool", "char", "string" };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string typeStr)
            {
                if (Primitives.Contains(typeStr)) return "🔹";
                return "🔗";
            }
            return "🔹";
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class TypeToColorConverter : IValueConverter
    {
        private static readonly List<string> Primitives = new List<string>
        { "uint8", "int8", "uint16", "int16", "uint32", "int32", "uint64", "int64", "float", "double", "bool", "char", "string" };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string typeStr)
            {
                if (Primitives.Contains(typeStr)) return new SolidColorBrush(Color.FromRgb(96, 165, 250));
                return new SolidColorBrush(Color.FromRgb(245, 158, 11));
            }
            return Brushes.Gray;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    // =========================================================
    // 2. MAIN WINDOW LOGIC
    // =========================================================
    public partial class IcdEditorWindow : Window
    {
        private string _icdId;
        private Icd _currentIcd;

        private Point _startPoint;
        private object _draggedItem;

        private bool _versionChangedManually = false;

        public ObservableCollection<Message> MessageList { get; set; } = new ObservableCollection<Message>();
        public ObservableCollection<IcdControl.Models.Struct> StructList { get; set; } = new ObservableCollection<IcdControl.Models.Struct>();

        public List<string> PrimitiveTypes { get; set; } = new List<string>
        {
            "uint8", "int8", "uint16", "int16", "uint32", "int32",
            "uint64", "int64", "float", "double", "bool", "char", "string"
        };

        public ObservableCollection<string> StructTypes { get; set; } = new ObservableCollection<string>();

        private bool _isChangingSelection = false;

        public IcdEditorWindow(string icdId)
        {
            InitializeComponent();
            _icdId = icdId;
            this.DataContext = this;

            MessagesTree.ItemsSource = MessageList;
            StructsTree.ItemsSource = StructList;

            Loaded += IcdEditorWindow_Loaded;
        }

        private async void IcdEditorWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_icdId)) return;
            try
            {
                _currentIcd = await ApiClient.Client.GetFromJsonAsync<Icd>($"api/icd/{_icdId}");
                if (_currentIcd != null)
                {
                    NameTxt.Text = _currentIcd.Name;

                    string verStr = _currentIcd.Version.ToString();
                    if (!verStr.Contains(".")) verStr += ".0";
                    VersionTxt.Text = verStr;

                    DescTxt.Text = _currentIcd.Description;
                    if (ApiClient.CurrentUser != null) LastUserTxt.Text = ApiClient.CurrentUser.Username;

                    RebuildTrees();
                    RefreshTypes();

                    _versionChangedManually = false;
                }
            }
            catch (Exception ex) { MessageBox.Show($"Error loading ICD: {ex.Message}"); }
        }

        private void VersionTxt_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (IsLoaded)
            {
                _versionChangedManually = true;
            }
        }

        private void RefreshTypes()
        {
            var currentStructNames = _currentIcd?.Structs?
                .Select(s => s.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList() ?? new List<string>();

            for (int i = StructTypes.Count - 1; i >= 0; i--)
            {
                if (!currentStructNames.Contains(StructTypes[i])) StructTypes.RemoveAt(i);
            }

            foreach (var name in currentStructNames)
            {
                if (!StructTypes.Contains(name)) StructTypes.Add(name);
            }
        }

        private void RebuildTrees()
        {
            MessageList.Clear();
            StructList.Clear();

            if (_currentIcd.Messages != null)
            {
                foreach (var msg in _currentIcd.Messages) MessageList.Add(msg);
            }

            if (_currentIcd.Structs != null)
            {
                foreach (var s in _currentIcd.Structs) StructList.Add(s);
            }
        }

        // =========================================================
        // 3. DRAG & DROP LOGIC
        // =========================================================

        private void Tree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(null);
        }

        private void Tree_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !IsDragging(_startPoint, e.GetPosition(null)))
            {
                TreeView tree = sender as TreeView;
                object data = GetObjectFromPoint(tree, e.GetPosition(tree));

                if (data != null)
                {
                    _draggedItem = data;
                    DragDrop.DoDragDrop(tree, data, DragDropEffects.Move | DragDropEffects.Copy);
                }
            }
        }

        private bool IsDragging(Point start, Point current)
        {
            return Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
                   Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance;
        }

        private object GetObjectFromPoint(TreeView tree, Point point)
        {
            HitTestResult result = VisualTreeHelper.HitTest(tree, point);
            if (result == null) return null;
            TreeViewItem item = Utils.FindVisualParent<TreeViewItem>(result.VisualHit);
            return item?.DataContext;
        }

        private void Tree_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;
            TreeView tree = sender as TreeView;
            object target = GetObjectFromPoint(tree, e.GetPosition(tree));

            if (_draggedItem is DataField)
            {
                if (target is Message || target is IcdControl.Models.Struct || target is DataField)
                    e.Effects = DragDropEffects.Move;
            }
            else if (_draggedItem is IcdControl.Models.Struct)
            {
                if (target is Message || (target is IcdControl.Models.Struct s && s != _draggedItem))
                    e.Effects = DragDropEffects.Copy;
            }
            e.Handled = true;
        }

        private void Tree_Drop(object sender, DragEventArgs e)
        {
            TreeView tree = sender as TreeView;
            object target = GetObjectFromPoint(tree, e.GetPosition(tree));

            if (target == null || _draggedItem == null) return;

            object containerToExpand = null;
            if (target is Message || target is IcdControl.Models.Struct)
            {
                containerToExpand = target;
            }
            else if (target is DataField df)
            {
                containerToExpand = FindParentContainer(df).Container;
            }

            if (_draggedItem is IcdControl.Models.Struct draggedStruct)
            {
                var targetList = GetListFromContainer(target);
                if (targetList != null && target != draggedStruct)
                {
                    string baseName = draggedStruct.Name.ToLower() + "_inst";
                    string uniqueName = GetUniqueName(baseName, targetList);

                    var newField = new DataField
                    {
                        Name = uniqueName,
                        Type = draggedStruct.Name,
                        SizeInBits = 0
                    };

                    targetList.Add(newField);
                    RefreshTreesVisual(containerToExpand);
                }
            }
            else if (_draggedItem is DataField draggedField)
            {
                var sourceList = FindParentList(draggedField);
                bool droppedOnField = target is DataField;
                var targetList = droppedOnField ? FindParentList((DataField)target) : GetListFromContainer(target);

                if (sourceList != null && targetList != null)
                {
                    if (sourceList == targetList)
                    {
                        if (droppedOnField)
                        {
                            int oldIndex = sourceList.IndexOf(draggedField);
                            int newIndex = sourceList.IndexOf((DataField)target);

                            if (oldIndex != -1 && newIndex != -1 && oldIndex != newIndex)
                            {
                                var item = sourceList[oldIndex];
                                sourceList.RemoveAt(oldIndex);
                                sourceList.Insert(newIndex, item);
                                RefreshTreesVisual(containerToExpand);
                            }
                        }
                        else
                        {
                            CopyFieldToList(draggedField, targetList, -1);
                            RefreshTreesVisual(containerToExpand);
                        }
                    }
                    else
                    {
                        int insertIndex = -1;
                        if (droppedOnField)
                        {
                            insertIndex = targetList.IndexOf((DataField)target) + 1;
                        }
                        CopyFieldToList(draggedField, targetList, insertIndex);
                        RefreshTreesVisual(containerToExpand);
                    }
                }
            }
        }

        private void CopyFieldToList(DataField original, IList targetList, int index)
        {
            string baseName = "copy_of_" + original.Name;
            string uniqueName = GetUniqueName(baseName, targetList);

            var copy = new DataField
            {
                Name = uniqueName,
                Type = original.Type,
                SizeInBits = original.SizeInBits
            };

            if (index >= 0 && index <= targetList.Count)
                targetList.Insert(index, copy);
            else
                targetList.Add(copy);
        }

        private string GetUniqueName(string baseName, IList targetList)
        {
            var existingNames = new HashSet<string>();
            foreach (var item in targetList)
            {
                if (item is DataField f) existingNames.Add(f.Name);
            }

            if (!existingNames.Contains(baseName)) return baseName;

            int counter = 1;
            while (true)
            {
                string candidate = $"{baseName}_{counter}";
                if (!existingNames.Contains(candidate)) return candidate;
                counter++;
            }
        }

        // --- List Helpers ---

        private IList FindParentList(DataField f)
        {
            foreach (var m in MessageList)
                if (m.Fields != null && ((IList)m.Fields).Contains(f)) return (IList)m.Fields;

            foreach (var s in StructList)
                if (s.Fields != null && ((IList)s.Fields).Contains(f)) return (IList)s.Fields;

            return null;
        }

        private (object Container, IList List) FindParentContainer(DataField f)
        {
            foreach (var m in MessageList)
                if (m.Fields != null && ((IList)m.Fields).Contains(f)) return (m, (IList)m.Fields);

            foreach (var s in StructList)
                if (s.Fields != null && ((IList)s.Fields).Contains(f)) return (s, (IList)s.Fields);

            return (null, null);
        }

        private IList GetListFromContainer(object container)
        {
            if (container is Message m)
            {
                if (m.Fields == null) m.Fields = new List<BaseField>();
                return (IList)m.Fields;
            }
            if (container is IcdControl.Models.Struct s)
            {
                if (s.Fields == null) s.Fields = new List<BaseField>();
                return (IList)s.Fields;
            }
            return null;
        }

        // --- Visual Refresh Helpers ---

        private void UnselectAllInTree(TreeView tree)
        {
            if (tree == null) return;
            foreach (var item in tree.Items)
            {
                var container = tree.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                if (container != null)
                {
                    if (container.IsSelected) container.IsSelected = false;
                    UnselectAllRecursive(container);
                }
            }
        }

        private void UnselectAllRecursive(TreeViewItem parent)
        {
            foreach (var item in parent.Items)
            {
                var container = parent.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                if (container != null)
                {
                    if (container.IsSelected) container.IsSelected = false;
                    UnselectAllRecursive(container);
                }
            }
        }

        private void RefreshTreesVisual(object expandNode = null)
        {
            var expandedMsgNames = GetExpandedNames(MessagesTree, MessageList);
            var expandedStrNames = GetExpandedNames(StructsTree, StructList);

            if (expandNode != null)
            {
                string name = (expandNode as dynamic).Name;
                if (expandNode is Message) { if (!expandedMsgNames.Contains(name)) expandedMsgNames.Add(name); }
                else if (expandNode is IcdControl.Models.Struct) { if (!expandedStrNames.Contains(name)) expandedStrNames.Add(name); }
            }

            MessagesTree.Items.Refresh();
            StructsTree.Items.Refresh();

            RestoreExpandedNodes(MessagesTree, MessageList, expandedMsgNames);
            RestoreExpandedNodes(StructsTree, StructList, expandedStrNames);
        }

        private List<string> GetExpandedNames(TreeView tree, IEnumerable items)
        {
            var names = new List<string>();
            foreach (dynamic item in items)
            {
                var container = tree.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                if (container != null && container.IsExpanded) names.Add(item.Name);
            }
            return names;
        }

        private void RestoreExpandedNodes(TreeView tree, IEnumerable items, List<string> namesToExpand)
        {
            tree.UpdateLayout();
            foreach (dynamic item in items)
            {
                if (namesToExpand.Contains(item.Name))
                {
                    var container = tree.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                    if (container != null) container.IsExpanded = true;
                }
            }
        }

        // =========================================================
        // 4. SELECTION & EDITOR LOGIC
        // =========================================================

        private void UpdateDetailsView(object selected)
        {
            RefreshTypes();

            DetailsPresenter.Content = null;
            DetailsPresenter.ContentTemplate = null;

            DataTemplate newTemplate = null;

            if (selected is Message)
                newTemplate = (DataTemplate)FindResource("MessageEditorTemplate");
            else if (selected is IcdControl.Models.Struct)
                newTemplate = (DataTemplate)FindResource("StructEditorTemplate");
            else if (selected is DataField field)
            {
                if (IsNestedStruct(field.Type))
                    newTemplate = (DataTemplate)FindResource("LinkedStructTemplate");
                else
                    newTemplate = (DataTemplate)FindResource("PrimitiveFieldTemplate");
            }

            DetailsPresenter.ContentTemplate = newTemplate;
            DetailsPresenter.Content = selected;
        }

        private bool IsNestedStruct(string type)
        {
            if (string.IsNullOrEmpty(type)) return false;
            return !PrimitiveTypes.Contains(type);
        }

        private void MessagesTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_isChangingSelection) return;
            _isChangingSelection = true;

            if (e.NewValue != null)
            {
                UnselectAllInTree(StructsTree);
                UpdateDetailsView(e.NewValue);
            }

            _isChangingSelection = false;
        }

        private void StructsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_isChangingSelection) return;
            _isChangingSelection = true;

            if (e.NewValue != null)
            {
                UnselectAllInTree(MessagesTree);
                UpdateDetailsView(e.NewValue);
            }

            _isChangingSelection = false;
        }

        // =========================================================
        // 5. CRUD OPERATIONS
        // =========================================================

        private void AddMessage_Click(object sender, RoutedEventArgs e)
        {
            var newMsg = new Message { Name = "NewMsg", Fields = new List<BaseField>() };
            if (_currentIcd.Messages == null) _currentIcd.Messages = new List<Message>();
            _currentIcd.Messages.Add(newMsg);
            MessageList.Add(newMsg);
        }

        private void AddStruct_Click(object sender, RoutedEventArgs e)
        {
            var newStruct = new IcdControl.Models.Struct { Name = "NewStruct", Fields = new List<BaseField>() };
            if (_currentIcd.Structs == null) _currentIcd.Structs = new List<IcdControl.Models.Struct>();
            _currentIcd.Structs.Add(newStruct);
            StructList.Add(newStruct);
            RefreshTypes();
        }

        private void AddPrimitive_Click(object sender, RoutedEventArgs e)
        {
            var container = DetailsPresenter.Content;
            var list = GetListFromContainer(container);

            if (list != null)
            {
                string name = GetUniqueName("NewVar", list);
                list.Add(new DataField { Name = name, Type = "uint32", SizeInBits = 32 });
                RefreshTreesVisual(container);
            }
        }

        private void AddLinkedStruct_Click(object sender, RoutedEventArgs e)
        {
            if (StructTypes.Count == 0)
            {
                MessageBox.Show("No struct definitions available. Create a struct first.");
                return;
            }

            var container = DetailsPresenter.Content;
            var list = GetListFromContainer(container);

            if (list != null)
            {
                string defaultStruct = StructTypes[0];
                string baseName = defaultStruct.ToLower() + "_inst";
                string uniqueName = GetUniqueName(baseName, list);

                list.Add(new DataField { Name = uniqueName, Type = defaultStruct, SizeInBits = 0 });
                RefreshTreesVisual(container);
            }
        }

        private void RemoveItem_Click(object sender, RoutedEventArgs e)
        {
            var selected = DetailsPresenter.Content;

            if (selected is Message msg)
            {
                _currentIcd.Messages.Remove(msg);
                MessageList.Remove(msg);
                DetailsPresenter.Content = null;
            }
            else if (selected is IcdControl.Models.Struct str)
            {
                _currentIcd.Structs.Remove(str);
                StructList.Remove(str);
                DetailsPresenter.Content = null;
                RefreshTypes();
            }
            else if (selected is DataField field)
            {
                var result = FindParentContainer(field);

                if (result.List != null && result.Container != null)
                {
                    result.List.Remove(field);

                    if (result.Container is Message m)
                    {
                        m.Fields = new List<BaseField>(m.Fields.Cast<BaseField>());
                    }
                    else if (result.Container is IcdControl.Models.Struct s)
                    {
                        s.Fields = new List<BaseField>(s.Fields.Cast<BaseField>());
                    }

                    DetailsPresenter.Content = null;
                    RefreshTreesVisual();
                }
            }
        }

        // =========================================================
        // 6. SAVE & EXPORT (CLIENT SIDE GENERATION)
        // =========================================================

        private async void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBox textBox)
            {
                textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }

            RefreshTypes();
            // Sync lists
            _currentIcd.Messages = MessageList.ToList();
            _currentIcd.Structs = StructList.ToList();

            // True = Increment Version
            await SaveInternal(true, true);
        }

        // התיקון המרכזי: יצירת הקובץ בצד הלקוח (ללא שרת)
        private async void ExportHeader_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIcd == null) return;

            // 1. קודם שומרים את המצב הנוכחי (בלי להעלות גרסה)
            bool saveSuccess = await SaveInternal(false, false);
            if (!saveSuccess) return; // אם השמירה נכשלה, לא ממשיכים

            try
            {
                // 2. יצירת המחרוזת של קובץ ה-Header בצד הלקוח
                string content = GenerateCHeader(_currentIcd);
                string fileName = _currentIcd.Name.Replace(" ", "_") + ".h";

                // 3. פתיחת דיאלוג לשמירה
                var dlg = new SaveFileDialog
                {
                    Filter = "C Header|*.h",
                    FileName = fileName
                };

                if (dlg.ShowDialog() == true)
                {
                    File.WriteAllText(dlg.FileName, content, Encoding.UTF8);
                    MessageBox.Show("Header exported successfully!", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export error: {ex.Message}");
            }
        }

        // --- לוגיקת יצירת Header (C/C++ Compatible) ---
        private string GenerateCHeader(Icd icd)
        {
            StringBuilder sb = new StringBuilder();

            // 1. Header Guard
            string cleanName = icd.Name.Replace(" ", "_").ToUpper();
            string guard = $"{cleanName}_H";

            sb.AppendLine($"#ifndef {guard}");
            sb.AppendLine($"#define {guard}");
            sb.AppendLine();

            sb.AppendLine("/*");
            sb.AppendLine($" * Generated ICD Header: {icd.Name}");
            sb.AppendLine($" * Version: {icd.Version:0.0}");
            sb.AppendLine($" * Description: {icd.Description}");
            sb.AppendLine(" */");
            sb.AppendLine();
            sb.AppendLine("#include <stdint.h>");
            sb.AppendLine("#include <stdbool.h>");
            sb.AppendLine();

            // 2. Struct Definitions (קודם, כי ההודעות משתמשות בהם)
            if (icd.Structs != null && icd.Structs.Any())
            {
                sb.AppendLine("/******************************************************************************");
                sb.AppendLine(" * STRUCT DEFINITIONS");
                sb.AppendLine(" ******************************************************************************/");
                foreach (var s in icd.Structs)
                {
                    sb.Append(GenerateStructBlock(s.Name, s.Fields));
                }
            }

            // 3. Messages
            if (icd.Messages != null && icd.Messages.Any())
            {
                sb.AppendLine("/******************************************************************************");
                sb.AppendLine(" * MESSAGES");
                sb.AppendLine(" ******************************************************************************/");
                foreach (var msg in icd.Messages)
                {
                    sb.Append(GenerateStructBlock(msg.Name, msg.Fields));
                }
            }

            sb.AppendLine($"#endif /* {guard} */");
            return sb.ToString();
        }

        private string GenerateStructBlock(string structName, IEnumerable<BaseField> fields)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"/* Struct: {structName} */");

            // שימוש ב-typedef כדי לאפשר גם ל-C וגם ל-C++ לעבוד נקי
            sb.AppendLine("typedef struct {");

            if (fields != null)
            {
                foreach (var baseField in fields)
                {
                    if (baseField is DataField field)
                    {
                        string cType = MapToCType(field.Type);
                        sb.Append($"    {cType} {field.Name}");

                        // אם זה פרימיטיבי ויש גודל בביטים מוגדר, מוסיפים Bitfield
                        if (field.SizeInBits > 0 && IsPrimitive(field.Type))
                        {
                            sb.Append($" : {field.SizeInBits}");
                        }

                        sb.AppendLine(";");
                    }
                }
            }

            // השם בסוף ללא תוספת _t
            sb.AppendLine($"}} {structName};");
            sb.AppendLine();
            return sb.ToString();
        }

        private bool IsPrimitive(string type)
        {
            return PrimitiveTypes.Contains(type);
        }

        private string MapToCType(string type)
        {
            // המרה בין שמות המערכת ל-stdint.h
            return type switch
            {
                "uint8" => "uint8_t",
                "int8" => "int8_t",
                "uint16" => "uint16_t",
                "int16" => "int16_t",
                "uint32" => "uint32_t",
                "int32" => "int32_t",
                "uint64" => "uint64_t",
                "int64" => "int64_t",
                "float" => "float",
                "double" => "double",
                "bool" => "bool",
                "char" => "char",
                "string" => "char*",
                _ => type // שמות של מבנים אחרים נשארים כמו שהם
            };
        }

        private async System.Threading.Tasks.Task<bool> SaveInternal(bool showMsg, bool incrementVersion)
        {
            try
            {
                _currentIcd.Name = NameTxt.Text;
                _currentIcd.Description = DescTxt.Text;

                string currentVerStr = VersionTxt.Text;
                double newVersion = 0;
                double.TryParse(currentVerStr, out newVersion);

                if (incrementVersion && !_versionChangedManually)
                {
                    if (double.TryParse(currentVerStr, out double currentVal))
                    {
                        int decimalPlaces = 0;
                        if (currentVerStr.Contains("."))
                        {
                            decimalPlaces = currentVerStr.Length - currentVerStr.IndexOf(".") - 1;
                        }

                        if (decimalPlaces == 0) decimalPlaces = 1;

                        double increment = 1.0 / Math.Pow(10, decimalPlaces);
                        newVersion = currentVal + increment;
                        newVersion = Math.Round(newVersion, decimalPlaces);
                    }
                }

                _currentIcd.Version = newVersion;
                VersionTxt.Text = newVersion.ToString("0.0################");
                _versionChangedManually = false;

                if (ApiClient.CurrentUser != null) LastUserTxt.Text = ApiClient.CurrentUser.Username;
                if (showMsg) StatusTxt.Text = "Saving...";

                var response = await ApiClient.Client.PostAsJsonAsync("api/icd/save", _currentIcd);
                if (response.IsSuccessStatusCode)
                {
                    if (showMsg) MessageBox.Show("ICD Saved Successfully!");
                    return true;
                }
                else
                {
                    if (showMsg) MessageBox.Show($"Save failed: {response.ReasonPhrase}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                if (showMsg) MessageBox.Show("Error: " + ex.Message);
                return false;
            }
        }

        private void Undo_Click(object sender, RoutedEventArgs e) { }
        private void Redo_Click(object sender, RoutedEventArgs e) { }
        private void CancelBtn_Click(object sender, RoutedEventArgs e) { Close(); }
    }

    // --- UTILS ---
    public static class Utils
    {
        public static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            while ((child != null) && !(child is T))
            {
                child = VisualTreeHelper.GetParent(child);
            }
            return child as T;
        }
    }
}