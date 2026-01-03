using System.Windows;

namespace IcdControl.Client
{
    internal static class DialogService
    {
        public static void ShowInfo(string title, string message, Window? owner = null)
        {
            var dlg = Create(owner, title, message, primaryText: "OK", secondaryText: "", showSecondary: false);
            dlg.ShowDialog();
        }

        public static void ShowError(string title, string message, Window? owner = null)
        {
            var dlg = Create(owner, title, message, primaryText: "OK", secondaryText: "", showSecondary: false);
            dlg.ShowDialog();
        }

        public static void ShowWarning(string title, string message, Window? owner = null)
        {
            var dlg = Create(owner, title, message, primaryText: "OK", secondaryText: "", showSecondary: false);
            dlg.ShowDialog();
        }

        public static bool Confirm(string title, string message, string primaryText = "Yes", string secondaryText = "No", Window? owner = null)
        {
            var dlg = Create(owner, title, message, primaryText, secondaryText, showSecondary: true);
            return dlg.ShowDialog() == true;
        }

        private static ModernDialogWindow Create(Window? owner, string title, string message, string primaryText, string secondaryText, bool showSecondary)
        {
            var dlg = new ModernDialogWindow
            {
                Owner = owner ?? Application.Current?.MainWindow,
                DialogTitle = title,
                DialogMessage = message,
                PrimaryText = primaryText,
                SecondaryText = secondaryText,
                ShowSecondary = showSecondary
            };

            return dlg;
        }
    }
}
