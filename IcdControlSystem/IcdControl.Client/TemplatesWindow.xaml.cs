using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Text.Json.Serialization;

namespace IcdControl.Client
{
    public class TemplateInfo
    {
        [JsonPropertyName("templateId")]
        public string TemplateId { get; set; }
        
        [JsonPropertyName("name")]
        public string Name { get; set; }
        
        [JsonPropertyName("description")]
        public string Description { get; set; }
        
        [JsonPropertyName("createdBy")]
        public string CreatedBy { get; set; }
    }

    public partial class TemplatesWindow : Window
    {
        public string SelectedTemplateId { get; private set; }

        public TemplatesWindow()
        {
            InitializeComponent();
            Loaded += TemplatesWindow_Loaded;
        }

        private async void TemplatesWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var templates = await ApiClient.Client.GetFromJsonAsync<List<TemplateInfo>>("api/icd/templates");
                if (templates != null)
                {
                    TemplatesGrid.ItemsSource = templates;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load templates: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UseTemplateBtn_Click(object sender, RoutedEventArgs e)
        {
            if (TemplatesGrid.SelectedItem is TemplateInfo selected)
            {
                SelectedTemplateId = selected.TemplateId;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Please select a template.", "Use Template", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

