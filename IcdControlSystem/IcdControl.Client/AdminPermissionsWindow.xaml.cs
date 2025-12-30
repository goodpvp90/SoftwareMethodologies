using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using IcdControl.Models;
using System.Net.Http;

namespace IcdControl.Client
{
    public partial class AdminPermissionsWindow : Window
    {
        public class IcdPermViewModel
        {
            public string IcdId { get; set; }
            public string IcdName { get; set; }
            public bool CanEdit { get; set; }
            public bool HasAccess { get; set; }
            public string StatusText => !HasAccess ? "No Access" : (CanEdit ? "Editor" : "Viewer");

            public bool IsEditor => HasAccess && CanEdit;
            public bool IsViewer => HasAccess && !CanEdit;
            public bool IsNoAccess => !HasAccess;
        }

        public class UserIcdPermissionDto
        {
            public string IcdId { get; set; }
            public bool CanEdit { get; set; }
        }

        private List<Icd> _icds;
        private List<User> _users;
        private User _selectedUser;

        public AdminPermissionsWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                ApiClient.EnsureAuthHeader();
                _icds = await LoadIcds();

                // Fetch all users (admin endpoint); UI will filter/search and allow selecting one.
                _users = await LoadUsersFromServer();

                // Default combobox list
                RefreshUserComboItems();
                SelectedUserTxt.Text = "Select a user to manage their ICD permissions.";
                PermGrid.ItemsSource = new List<IcdPermViewModel>();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message);
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

        private async System.Threading.Tasks.Task<List<Icd>> LoadIcds()
        {
            ApiClient.EnsureAuthHeader();
            using var req = new HttpRequestMessage(HttpMethod.Get, "api/icd/list");
            var res = await ApiClient.Client.SendAsync(req);
            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                MessageBox.Show("You are not logged in (401). Please log in again.");
                throw new InvalidOperationException("401 Unauthorized");
            }
            res.EnsureSuccessStatusCode();
            var icds = await res.Content.ReadFromJsonAsync<List<Icd>>();
            return icds ?? new List<Icd>();
        }

        private void UserSearchTxt_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshUserComboItems();
        }

        private void RefreshUserComboItems()
        {
            if (_users == null) return;

            var term = (UserSearchTxt.Text ?? string.Empty).Trim();
            IEnumerable<User> src = _users
                .Where(u => !u.IsAdmin)
                // Some legacy/invalid records can come back as "Unknown"; don't show those in the picker.
                .Where(u => !string.Equals(u.Username, "Unknown", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(term))
            {
                src = src.Where(u =>
                    (!string.IsNullOrWhiteSpace(u.Username) && u.Username.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(u.Email) && u.Email.Contains(term, StringComparison.OrdinalIgnoreCase)));
            }

            var list = src
                .OrderBy(u => u.Username ?? string.Empty)
                .ToList();

            UserCombo.ItemsSource = list;
        }

        private async void UserCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedUser = UserCombo.SelectedItem as User;
            await LoadSelectedUserPermissions();
        }

        private async System.Threading.Tasks.Task LoadSelectedUserPermissions()
        {
            if (_selectedUser == null)
            {
                SelectedUserTxt.Text = "Select a user to manage their ICD permissions.";
                PermGrid.ItemsSource = new List<IcdPermViewModel>();
                return;
            }

            SelectedUserTxt.Text = $"Selected: {_selectedUser.Username} ({_selectedUser.Email})";

            try
            {
                ApiClient.EnsureAuthHeader();

                using var req = new HttpRequestMessage(HttpMethod.Get, $"api/icd/admin/user/{_selectedUser.UserId}/permissions");
                var res = await ApiClient.Client.SendAsync(req);

                if (res.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    MessageBox.Show("Admin request was forbidden (403).\n\n" + GetAuthDebugString());
                    Close();
                    return;
                }

                res.EnsureSuccessStatusCode();
                var perms = await res.Content.ReadFromJsonAsync<List<UserIcdPermissionDto>>() ?? new List<UserIcdPermissionDto>();

                var list = new List<IcdPermViewModel>();
                foreach (var icd in _icds ?? new List<Icd>())
                {
                    var p = perms.FirstOrDefault(x => x.IcdId == icd.IcdId);
                    list.Add(new IcdPermViewModel
                    {
                        IcdId = icd.IcdId,
                        IcdName = icd.Name,
                        HasAccess = p != null,
                        CanEdit = p?.CanEdit ?? false
                    });
                }

                PermGrid.ItemsSource = list;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading permissions: " + ex.Message);
            }
        }

        // Standard block syntax for event handlers to ensure XAML parser visibility
        private async void GrantEdit_Click(object sender, RoutedEventArgs e)
        {
            await SetPermission(sender, true, false);
        }

        private async void GrantView_Click(object sender, RoutedEventArgs e)
        {
            await SetPermission(sender, false, false);
        }

        private async void Revoke_Click(object sender, RoutedEventArgs e)
        {
            await SetPermission(sender, false, true);
        }

        private async System.Threading.Tasks.Task SetPermission(object sender, bool canEdit, bool revoke)
        {
            if (_selectedUser == null) return;
            if ((sender as FrameworkElement)?.DataContext is not IcdPermViewModel icd) return;

            var req = new { UserId = _selectedUser.UserId, IcdId = icd.IcdId, CanEdit = canEdit, Revoke = revoke };
            ApiClient.EnsureAuthHeader();
            var res = await ApiClient.Client.PostAsJsonAsync("api/icd/admin/grant", req);

            if (res.IsSuccessStatusCode)
            {
                // Refresh list to show updated status
                await LoadSelectedUserPermissions();
            }
            else if (res.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                MessageBox.Show("Admin request was forbidden (403).\n\n" + GetAuthDebugString());
                Close();
            }
            else
            {
                MessageBox.Show("Failed to update permission.");
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}