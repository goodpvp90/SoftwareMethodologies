using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using IcdControl.Models;
using Microsoft.Win32;
using System.IO;
using System.Text;

namespace IcdControl.Client
{
    public partial class MainWindow : Window
    {
        private List<Icd> _icds = new List<Icd>();

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Admin Check
            if (ApiClient.CurrentUser != null && ApiClient.CurrentUser.IsAdmin)
            {
                AdminPanel.Visibility = Visibility.Visible;
            }

            await LoadIcds();
        }

        private async System.Threading.Tasks.Task LoadIcds()
        {
            try
            {
                _icds = await ApiClient.Client.GetFromJsonAsync<List<Icd>>("api/icd/list") ?? new List<Icd>();
                IcdGrid.ItemsSource = _icds;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load ICDs: {ex.Message}");
            }
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e) => await LoadIcds();

        private void NewBtn_Click(object sender, RoutedEventArgs e)
        {
            var win = new NewIcdWindow();
            win.Owner = this;
            if (win.ShowDialog() == true)
            {
                _ = LoadIcds();
            }
        }

        private void OpenBtn_Click(object sender, RoutedEventArgs e)
        {
            if (IcdGrid.SelectedItem is Icd selected)
            {
                var win = new IcdEditorWindow(selected.IcdId);
                win.Owner = this;
                win.ShowDialog();
            }
            else
            {
                MessageBox.Show("Please select an ICD from the list.");
            }
        }

        private async void ExportBtn_Click(object sender, RoutedEventArgs e)
        {
            if (IcdGrid.SelectedItem is Icd selected)
            {
                try
                {
                    var res = await ApiClient.Client.GetAsync($"api/icd/{selected.IcdId}/export");
                    if (!res.IsSuccessStatusCode)
                    {
                        MessageBox.Show($"Export failed: {res.ReasonPhrase}");
                        return;
                    }
                    var content = await res.Content.ReadAsStringAsync();
                    var dlg = new SaveFileDialog { Filter = "C Header|*.h", FileName = MakeSafeFileName(selected.Name) + ".h" };
                    if (dlg.ShowDialog() == true)
                    {
                        File.WriteAllText(dlg.FileName, content, Encoding.UTF8);
                        MessageBox.Show("Export saved successfully.");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export failed: {ex.Message}");
                }
            }
            else
            {
                MessageBox.Show("Please select an ICD first.");
            }
        }

        private void AdminPermissions_Click(object sender, RoutedEventArgs e)
        {
            var win = new AdminPermissionsWindow();
            win.Owner = this;
            win.ShowDialog();
        }

        private string MakeSafeFileName(string name)
        {
            var sb = new StringBuilder();
            foreach (var ch in name ?? string.Empty)
            {
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-') sb.Append(ch);
                else sb.Append('_');
            }
            return sb.ToString();
        }
    }
}