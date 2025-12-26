using ICDControl;
using ICDControl.Logic;
using System.Windows;

namespace ICDControl
{
    public partial class LoginWindow : Window
    {
        private IcdController _controller = new IcdController();

        public LoginWindow()
        {
            InitializeComponent();
        }

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            string email = EmailTxt.Text;
            string pass = PassBox.Password;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(pass))
            {
                MessageBox.Show("Please enter email and password.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var user = _controller.Login(email, pass);
            if (user != null)
            {
                // מעבר למסך הראשי
                MainWindow dashboard = new MainWindow(user);
                dashboard.Show();
                this.Close();
            }
            else
            {
                MessageBox.Show("Invalid email or password.", "Login Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Register_Click(object sender, RoutedEventArgs e)
        {
            // הרשמה פשוטה להדגמה
            if (string.IsNullOrWhiteSpace(EmailTxt.Text) || string.IsNullOrWhiteSpace(PassBox.Password))
            {
                MessageBox.Show("Fill email and password to register.", "Info");
                return;
            }
            try
            {
                _controller.Register("New User", EmailTxt.Text, PassBox.Password);
                MessageBox.Show("User registered successfully! Please login.", "Success");
            }
            catch
            {
                MessageBox.Show("User already exists or database error.", "Error");
            }
        }
    }
}