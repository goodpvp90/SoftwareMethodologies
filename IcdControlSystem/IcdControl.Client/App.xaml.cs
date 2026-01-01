using System.Configuration;
using System.Data;
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

            // Best-effort: apply saved theme early so all windows render correctly.
            // If the server isn't running yet, this will silently fall back to defaults.
            _ = ThemeManager.InitializeFromServerAsync();

            var login = new LoginWindow();
            login.Show();
        }
    }
}
