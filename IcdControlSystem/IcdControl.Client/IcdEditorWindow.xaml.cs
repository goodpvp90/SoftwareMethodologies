using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using IcdControl.Models;
using System.Text.Json;

namespace IcdControl.Client
{
 public partial class IcdEditorWindow : Window
 {
 private readonly string _id;
 private HttpClient _http = new HttpClient { BaseAddress = new Uri("http://localhost:5273/") };
 private Icd _icd;
 public IcdEditorWindow(string id)
 {
 _id = id;
 InitializeComponent();
 Loaded += IcdEditorWindow_Loaded;
 }

 private async void IcdEditorWindow_Loaded(object sender, RoutedEventArgs e)
 {
 try
 {
 _icd = await _http.GetFromJsonAsync<Icd>($"api/icd/{_id}");
 if (_icd == null) { MessageBox.Show("ICD not found"); this.Close(); return; }
 NameTxt.Text = _icd.Name;
 VersionTxt.Text = _icd.Version.ToString();
 MessagesTxt.Text = JsonSerializer.Serialize(_icd.Messages, new JsonSerializerOptions { WriteIndented = true });
 }
 catch (Exception ex)
 {
 MessageBox.Show($"Failed to load ICD: {ex.Message}");
 this.Close();
 }
 }

 private async void Save_Click(object sender, RoutedEventArgs e)
 {
 try
 {
 // try parse messages as JSON array; if invalid, show error
 var msgs = JsonSerializer.Deserialize<List<Message>>(MessagesTxt.Text);
 if (msgs == null) msgs = new List<Message>();
 _icd.Messages = msgs;
 var res = await _http.PostAsJsonAsync("api/icd/save", _icd);
 if (res.IsSuccessStatusCode)
 {
 MessageBox.Show("Saved");
 }
 else MessageBox.Show($"Save failed: {(int)res.StatusCode} {res.ReasonPhrase}");
 }
 catch (Exception ex)
 {
 MessageBox.Show($"Save failed: {ex.Message}");
 }
 }

 private void Close_Click(object sender, RoutedEventArgs e)
 {
 this.Close();
 }
 }
}
