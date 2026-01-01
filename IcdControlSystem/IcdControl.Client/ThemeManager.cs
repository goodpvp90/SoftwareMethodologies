using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace IcdControl.Client
{
    internal static class ThemeManager
    {
        private static volatile bool _userOverrideApplied;

        public static void ApplyDarkMode(bool userInitiated = false)
        {
            if (userInitiated)
                _userOverrideApplied = true;

            // Higher-contrast, modern dark palette.
            SetThemeBrush("BackgroundColor", Color.FromRgb(11, 18, 32));
            SetThemeBrush("SurfaceColor", Color.FromRgb(15, 23, 42));
            SetThemeBrush("SurfaceAltColor", Color.FromRgb(20, 31, 55));
            SetThemeBrush("ControlBackgroundColor", Color.FromRgb(17, 24, 39));
            SetThemeBrush("HoverBackgroundColor", Color.FromRgb(31, 42, 68));
            SetThemeBrush("DataGridHeaderBackgroundColor", Color.FromRgb(17, 24, 39));
            SetThemeBrush("TextPrimaryColor", Color.FromRgb(229, 231, 235));
            SetThemeBrush("TextSecondaryColor", Color.FromRgb(156, 163, 175));
            SetThemeBrush("BorderColor", Color.FromRgb(51, 65, 85));
        }

        public static void ApplyLightMode(bool userInitiated = false)
        {
            if (userInitiated)
                _userOverrideApplied = true;

            SetThemeBrush("BackgroundColor", Color.FromRgb(243, 244, 246));
            SetThemeBrush("SurfaceColor", Color.FromRgb(255, 255, 255));
            SetThemeBrush("SurfaceAltColor", Color.FromRgb(249, 250, 251));
            SetThemeBrush("ControlBackgroundColor", Color.FromRgb(255, 255, 255));
            SetThemeBrush("HoverBackgroundColor", Color.FromRgb(249, 250, 251));
            SetThemeBrush("DataGridHeaderBackgroundColor", Color.FromRgb(243, 244, 246));
            SetThemeBrush("TextPrimaryColor", Color.FromRgb(31, 41, 55));
            SetThemeBrush("TextSecondaryColor", Color.FromRgb(107, 114, 128));
            SetThemeBrush("BorderColor", Color.FromRgb(229, 231, 235));
        }

        public static Task InitializeFromServerAsync() => InitializeFromServerAsync(ApiClient.Client);

        public static async Task InitializeFromServerAsync(HttpClient httpClient)
        {
            if (httpClient == null)
                return;

            try
            {
                var json = await httpClient.GetStringAsync("api/icd/settings").ConfigureAwait(false);
                var darkMode = TryReadBooleanProperty(json, "darkMode") ?? TryReadBooleanProperty(json, "DarkMode");

                // If the user has toggled the theme (e.g., in Settings) while this request was in-flight,
                // do not override their choice.
                if (_userOverrideApplied)
                    return;

                if (darkMode == true)
                    ApplyDarkMode();
                else
                    ApplyLightMode();
            }
            catch
            {
                // If the server isn't reachable (e.g., not started yet), keep defaults.
            }
        }

        private static bool? TryReadBooleanProperty(string json, string propertyName)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return null;

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (!string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (prop.Value.ValueKind == JsonValueKind.True)
                        return true;
                    if (prop.Value.ValueKind == JsonValueKind.False)
                        return false;

                    if (prop.Value.ValueKind == JsonValueKind.String && bool.TryParse(prop.Value.GetString(), out var parsed))
                        return parsed;

                    return null;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static void SetThemeBrush(string resourceKey, Color color)
        {
            var app = Application.Current;
            if (app == null)
                return;

            void Set()
            {
                // Replace the resource with a new brush instance.
                // This avoids mutating XAML-frozen brushes ("read-only state") and works with DynamicResource.
                app.Resources[resourceKey] = new SolidColorBrush(color);
            }

            if (app.Dispatcher.CheckAccess())
                Set();
            else
                app.Dispatcher.Invoke(Set);
        }
    }
}
