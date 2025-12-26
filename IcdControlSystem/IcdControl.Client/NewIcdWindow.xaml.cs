using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using IcdControl.Models;

namespace IcdControl.Client
{
 public partial class NewIcdWindow : Window
 {
 private HttpClient _http = new HttpClient { BaseAddress = new Uri("http://localhost:5273/") };
 public NewIcdWindow()
 {
 InitializeComponent();
 }

 private async void Save_Click(object sender, RoutedEventArgs e)
 {
 var name = NameTxt.Text?.Trim();
 if (string.IsNullOrEmpty(name)) { MessageBox.Show("Name required"); return; }
 if (!double.TryParse(VersionTxt.Text?.Trim(), out var ver)) ver =1.0;
 var icd = new Icd { Name = name, Version = ver, Description = DescTxt.Text };
 try
 {
 var res = await _http.PostAsJsonAsync("api/icd/save", icd);
 if (res.IsSuccessStatusCode)
 {
 this.DialogResult = true;
 this.Close();
 }
 else
 {
 MessageBox.Show($"Save failed: {(int)res.StatusCode} {res.ReasonPhrase}");
 }
 }
 catch (Exception ex)
 {
 MessageBox.Show($"Save failed: {ex.Message}");
 }
 }

 private void Cancel_Click(object sender, RoutedEventArgs e)
 {
 this.DialogResult = false;
 this.Close();
 }
 }
}
