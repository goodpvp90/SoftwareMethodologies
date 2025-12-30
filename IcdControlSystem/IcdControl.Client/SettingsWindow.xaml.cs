using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using IcdControl.Models;

namespace IcdControl.Client
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            Loaded += SettingsWindow_Loaded;
        }

        private async void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = await ApiClient.Client.GetFromJsonAsync<dynamic>("api/icd/settings");
                if (settings != null)
                {
                    DarkModeChk.IsChecked = settings.DarkMode == true;
                    if (settings.Language != null)
                    {
                        foreach (System.Windows.Controls.ComboBoxItem item in LanguageCombo.Items)
                            if (item.Tag?.ToString() == settings.Language.ToString())
                                LanguageCombo.SelectedItem = item;
                    }
                    if (settings.Theme != null)
                    {
                        foreach (System.Windows.Controls.ComboBoxItem item in ThemeCombo.Items)
                            if (item.Tag?.ToString() == settings.Theme.ToString())
                                ThemeCombo.SelectedItem = item;
                    }
                }
            }
            catch { }
        }

        private async void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var language = (LanguageCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "en";
                var theme = (ThemeCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "default";
                
                await ApiClient.Client.PostAsJsonAsync("api/icd/settings", new
                {
                    DarkMode = DarkModeChk.IsChecked == true,
                    Language = language,
                    Theme = theme
                });

                // Apply dark mode immediately
                if (DarkModeChk.IsChecked == true)
                    ApplyDarkMode();
                else
                    ApplyLightMode();

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
            ApplyDarkMode();
        }

        private void DarkModeChk_Unchecked(object sender, RoutedEventArgs e)
        {
            ApplyLightMode();
        }

        private void ApplyDarkMode()
        {
            // This is a simplified dark mode - in production you'd want a proper theme system
            Application.Current.Resources["BackgroundColor"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30));
            Application.Current.Resources["SurfaceColor"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 40, 40));
            Application.Current.Resources["TextPrimaryColor"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 240, 240));
            Application.Current.Resources["TextSecondaryColor"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 180, 180));
            Application.Current.Resources["BorderColor"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 60, 60));
        }

        private void ApplyLightMode()
        {
            Application.Current.Resources["BackgroundColor"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(243, 244, 246));
            Application.Current.Resources["SurfaceColor"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255));
            Application.Current.Resources["TextPrimaryColor"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(31, 41, 55));
            Application.Current.Resources["TextSecondaryColor"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128));
            Application.Current.Resources["BorderColor"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(229, 231, 235));
        }
    }
}

