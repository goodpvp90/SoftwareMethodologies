using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using IcdControl.Models;
using Microsoft.Win32;
using System.IO;
using System.Text;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace IcdControl.Client
{
    public class StatsInfo
    {
        [JsonPropertyName("totalMessages")]
        public int TotalMessages { get; set; }

        [JsonPropertyName("totalStructs")]
        public int TotalStructs { get; set; }

        [JsonPropertyName("totalFields")]
        public int TotalFields { get; set; }

        [JsonPropertyName("estimatedSizeBytes")]
        public int EstimatedSizeBytes { get; set; }
    }

    // הגדרה יחידה של AppConfig - כל הקבצים בפרויקט יכירו אותה מכאן
    public class AppConfig
    {
        public bool IsDarkMode { get; set; }
    }

    public partial class MainWindow : Window
    {
        private List<Icd> _icds = new List<Icd>();
        private List<Icd> _filteredIcds = new List<Icd>();
        private const string ConfigFile = "config.json";

        public MainWindow()
        {
            InitializeComponent();
            LoadLocalTheme(); // טעינת ערכת נושא בהפעלה
        }

        private void LoadLocalTheme()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    var json = File.ReadAllText(ConfigFile);
                    var config = JsonSerializer.Deserialize<AppConfig>(json);
                    if (config != null && config.IsDarkMode)
                    {
                        ThemeManager.ApplyDarkMode();
                    }
                    else
                    {
                        ThemeManager.ApplyLightMode();
                    }
                }
            }
            catch { /* התעלמות משגיאות בהפעלה ראשונית */ }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // בדיקת הרשאות אדמין
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
                ApplyFilters();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load ICDs: {ex.Message}");
            }
        }

        private void ApplyFilters()
        {
            _filteredIcds = _icds.ToList();

            // Search filter
            if (!string.IsNullOrWhiteSpace(SearchTxt.Text))
            {
                var query = SearchTxt.Text.ToLowerInvariant();
                _filteredIcds = _filteredIcds.Where(i =>
                    (i.Name?.ToLowerInvariant().Contains(query) ?? false) ||
                    (i.Description?.ToLowerInvariant().Contains(query) ?? false) ||
                    (i.Messages?.Any(m => m.Name?.ToLowerInvariant().Contains(query) ?? false) ?? false) ||
                    (i.Structs?.Any(s => s.Name?.ToLowerInvariant().Contains(query) ?? false) ?? false)
                ).ToList();
            }

            // Version filters
            if (double.TryParse(MinVersionTxt.Text, out double minVersion))
                _filteredIcds = _filteredIcds.Where(i => i.Version >= minVersion).ToList();

            if (double.TryParse(MaxVersionTxt.Text, out double maxVersion))
                _filteredIcds = _filteredIcds.Where(i => i.Version <= maxVersion).ToList();

            IcdGrid.ItemsSource = _filteredIcds;
        }

        private void SearchTxt_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void Filter_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void ClearFilter_Click(object sender, RoutedEventArgs e)
        {
            SearchTxt.Text = "";
            MinVersionTxt.Text = "";
            MaxVersionTxt.Text = "";
            ApplyFilters();
        }

        private void IcdGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            OpenBtn_Click(sender, e);
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

        private async void DeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            if (IcdGrid.SelectedItem is not Icd selected)
            {
                DialogService.ShowInfo("Delete ICD", "Please select an ICD from the list.", this);
                return;
            }

            var confirmed = DialogService.Confirm(
                "Confirm Delete",
                $"Delete ICD '{selected.Name}'? This cannot be undone.",
                primaryText: "Delete",
                secondaryText: "Cancel",
                owner: this);

            if (!confirmed)
                return;

            try
            {
                var res = await ApiClient.Client.DeleteAsync($"api/icd/{selected.IcdId}");
                if (res.IsSuccessStatusCode)
                {
                    await LoadIcds();
                    return;
                }

                if ((int)res.StatusCode == 403)
                {
                    DialogService.ShowWarning("Delete ICD", "You do not have permission to delete this ICD.", this);
                    return;
                }

                if ((int)res.StatusCode == 404)
                {
                    DialogService.ShowWarning("Delete ICD", "ICD was not found (it may have been deleted already). Refreshing list.", this);
                    await LoadIcds();
                    return;
                }

                var body = await res.Content.ReadAsStringAsync();
                DialogService.ShowError("Delete ICD", $"Delete failed: {res.StatusCode}\n{body}", this);
            }
            catch (Exception ex)
            {
                DialogService.ShowError("Delete ICD", $"Delete failed: {ex.Message}", this);
            }
        }

        private async void ExportCsvBtn_Click(object sender, RoutedEventArgs e)
        {
            if (IcdGrid.SelectedItem is Icd selected)
            {
                try
                {
                    var icd = await ApiClient.Client.GetFromJsonAsync<Icd>($"api/icd/{selected.IcdId}");
                    if (icd == null)
                    {
                        MessageBox.Show("Failed to load ICD.");
                        return;
                    }

                    var csv = new StringBuilder();
                    csv.AppendLine("Type,Name,Field,FieldType,SizeInBits");

                    if (icd.Messages != null)
                    {
                        foreach (var msg in icd.Messages)
                        {
                            csv.AppendLine($"Message,{msg.Name},,,");
                            if (msg.Fields != null)
                            {
                                foreach (var field in msg.Fields)
                                {
                                    if (field is DataField df)
                                        csv.AppendLine($",,{df.Name},{df.Type},{df.SizeInBits}");
                                    else if (field is Struct st)
                                        csv.AppendLine($",,{st.Name},Struct,");
                                }
                            }
                        }
                    }

                    if (icd.Structs != null)
                    {
                        foreach (var st in icd.Structs)
                        {
                            csv.AppendLine($"Struct,{st.Name},,,");
                            if (st.Fields != null)
                            {
                                foreach (var field in st.Fields)
                                {
                                    if (field is DataField df)
                                        csv.AppendLine($",,{df.Name},{df.Type},{df.SizeInBits}");
                                    else if (field is Struct nested)
                                        csv.AppendLine($",,{nested.Name},Struct,");
                                }
                            }
                        }
                    }

                    var dlg = new SaveFileDialog { Filter = "CSV Files|*.csv", FileName = MakeSafeFileName(selected.Name) + ".csv" };
                    if (dlg.ShowDialog() == true)
                    {
                        File.WriteAllText(dlg.FileName, csv.ToString(), Encoding.UTF8);
                        MessageBox.Show("CSV export saved successfully.");
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

        private void AdminUsers_Click(object sender, RoutedEventArgs e)
        {
            var win = new AdminUsersWindow();
            win.Owner = this;
            win.ShowDialog();
        }

        private async void TemplateBtn_Click(object sender, RoutedEventArgs e)
        {
            var win = new TemplatesWindow();
            win.Owner = this;
            if (win.ShowDialog() == true && win.SelectedTemplateId != null)
            {
                try
                {
                    var template = await ApiClient.Client.GetFromJsonAsync<Icd>($"api/icd/template/{win.SelectedTemplateId}");
                    if (template != null)
                    {
                        // Create new ICD from template
                        template.IcdId = Guid.NewGuid().ToString();
                        template.Name = template.Name + " (Copy)";
                        var response = await ApiClient.Client.PostAsJsonAsync("api/icd/save", template);
                        if (response.IsSuccessStatusCode)
                        {
                            MessageBox.Show("ICD created from template successfully!", "Template", MessageBoxButton.OK, MessageBoxImage.Information);
                            await LoadIcds();
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to create ICD from template: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
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

        private void SettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            var win = new SettingsWindow();
            win.Owner = this;
            win.ShowDialog();
        }

        private async void StatsBtn_Click(object sender, RoutedEventArgs e)
        {
            if (IcdGrid.SelectedItem is Icd selected)
            {
                try
                {
                    var stats = await ApiClient.Client.GetFromJsonAsync<StatsInfo>($"api/icd/{selected.IcdId}/stats");
                    if (stats != null)
                    {
                        var win = new StatsWindow(selected.Name ?? "", stats);
                        win.Owner = this;
                        win.ShowDialog();
                    }
                }
                catch (Exception ex)
                {
                    DialogService.ShowError("ICD Statistics", $"Failed to load statistics: {ex.Message}", this);
                }
            }
            else
            {
                DialogService.ShowInfo("ICD Statistics", "Please select an ICD first.", this);
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control)
                return;

            switch (e.Key)
            {
                case Key.N:
                    NewBtn_Click(sender, e);
                    e.Handled = true;
                    break;
                case Key.O:
                    OpenBtn_Click(sender, e);
                    e.Handled = true;
                    break;
                case Key.F:
                    SearchTxt.Focus();
                    SearchTxt.SelectAll();
                    e.Handled = true;
                    break;
            }
        }
    }
}