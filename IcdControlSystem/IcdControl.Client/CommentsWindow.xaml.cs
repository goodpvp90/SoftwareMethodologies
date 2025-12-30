using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Text.Json.Serialization;

namespace IcdControl.Client
{
    public class CommentInfo
    {
        [JsonPropertyName("commentId")]
        public string CommentId { get; set; }
        
        [JsonPropertyName("userId")]
        public string UserId { get; set; }
        
        [JsonPropertyName("commentText")]
        public string CommentText { get; set; }
        
        [JsonPropertyName("createdAt")]
        public string CreatedAt { get; set; }
    }

    public partial class CommentsWindow : Window
    {
        private string _icdId;

        public CommentsWindow(string icdId)
        {
            InitializeComponent();
            _icdId = icdId;
            Loaded += CommentsWindow_Loaded;
        }

        private async void CommentsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadComments();
        }

        private async System.Threading.Tasks.Task LoadComments()
        {
            try
            {
                var comments = await ApiClient.Client.GetFromJsonAsync<List<CommentInfo>>($"api/icd/{_icdId}/comments");
                if (comments != null)
                {
                    CommentsList.ItemsSource = comments;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load comments: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void AddCommentBtn_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(CommentTxt.Text))
            {
                MessageBox.Show("Please enter a comment.", "Add Comment", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var response = await ApiClient.Client.PostAsJsonAsync($"api/icd/{_icdId}/comments", new { CommentText = CommentTxt.Text });
                if (response.IsSuccessStatusCode)
                {
                    CommentTxt.Text = "";
                    await LoadComments();
                    MessageBox.Show("Comment added successfully.", "Add Comment", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Failed to add comment: {response.ReasonPhrase}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to add comment: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

