using System;
using System.Windows;

namespace IcdControl.Client
{
    public partial class StatsWindow : Window
    {
        public StatsWindow(string icdName, StatsInfo stats)
        {
            InitializeComponent();

            IcdNameTxt.Text = string.IsNullOrWhiteSpace(icdName) ? string.Empty : icdName;

            TotalMessagesTxt.Text = stats.TotalMessages.ToString();
            TotalStructsTxt.Text = stats.TotalStructs.ToString();
            TotalFieldsTxt.Text = stats.TotalFields.ToString();
            EstimatedSizeTxt.Text = FormatBytes(stats.EstimatedSizeBytes);
        }

        private static string FormatBytes(int bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            if (bytes < 1024 * 1024)
                return $"{bytes / 1024.0:0.##} KB";
            return $"{bytes / (1024.0 * 1024.0):0.##} MB";
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
