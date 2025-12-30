using System.Net.Http;
using IcdControl.Models;

namespace IcdControl.Client
{
 public static class ApiClient
 {
 // Update base address if your server listens on different port
 public static HttpClient Client { get; } = new HttpClient { BaseAddress = new System.Uri("http://localhost:5273/") };

 public static User? CurrentUser { get; private set; }

 public static void SetCurrentUser(User? user)
 {
 if (user == null)
 {
 // clear user and header
 CurrentUser = null;
 Client.DefaultRequestHeaders.Remove("X-UserId");
 return;
 }
 CurrentUser = user;
 EnsureAuthHeader();
 }

 public static void EnsureAuthHeader()
 {
 if (CurrentUser == null) return;
 // Always remove first (safe even if missing) to prevent multiple values like "id1,id2".
 Client.DefaultRequestHeaders.Remove("X-UserId");
 // TryAddWithoutValidation avoids strict header formatting issues.
 if (!Client.DefaultRequestHeaders.TryAddWithoutValidation("X-UserId", CurrentUser.UserId))
 {
 // Fallback
 Client.DefaultRequestHeaders.Add("X-UserId", CurrentUser.UserId);
 }
 }
 }
}
