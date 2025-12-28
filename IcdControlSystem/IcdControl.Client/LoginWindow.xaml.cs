using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Input; // Required for KeyEventArgs
using IcdControl.Models;

namespace IcdControl.Client
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
        }

        // אפשרות לגרור את החלון כי ביטלנו את ה-TitleBar הסטנדרטי
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            this.DragMove();
        }

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            var username = UsernameTxt.Text; // Renamed from EmailTxt for clarity
            var pass = PassBox.Password;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(pass))
            {
                MessageBox.Show("Please enter both username and password.");
                return;
            }

            try
            {
                // UI feedback - optional: Disable button here
                var res = await ApiClient.Client.PostAsJsonAsync("api/icd/login", new LoginRequest { Username = username, Password = pass });

                if (res.IsSuccessStatusCode)
                {
                    var user = await res.Content.ReadFromJsonAsync<User>();
                    ApiClient.SetCurrentUser(user);

                    // Open Main Window
                    var main = new MainWindow();
                    Application.Current.MainWindow = main;
                    main.Show();

                    this.Close();
                }
                else
                {
                    MessageBox.Show($"Login Failed: {res.ReasonPhrase}\nCheck your credentials.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Connection Error: {ex.Message}");
            }
        }

        private void OpenRegister_Click(object sender, RoutedEventArgs e)
        {
            var w = new RegisterWindow();
            w.Owner = this;
            // Hide login while registering? Or just show dialog.
            // Using ShowDialog creates a modal window.
            bool? result = w.ShowDialog();

            // Optional: If registration successful, maybe auto-fill username?
        }

        // Allow pressing "Enter" in the password box to login
        private void PassBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Login_Click(sender, e);
            }
        }

        private void CloseApp_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}