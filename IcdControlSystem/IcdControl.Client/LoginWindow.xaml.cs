using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Input;
using System.IO;            // הוספתי
using System.Text.Json;     // הוספתי
using IcdControl.Models;

namespace IcdControl.Client
{
    public partial class LoginWindow : Window
    {
        private const string ConfigFile = "config.json";

        public LoginWindow()
        {
            InitializeComponent();
            LoadLocalTheme(); // טעינת ערכת נושא מיד בפתיחת החלון
        }

        // --- Theme Logic ---
        private void LoadLocalTheme()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    var json = File.ReadAllText(ConfigFile);
                    // משתמשים ב-AppConfig שהוגדר ב-MainWindow (הוא באותו Namespace)
                    var config = JsonSerializer.Deserialize<AppConfig>(json);

                    if (config != null && config.IsDarkMode)
                    {
                        ThemeManager.ApplyDarkMode();
                    }
                    else
                    {
                        ThemeManager.ApplyLightMode();
                    }
                }
            }
            catch
            {
                // אם הקובץ לא קיים או שיש שגיאה, נשארים עם ברירת המחדל
            }
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

        // --- Helpers to Manage Errors ---

        private void ClearErrors()
        {
            LoginErrorTxt.Text = "";
            LoginErrorTxt.Visibility = Visibility.Collapsed;

            RegErrorTxt.Text = "";
            RegErrorTxt.Visibility = Visibility.Collapsed;
        }

        private void ShowLoginError(string message)
        {
            LoginErrorTxt.Text = message;
            LoginErrorTxt.Visibility = Visibility.Visible;
        }

        private void ShowRegError(string message)
        {
            RegErrorTxt.Text = message;
            RegErrorTxt.Visibility = Visibility.Visible;
        }

        // --- Switching Views ---

        private void SwitchToRegister_Click(object sender, RoutedEventArgs e)
        {
            ClearErrors(); // Clear previous errors
            LoginPanel.Visibility = Visibility.Collapsed;
            RegisterPanel.Visibility = Visibility.Visible;

            RegUsernameTxt.Text = "";
            RegEmailTxt.Text = "";
            RegPassBox.Password = "";
            RegConfirmPassBox.Password = "";
        }

        private void SwitchToLogin_Click(object sender, RoutedEventArgs e)
        {
            ClearErrors(); // Clear previous errors
            RegisterPanel.Visibility = Visibility.Collapsed;
            LoginPanel.Visibility = Visibility.Visible;
        }

        // --- Login Logic ---

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            ClearErrors(); // Reset error state

            var username = LoginUsernameTxt.Text?.Trim();
            var pass = LoginPassBox.Password?.Trim();

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(pass))
            {
                ShowLoginError("Please enter both username and password.");
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
                    // Read custom error from server if possible, else generic
                    string errorMsg = "Invalid username or password.";
                    if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        errorMsg = "Incorrect credentials.";
                    else
                        errorMsg = $"Login failed: {res.ReasonPhrase}";

                    ShowLoginError(errorMsg);
                }
            }
            catch (Exception ex)
            {
                ShowLoginError("Could not connect to server.\nIs it running?");
            }
        }

        private void LoginPassBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Login_Click(sender, e);
        }

        // --- Register Logic ---

        private async void Register_Click(object sender, RoutedEventArgs e)
        {
            ClearErrors(); // Reset error state

            var username = RegUsernameTxt.Text?.Trim();
            var email = RegEmailTxt.Text?.Trim();
            var pass = RegPassBox.Password;
            var confirm = RegConfirmPassBox.Password;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(pass))
            {
                ShowRegError("All fields are required.");
                return;
            }

            if (pass != confirm)
            {
                ShowRegError("Passwords do not match.");
                return;
            }

            try
            {
                var req = new RegisterRequest { Username = username, Email = email, Password = pass };
                var res = await ApiClient.Client.PostAsJsonAsync("api/icd/register", req);

                if (res.IsSuccessStatusCode)
                {
                    MessageBox.Show("Registration successful! Please log in."); // Keep popup only for success
                    SwitchToLogin_Click(sender, e);
                    LoginUsernameTxt.Text = username;
                }
                else if (res.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    ShowRegError("Username or Email already exists.");
                }
                else
                {
                    ShowRegError($"Registration failed: {res.ReasonPhrase}");
                }
            }
            catch (Exception ex)
            {
                ShowRegError("Could not connect to server.");
            }
        }
    }
}