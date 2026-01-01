using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.IO;
using System.Text;
using IcdControl.Models;

namespace IcdControl.Client
{
    public partial class IcdEditorWindow : Window
    {
        private string _icdId;
        private Icd _currentIcd;

        public ObservableCollection<Message> MessageList { get; set; } = new ObservableCollection<Message>();
        public ObservableCollection<IcdControl.Models.Struct> StructList { get; set; } = new ObservableCollection<IcdControl.Models.Struct>();

        public List<string> AvailableTypes { get; set; } = new List<string>
        {
            "uint8", "int8", "uint16", "int16", "uint32", "int32",
            "uint64", "int64", "float", "double", "bool", "char", "string"
        };

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
                    VersionTxt.Text = _currentIcd.Version.ToString();
                    DescTxt.Text = _currentIcd.Description;

                    // שחזור הפיצ'ר שנעלם: Last Modified
                    // (במערכת אמיתית זה יגיע מהשרת, כאן אני מדמה את התצוגה)
                    LastUserTxt.Text = "Current User"; // אפשר להחליף ב _currentIcd.LastModifiedBy
                    ContributorsCountTxt.Text = "3";   // אפשר להחליף ב _currentIcd.Contributors.Count

                    RebuildTrees();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading ICD: {ex.Message}");
            }
        }

        private void RebuildTrees()
        {
            MessageList.Clear();
            StructList.Clear();

            if (_currentIcd.Messages != null)
                foreach (var msg in _currentIcd.Messages) MessageList.Add(msg);

            if (_currentIcd.Structs != null)
                foreach (var s in _currentIcd.Structs) StructList.Add(s);
        }

        // --- Selection Logic ---

        private void UpdateDetailsView(object selected)
        {
            DetailsPresenter.Content = selected;

            if (selected is Message)
                DetailsPresenter.ContentTemplate = (DataTemplate)FindResource("MessageEditorTemplate");
            else if (selected is IcdControl.Models.Struct)
                DetailsPresenter.ContentTemplate = (DataTemplate)FindResource("StructEditorTemplate");
            else if (selected is DataField)
                DetailsPresenter.ContentTemplate = (DataTemplate)FindResource("FieldEditorTemplate");
            else
                DetailsPresenter.Content = null;
        }

        private void MessagesTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_isChangingSelection) return;
            _isChangingSelection = true;

            // Clear selection in the other tree visually (simple logic)
            if (StructsTree.SelectedItem != null)
            {
                // In pure MVVM we would manage IsSelected property, here we just focus on content
            }

            UpdateDetailsView(e.NewValue);
            _isChangingSelection = false;
        }

        private void StructsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_isChangingSelection) return;
            _isChangingSelection = true;
            UpdateDetailsView(e.NewValue);
            _isChangingSelection = false;
        }

        // --- Add Logic ---

        private void AddMessage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIcd == null) return;
            if (_currentIcd.Messages == null) _currentIcd.Messages = new List<Message>();

            var newMsg = new Message { Name = "NewMsg", Fields = new() };
            _currentIcd.Messages.Add(newMsg);
            MessageList.Add(newMsg);
        }

        private void AddStruct_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIcd == null) return;
            if (_currentIcd.Structs == null) _currentIcd.Structs = new();

            var newStruct = new IcdControl.Models.Struct { Name = "NewStruct", Fields = new() };
            _currentIcd.Structs.Add(newStruct);
            StructList.Add(newStruct);
        }

        private void AddField_Click(object sender, RoutedEventArgs e)
        {
            var currentContext = DetailsPresenter.Content;

            if (currentContext is Message msg)
            {
                if (msg.Fields == null) msg.Fields = new();
                msg.Fields.Add(new DataField { Name = "NewField", Type = "uint32", SizeInBits = 32 });
                MessagesTree.Items.Refresh();
            }
            else if (currentContext is IcdControl.Models.Struct str)
            {
                if (str.Fields == null) str.Fields = new();
                str.Fields.Add(new DataField { Name = "NewField", Type = "uint32", SizeInBits = 32 });
                StructsTree.Items.Refresh();
            }
        }

        // --- Remove Logic ---

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
            }
            else if (selected is DataField field)
            {
                foreach (var m in _currentIcd.Messages)
                {
                    if (m.Fields != null && m.Fields.Contains(field))
                    {
                        m.Fields.Remove(field);
                        MessagesTree.Items.Refresh();
                        DetailsPresenter.Content = null;
                        return;
                    }
                }
                foreach (var s in _currentIcd.Structs)
                {
                    if (s.Fields != null && s.Fields.Contains(field))
                    {
                        s.Fields.Remove(field);
                        StructsTree.Items.Refresh();
                        DetailsPresenter.Content = null;
                        return;
                    }
                }
            }
        }

        // --- Toolbar Logic ---

        private async void ExportHeader_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIcd == null) return;
            await SaveInternal(showMessages: false);

            try
            {
                var res = await ApiClient.Client.GetAsync($"api/icd/{_icdId}/export");
                if (res.IsSuccessStatusCode)
                {
                    var content = await res.Content.ReadAsStringAsync();
                    var dlg = new SaveFileDialog { Filter = "C Header|*.h", FileName = _currentIcd.Name + ".h" };
                    if (dlg.ShowDialog() == true)
                    {
                        File.WriteAllText(dlg.FileName, content, Encoding.UTF8);
                        MessageBox.Show("Export generated successfully.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else
                {
                    MessageBox.Show($"Export failed: {res.ReasonPhrase}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export error: {ex.Message}");
            }
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Undo functionality coming soon.");
        }

        private void Redo_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Redo functionality coming soon.");
        }

        // --- Save Logic ---

        private async void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            if (await SaveInternal(showMessages: true))
            {
                DialogResult = true;
                Close();
            }
        }

        private async System.Threading.Tasks.Task<bool> SaveInternal(bool showMessages)
        {
            try
            {
                _currentIcd.Name = NameTxt.Text;
                _currentIcd.Description = DescTxt.Text;

                if (double.TryParse(VersionTxt.Text, out double ver))
                    _currentIcd.Version = ver;

                if (showMessages) StatusTxt.Text = "Saving...";

                // עדכון מי שערך אחרון (סימולציה)
                if (ApiClient.CurrentUser != null)
                    LastUserTxt.Text = ApiClient.CurrentUser.Username;

                var response = await ApiClient.Client.PostAsJsonAsync("api/icd/save", _currentIcd);

                if (response.IsSuccessStatusCode)
                {
                    if (showMessages) MessageBox.Show("ICD Saved Successfully!");
                    return true;
                }
                else
                {
                    if (showMessages) MessageBox.Show($"Save failed: {response.ReasonPhrase}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                if (showMessages) MessageBox.Show($"Error saving: {ex.Message}");
                return false;
            }
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}