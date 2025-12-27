using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using IcdControl.Models;

namespace IcdControl.Client
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
        }

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            var username = EmailTxt.Text; // control named EmailTxt used for username now
            var pass = PassBox.Password;

            try
            {
                var res = await ApiClient.Client.PostAsJsonAsync("api/icd/login", new LoginRequest { Username = username, Password = pass });
                if (res.IsSuccessStatusCode)
                {
                    var user = await res.Content.ReadFromJsonAsync<User>();
                    ApiClient.SetCurrentUser(user);

                    MessageBox.Show("מחובר לשרת!");
                    var main = new MainWindow();
                    Application.Current.MainWindow = main; // ensure app stays alive
                    main.Show();
                    this.Close();
                }
                else
                {
                    MessageBox.Show($"Server returned {(int)res.StatusCode} {res.ReasonPhrase}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Request to {ApiClient.Client.BaseAddress} failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void OpenRegister_Click(object sender, RoutedEventArgs e)
        {
            var w = new RegisterWindow();
            w.Owner = this;
            w.ShowDialog();
        }
    }
}