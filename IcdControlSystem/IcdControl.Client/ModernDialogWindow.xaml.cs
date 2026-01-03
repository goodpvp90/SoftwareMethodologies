using System.Windows;

namespace IcdControl.Client
{
    public partial class ModernDialogWindow : Window
    {
        public ModernDialogWindow()
        {
            InitializeComponent();
        }

        public string DialogTitle
        {
            get => TitleTxt.Text;
            set => TitleTxt.Text = value ?? string.Empty;
        }

        public string DialogMessage
        {
            get => MessageTxt.Text;
            set => MessageTxt.Text = value ?? string.Empty;
        }

        public string PrimaryText
        {
            get => PrimaryBtn.Content?.ToString() ?? string.Empty;
            set => PrimaryBtn.Content = value;
        }

        public string SecondaryText
        {
            get => SecondaryBtn.Content?.ToString() ?? string.Empty;
            set => SecondaryBtn.Content = value;
        }

        public bool ShowSecondary
        {
            get => SecondaryBtn.Visibility == Visibility.Visible;
            set => SecondaryBtn.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Primary_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Secondary_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
