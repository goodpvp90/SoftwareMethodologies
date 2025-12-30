using System.Windows;

namespace IcdControl.Client
{
    public partial class PreviewWindow : Window
    {
        public PreviewWindow(string cHeader)
        {
            InitializeComponent();
            PreviewTxt.Text = cHeader;
        }

        private void CopyBtn_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(PreviewTxt.Text);
            MessageBox.Show("C Header copied to clipboard.", "Copy", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

