using System;
using System.Windows;
using System.Net.Http.Json;
using System.IO;
using System.Text.Json;
using IcdControl.Models;

namespace IcdControl.Client
{
    // הערה: המחלקה AppConfig מוגדרת כבר ב-MainWindow.xaml.cs
    // ואין צורך להגדיר אותה כאן שוב.

    public partial class SettingsWindow : Window
    {
        private bool _isInitializing;
        private const string ConfigFile = "config.json";

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

                // 1. Try to load local config first
                if (File.Exists(ConfigFile))
                {
                    var json = File.ReadAllText(ConfigFile);
                    var config = JsonSerializer.Deserialize<AppConfig>(json);
                    if (config != null)
                    {
                        DarkModeChk.IsChecked = config.IsDarkMode;
                    }
                }

                // 2. Try to sync with Server (Optional)
                try
                {
                    var settings = await ApiClient.Client.GetFromJsonAsync<dynamic>("api/icd/settings");
                }
                catch { }

                // Apply visual state
                if (DarkModeChk.IsChecked == true)
                    ThemeManager.ApplyDarkMode();
                else
                    ThemeManager.ApplyLightMode();
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private async void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool isDarkMode = DarkModeChk.IsChecked == true;

                // 1. Save Locally
                var config = new AppConfig { IsDarkMode = isDarkMode };
                var json = JsonSerializer.Serialize(config);
                File.WriteAllText(ConfigFile, json);

                // 2. Save to Server
                await ApiClient.Client.PostAsJsonAsync("api/icd/settings", new
                {
                    DarkMode = isDarkMode
                });

                // 3. Apply Theme
                if (isDarkMode)
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
            if (_isInitializing) return;
            ThemeManager.ApplyDarkMode(userInitiated: true);
        }

        private void DarkModeChk_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            ThemeManager.ApplyLightMode(userInitiated: true);
        }
    }
}