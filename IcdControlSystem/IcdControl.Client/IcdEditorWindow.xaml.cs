using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using IcdControl.Models;

namespace IcdControl.Client
{
    public partial class IcdEditorWindow : Window
    {
        private string _icdId;
        private Icd _icd;
        private bool _isLoading = true;

        public IcdEditorWindow(string icdId)
        {
            InitializeComponent();
            _icdId = icdId;
            Loaded += OnLoaded;

            PropTypeCombo.ItemsSource = new string[] { "uint32_t", "int32_t", "float", "double", "uint8_t", "uint16_t", "bool" };
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

        private void RefreshTree()
        {
            IcdTree.Items.Clear();

            // Messages
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

            // Structs
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
            var item = new TreeViewItem { Header = field.Name, Tag = field };
            if (field is Struct s && s.Fields != null)
            {
                foreach (var child in s.Fields) item.Items.Add(CreateTreeItem(child));
            }
            return item;
        }

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

        private void Ctx_AddField_Click(object sender, RoutedEventArgs e)
        {
            if (IcdTree.SelectedItem is TreeViewItem item && item.Tag is Struct parentStruct)
            {
                var newField = new DataField { Name = "new_field", Type = "uint32_t", SizeInBits = 32 };
                parentStruct.Fields.Add(newField);
                RefreshTree();
                // Expand item to show new field
                item.IsExpanded = true;
            }
            else
            {
                MessageBox.Show("Select a Message or Struct to add a field to.");
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
                item.IsExpanded = true;
            }
        }

        // Properties logic
        private BaseField _selectedField;
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
                    StructLinkPanel.Visibility = Visibility.Collapsed;
                    PropIsUnionChk.Visibility = Visibility.Visible;
                    PropIsUnionChk.IsChecked = s.IsUnion;
                }
                _isLoading = false;
            }
            else
            {
                EditorPanel.Visibility = Visibility.Hidden;
            }
        }

        private void PropNameTxt_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isLoading && _selectedField != null) _selectedField.Name = PropNameTxt.Text;
        }

        private void PropTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoading && _selectedField is DataField df && PropTypeCombo.SelectedItem is string type)
                df.Type = type;
        }

        private void StructLinkCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private void PropIsUnionChk_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isLoading && _selectedField is Struct s) s.IsUnion = PropIsUnionChk.IsChecked == true;
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            _icd.Name = NameTxt.Text;
            if (double.TryParse(VersionTxt.Text, out var v)) _icd.Version = v;

            // This call should now work correctly because the infinite recursion in serialization is fixed in Entities.cs
            var res = await ApiClient.Client.PostAsJsonAsync("api/icd/save", _icd);
            if (res.IsSuccessStatusCode)
            {
                MessageBox.Show("Saved successfully!");
            }
            else
            {
                MessageBox.Show("Save failed: " + res.ReasonPhrase);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}