using System.Net.Http;
using IcdControl.Models;

namespace IcdControl.Client
{
 public static class ApiClient
 {
 // Update base address if your server listens on different port
 public static HttpClient Client { get; } = new HttpClient { BaseAddress = new System.Uri("http://localhost:5273/") };

 public static User CurrentUser { get; private set; }

 public static void SetCurrentUser(User user)
 {
 if (user == null)
 {
 // clear user and header
 CurrentUser = null;
 if (Client.DefaultRequestHeaders.Contains("X-UserId"))
 Client.DefaultRequestHeaders.Remove("X-UserId");
 return;
 }
 CurrentUser = user;
 EnsureAuthHeader();
 }

 public static void EnsureAuthHeader()
 {
 if (CurrentUser == null) return;
 if (Client.DefaultRequestHeaders.Contains("X-UserId"))
 Client.DefaultRequestHeaders.Remove("X-UserId");
 Client.DefaultRequestHeaders.Add("X-UserId", CurrentUser.UserId);
 }
 }
}
