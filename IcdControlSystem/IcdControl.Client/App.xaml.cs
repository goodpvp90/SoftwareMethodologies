using System;
using System.Windows.Threading;
using System.Windows;

namespace IcdControl.Client
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Ensure any window that opens (including dialogs) fits the current screen work area.
            EventManager.RegisterClassHandler(
                typeof(Window),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler((s, _) =>
                {
                    if (s is Window w)
                        FitWindowToWorkArea(w);
                }));

            // Best-effort: apply saved theme early so all windows render correctly.
            // If the server isn't running yet, this will silently fall back to defaults.
            _ = ThemeManager.InitializeFromServerAsync();

            var login = new LoginWindow();
            login.Show();
        }

        private static void FitWindowToWorkArea(Window window)
        {
            if (window == null)
                return;

            // Skip if the window is maximized.
            if (window.WindowState == WindowState.Maximized)
                return;

            // Run after layout so ActualWidth/ActualHeight are valid.
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (window.WindowState == WindowState.Maximized)
                    return;

                var workArea = SystemParameters.WorkArea;

                window.MaxWidth = workArea.Width;
                window.MaxHeight = workArea.Height;

                if (!double.IsNaN(window.Width) && window.Width > workArea.Width)
                    window.Width = workArea.Width;
                if (!double.IsNaN(window.Height) && window.Height > workArea.Height)
                    window.Height = workArea.Height;

                // Clamp position so the window bottom/right never goes off-screen.
                // If WPF hasn't set Top/Left yet, these will be 0 (fine).
                var actualWidth = Math.Max(1, window.ActualWidth);
                var actualHeight = Math.Max(1, window.ActualHeight);

                var minLeft = workArea.Left;
                var minTop = workArea.Top;
                var maxLeft = workArea.Right - actualWidth;
                var maxTop = workArea.Bottom - actualHeight;

                window.Left = Math.Max(minLeft, Math.Min(window.Left, maxLeft));
                window.Top = Math.Max(minTop, Math.Min(window.Top, maxTop));
            }), DispatcherPriority.Background);
        }
    }
}
