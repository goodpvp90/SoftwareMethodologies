using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;

namespace IcdControl.Client
{
    public partial class HistoryWindow : Window
    {
        private string _icdId;

        public HistoryWindow(string icdId)
        {
            InitializeComponent();
            _icdId = icdId;
            Loaded += HistoryWindow_Loaded;
        }

        private async void HistoryWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var history = await ApiClient.Client.GetFromJsonAsync<List<dynamic>>($"api/icd/{_icdId}/history");
                if (history != null)
                {
                    HistoryGrid.ItemsSource = history;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load history: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

