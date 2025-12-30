using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Linq;
using System.Text.Json.Serialization;

namespace IcdControl.Client
{
    public class VersionInfo
    {
        [JsonPropertyName("versionId")]
        public string VersionId { get; set; }
        
        [JsonPropertyName("versionNumber")]
        public double VersionNumber { get; set; }
        
        [JsonPropertyName("createdAt")]
        public string CreatedAt { get; set; }
        
        [JsonPropertyName("createdBy")]
        public string CreatedBy { get; set; }
    }

    public partial class VersionsWindow : Window
    {
        private string _icdId;
        private List<VersionInfo> _versions = new List<VersionInfo>();

        public VersionsWindow(string icdId)
        {
            InitializeComponent();
            _icdId = icdId;
            Loaded += VersionsWindow_Loaded;
        }

        private async void VersionsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var versions = await ApiClient.Client.GetFromJsonAsync<List<VersionInfo>>($"api/icd/{_icdId}/versions");
                if (versions != null)
                {
                    _versions = versions;
                    VersionsGrid.ItemsSource = versions;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load versions: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ViewVersionBtn_Click(object sender, RoutedEventArgs e)
        {
            if (VersionsGrid.SelectedItem is VersionInfo selected)
            {
                try
                {
                    var version = await ApiClient.Client.GetFromJsonAsync<object>($"api/icd/version/{selected.VersionId}");
                    if (version != null)
                    {
                        MessageBox.Show($"Version {selected.VersionNumber} details loaded.", "View Version", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to load version: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("Please select a version.", "View Version", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void RollbackBtn_Click(object sender, RoutedEventArgs e)
        {
            if (VersionsGrid.SelectedItem is VersionInfo selected)
            {
                var result = MessageBox.Show($"Are you sure you want to rollback to version {selected.VersionNumber}?", 
                    "Rollback", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        var response = await ApiClient.Client.PostAsJsonAsync($"api/icd/{_icdId}/rollback", new { VersionId = selected.VersionId });
                        if (response.IsSuccessStatusCode)
                        {
                            MessageBox.Show("Rollback successful!", "Rollback", MessageBoxButton.OK, MessageBoxImage.Information);
                            DialogResult = true;
                            Close();
                        }
                        else
                        {
                            MessageBox.Show($"Rollback failed: {response.ReasonPhrase}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to rollback: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            else
            {
                MessageBox.Show("Please select a version to rollback to.", "Rollback", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

