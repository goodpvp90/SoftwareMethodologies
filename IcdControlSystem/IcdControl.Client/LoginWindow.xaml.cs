using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Input;
using IcdControl.Models;

namespace IcdControl.Client
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
        }

        // --- Window Logic ---

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            this.DragMove();
        }

        private void CloseApp_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        // --- Switching Views ---

        private void SwitchToRegister_Click(object sender, RoutedEventArgs e)
        {
            LoginPanel.Visibility = Visibility.Collapsed;
            RegisterPanel.Visibility = Visibility.Visible;
            // Clear fields optionally
            RegUsernameTxt.Text = "";
            RegEmailTxt.Text = "";
            RegPassBox.Password = "";
            RegConfirmPassBox.Password = "";
        }

        private void SwitchToLogin_Click(object sender, RoutedEventArgs e)
        {
            RegisterPanel.Visibility = Visibility.Collapsed;
            LoginPanel.Visibility = Visibility.Visible;
        }

        // --- Login Logic ---

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            var username = LoginUsernameTxt.Text;
            var pass = LoginPassBox.Password;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(pass))
            {
                MessageBox.Show("Please enter both username and password.");
                return;
            }

            try
            {
                var res = await ApiClient.Client.PostAsJsonAsync("api/icd/login", new LoginRequest { Username = username, Password = pass });

                if (res.IsSuccessStatusCode)
                {
                    var user = await res.Content.ReadFromJsonAsync<User>();
                    ApiClient.SetCurrentUser(user);

                    var main = new MainWindow();
                    Application.Current.MainWindow = main;
                    main.Show();
                    this.Close();
                }
                else
                {
                    MessageBox.Show($"Login Failed: {res.ReasonPhrase}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Connection Error: {ex.Message}");
            }
        }

        private void LoginPassBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Login_Click(sender, e);
        }

        // --- Register Logic (Integrated) ---

        private async void Register_Click(object sender, RoutedEventArgs e)
        {
            var username = RegUsernameTxt.Text?.Trim();
            var email = RegEmailTxt.Text?.Trim();
            var pass = RegPassBox.Password;
            var confirm = RegConfirmPassBox.Password;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(pass))
            {
                MessageBox.Show("Please provide email and password.");
                return;
            }

            if (pass != confirm)
            {
                MessageBox.Show("Passwords do not match.");
                return;
            }

            try
            {
                var req = new RegisterRequest { Username = username, Email = email, Password = pass };
                var res = await ApiClient.Client.PostAsJsonAsync("api/icd/register", req);

                if (res.IsSuccessStatusCode)
                {
                    MessageBox.Show("Registration successful. You can now log in.");
                    // Auto switch back to login
                    SwitchToLogin_Click(sender, e);
                    // Optional: pre-fill login username
                    LoginUsernameTxt.Text = username;
                }
                else if (res.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    MessageBox.Show("Email or Username already registered.");
                }
                else
                {
                    MessageBox.Show($"Server returned {(int)res.StatusCode} {res.ReasonPhrase}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Request failed: {ex.Message}");
            }
        }
    }
}