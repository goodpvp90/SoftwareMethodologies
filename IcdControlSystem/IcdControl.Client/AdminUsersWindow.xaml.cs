using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using IcdControl.Models;

namespace IcdControl.Client
{
    public partial class AdminUsersWindow : Window
    {
        private List<User> _allUsers = new();
        private User? _selectedUser;

        public AdminUsersWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                ApiClient.EnsureAuthHeader();
                _allUsers = await LoadUsersFromServer();

                // Only manage non-admin accounts from UI.
                _allUsers = _allUsers.Where(u => !u.IsAdmin).OrderBy(u => u.Username).ToList();

                UsersGrid.ItemsSource = _allUsers;
                UsersHintTxt.Text = _allUsers.Count == 0 ? "No users found." : "Select a user to edit.";
                SetSelectedUser(null);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load users: " + ex.Message);
            }
        }

        private string GetAuthDebugString()
        {
            var cu = ApiClient.CurrentUser;
            var header = ApiClient.Client.DefaultRequestHeaders.TryGetValues("X-UserId", out var vals)
                ? string.Join(",", vals)
                : "<missing>";

            return $"CurrentUser: {(cu == null ? "<null>" : cu.Username + " / " + cu.UserId + " (IsAdmin=" + cu.IsAdmin + ")")}" +
                   $"\nHeader X-UserId: {header}" +
                   $"\nBaseAddress: {ApiClient.Client.BaseAddress}";
        }

        private async System.Threading.Tasks.Task<List<User>> LoadUsersFromServer()
        {
            ApiClient.EnsureAuthHeader();

            using var req = new HttpRequestMessage(HttpMethod.Get, "api/icd/admin/users");
            var res = await ApiClient.Client.SendAsync(req);

            if (res.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                MessageBox.Show("Admin request was forbidden (403).\n\n" + GetAuthDebugString());
                throw new InvalidOperationException("403 Forbidden");
            }

            res.EnsureSuccessStatusCode();

            var users = await res.Content.ReadFromJsonAsync<List<User>>();
            return users ?? new List<User>();
        }

        private void SearchTxt_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (_allUsers == null) return;

            var term = (SearchTxt.Text ?? string.Empty).Trim();
            IEnumerable<User> src = _allUsers;

            if (!string.IsNullOrWhiteSpace(term))
            {
                src = src.Where(u =>
                    (!string.IsNullOrWhiteSpace(u.Username) && u.Username.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(u.Email) && u.Email.Contains(term, StringComparison.OrdinalIgnoreCase)));
            }

            var filtered = src.OrderBy(u => u.Username).ToList();
            UsersGrid.ItemsSource = filtered;
            UsersHintTxt.Text = filtered.Count == 0 ? "No users match your search." : "Select a user to edit.";
        }

        private void UsersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SetSelectedUser(UsersGrid.SelectedItem as User);
        }

        private void SetSelectedUser(User? user)
        {
            _selectedUser = user;
            StatusTxt.Text = "";

            UsernameTxt.Text = user?.Username ?? string.Empty;
            UserIdTxt.Text = user?.UserId ?? string.Empty;
            EmailTxt.Text = user?.Email ?? string.Empty;

            NewPasswordBox.Password = string.Empty;
            ConfirmPasswordBox.Password = string.Empty;

            var enabled = user != null;
            EmailTxt.IsEnabled = enabled;
            NewPasswordBox.IsEnabled = enabled;
            ConfirmPasswordBox.IsEnabled = enabled;
        }

        private async void SaveEmail_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedUser == null) return;

            var email = (EmailTxt.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(email))
            {
                StatusTxt.Text = "Email cannot be empty.";
                return;
            }

            try
            {
                ApiClient.EnsureAuthHeader();
                var res = await ApiClient.Client.PostAsJsonAsync($"api/icd/admin/user/{_selectedUser.UserId}/email", new { Email = email });
                if (res.IsSuccessStatusCode)
                {
                    StatusTxt.Text = "Email updated.";
                    await ReloadUsersAndReselect(_selectedUser.UserId);
                }
                else if (res.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    MessageBox.Show("Admin permission required (or session expired). Please log in again as an admin.");
                    Close();
                }
                else if (res.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    StatusTxt.Text = "That email is already in use.";
                }
                else
                {
                    StatusTxt.Text = $"Failed to update email ({(int)res.StatusCode} {res.ReasonPhrase}).";
                }
            }
            catch (Exception ex)
            {
                StatusTxt.Text = "Failed to update email: " + ex.Message;
            }
        }

        private async void UpdatePassword_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedUser == null) return;

            var pw = NewPasswordBox.Password;
            var confirm = ConfirmPasswordBox.Password;

            if (string.IsNullOrWhiteSpace(pw))
            {
                StatusTxt.Text = "Password cannot be empty.";
                return;
            }

            if (pw != confirm)
            {
                StatusTxt.Text = "Passwords do not match.";
                return;
            }

            try
            {
                ApiClient.EnsureAuthHeader();
                var res = await ApiClient.Client.PostAsJsonAsync($"api/icd/admin/user/{_selectedUser.UserId}/password", new { Password = pw });
                if (res.IsSuccessStatusCode)
                {
                    StatusTxt.Text = "Password updated.";
                    NewPasswordBox.Password = string.Empty;
                    ConfirmPasswordBox.Password = string.Empty;
                }
                else if (res.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    MessageBox.Show("Admin permission required (or session expired). Please log in again as an admin.");
                    Close();
                }
                else
                {
                    StatusTxt.Text = $"Failed to update password ({(int)res.StatusCode} {res.ReasonPhrase}).";
                }
            }
            catch (Exception ex)
            {
                StatusTxt.Text = "Failed to update password: " + ex.Message;
            }
        }

        private async System.Threading.Tasks.Task ReloadUsersAndReselect(string userId)
        {
            ApiClient.EnsureAuthHeader();

            try
            {
                _allUsers = await LoadUsersFromServer();
            }
            catch (Exception ex)
            {
                // Most common cause here is 403 (missing/invalid admin header). Surface that clearly.
                MessageBox.Show("Failed to load users: " + ex.Message + "\n\nIf you recently restarted the server, log in again as admin.");
                Close();
                return;
            }
            _allUsers = _allUsers.Where(u => !u.IsAdmin).OrderBy(u => u.Username).ToList();

            // Keep current filter applied.
            ApplyFilter();

            var currentView = UsersGrid.ItemsSource as IEnumerable<User>;
            var match = currentView?.FirstOrDefault(u => u.UserId == userId) ?? _allUsers.FirstOrDefault(u => u.UserId == userId);
            if (match != null)
            {
                UsersGrid.SelectedItem = match;
                UsersGrid.ScrollIntoView(match);
                SetSelectedUser(match);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
