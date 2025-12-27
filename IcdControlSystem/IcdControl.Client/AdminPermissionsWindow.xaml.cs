using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using IcdControl.Models;

namespace IcdControl.Client
{
    public partial class AdminPermissionsWindow : Window
    {
        public class UserPermViewModel
        {
            public string UserId { get; set; }
            public string Username { get; set; }
            public bool CanEdit { get; set; }
            public bool HasAccess { get; set; }
            public string StatusText => !HasAccess ? "No Access" : (CanEdit ? "Editor" : "Viewer");
        }

        public class PermissionDto
        {
            public string UserId { get; set; }
            public bool CanEdit { get; set; }
        }

        private List<Icd> _icds;
        private List<User> _users;

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
                _icds = await ApiClient.Client.GetFromJsonAsync<List<Icd>>("api/icd/list");
                IcdCombo.ItemsSource = _icds;
                // Fetch all non-admin users to manage their permissions
                _users = await ApiClient.Client.GetFromJsonAsync<List<User>>("api/icd/admin/users");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message);
            }
        }

        private async void IcdCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IcdCombo.SelectedItem is Icd selectedIcd && _users != null)
            {
                try
                {
                    // Fetch existing permissions for this ICD
                    var perms = await ApiClient.Client.GetFromJsonAsync<List<PermissionDto>>($"api/icd/admin/icd/{selectedIcd.IcdId}/permissions");
                    var list = new List<UserPermViewModel>();

                    foreach (var user in _users)
                    {
                        if (user.IsAdmin) continue; // Skip admins

                        var p = perms.FirstOrDefault(x => x.UserId == user.UserId);
                        list.Add(new UserPermViewModel
                        {
                            UserId = user.UserId,
                            Username = user.Username,
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
            if (IcdCombo.SelectedItem is not Icd icd) return;
            if ((sender as FrameworkElement)?.DataContext is not UserPermViewModel user) return;

            var req = new { UserId = user.UserId, IcdId = icd.IcdId, CanEdit = canEdit, Revoke = revoke };
            var res = await ApiClient.Client.PostAsJsonAsync("api/icd/admin/grant", req);

            if (res.IsSuccessStatusCode)
            {
                // Refresh the list to show updated status
                IcdCombo_SelectionChanged(null, null);
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