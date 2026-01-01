using System;
using System.Windows;
using System.Net.Http.Json;
using IcdControl.Models;

namespace IcdControl.Client
{
    public partial class SettingsWindow : Window
    {
        private bool _isInitializing;

        public SettingsWindow()
        {
            InitializeComponent();
            Loaded += SettingsWindow_Loaded;
        }

        private async void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _isInitializing = true;
                var settings = await ApiClient.Client.GetFromJsonAsync<dynamic>("api/icd/settings");
                if (settings != null)
                {
                    DarkModeChk.IsChecked = settings.DarkMode == true;

                    if (DarkModeChk.IsChecked == true)
                        ThemeManager.ApplyDarkMode();
                    else
                        ThemeManager.ApplyLightMode();
                }
            }
            catch { }
            finally
            {
                _isInitializing = false;
            }
        }

        private async void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ApiClient.Client.PostAsJsonAsync("api/icd/settings", new
                {
                    DarkMode = DarkModeChk.IsChecked == true
                });

                // Apply dark mode immediately
                if (DarkModeChk.IsChecked == true)
                    ThemeManager.ApplyDarkMode();
                else
                    ThemeManager.ApplyLightMode();

                MessageBox.Show("Settings saved successfully.", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void DarkModeChk_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
                return;

            ThemeManager.ApplyDarkMode(userInitiated: true);
        }

        private void DarkModeChk_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
                return;

            ThemeManager.ApplyLightMode(userInitiated: true);
        }
    }
}

