using ICDControl;
using ICDControl.Logic;
using ICDControl.Models;
using Microsoft.Win32; // For SaveFileDialog
using System;
using System.Windows;
using System.Windows.Controls;

namespace ICDControl
{
    public partial class MainWindow : Window
    {
        private User _currentUser;
        private IcdController _controller = new IcdController();

        // בנאי שמקבל את המשתמש המחובר
        public MainWindow(User user)
        {
            InitializeComponent();
            _currentUser = user;
            UserInfoTxt.Text = $"Hello, {_currentUser.Username} ({(_currentUser.IsAdmin ? "Admin" : "Viewer")})";

            RefreshList();
        }

        // בנאי ריק נדרש לפעמים ע"י המעצב, אבל לא נשתמש בו בריצה
        public MainWindow() { InitializeComponent(); }

        private void RefreshList()
        {
            ProjectsList.ItemsSource = _controller.GetProjects();
        }

        private void NewProject_Click(object sender, RoutedEventArgs e)
        {
            // יצירה מהירה של פרויקט (במערכת מלאה זה יפתח חלון דיאלוג)
            string projName = "Project_" + DateTime.Now.ToString("HHmm");
            _controller.CreateProject(projName, "Auto generated project for demo");
            RefreshList();
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var icd = button.DataContext as Icd;

            SaveFileDialog saveDialog = new SaveFileDialog
            {
                Filter = "C Header File (*.h)|*.h",
                FileName = icd.Name + ".h"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    _controller.ExportToHeaderFile(icd, saveDialog.FileName);
                    MessageBox.Show("File exported successfully!", "Success");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error exporting: " + ex.Message, "Error");
                }
            }
        }

        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            new LoginWindow().Show();
            this.Close();
        }
    }
}