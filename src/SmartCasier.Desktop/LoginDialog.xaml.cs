using System.Windows;
using SmartCasier.Core;

namespace SmartCasier.Desktop;

/// <summary>First-run bootstrap or normal login. No built-in passwords.</summary>
public partial class LoginDialog : Window
{
    private readonly AuthService _auth;
    private readonly bool _setup;
    public AuthSession? Session { get; private set; }

    public LoginDialog(PosDatabase database)
    {
        InitializeComponent();
        _auth = new AuthService(database);
        _setup = !_auth.HasUsers();
        if (_setup)
        {
            Title = "Buat Admin Pertama — Smart Casier";
            Heading.Text = "Buat Admin Pertama";
            Explanation.Text = "Database baru belum memiliki pengguna. " +
                "Buat username dan password sendiri (minimal 12 karakter). " +
                "Jangan gunakan password toko sebenarnya saat menjalankan mode demo.";
            ConfirmationPanel.Visibility = Visibility.Visible;
            SubmitButton.Content = "Buat Admin dan Masuk";
        }
        Loaded += (_, _) => UsernameBox.Focus();
    }

    private void Submit_Click(object sender, RoutedEventArgs e)
    {
        SubmitButton.IsEnabled = false;
        try
        {
            var username = UsernameBox.Text.Trim();
            var password = PasswordInput.Password;
            if (_setup)
            {
                if (password != ConfirmationInput.Password)
                    throw new ArgumentException("Konfirmasi password tidak cocok.");
                _auth.CreateFirstAdmin(username, password);
            }
            Session = _auth.Login(username, password);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = "Autentikasi gagal: " + ex.Message;
            PasswordInput.Clear();
            ConfirmationInput.Clear();
        }
        finally
        {
            SubmitButton.IsEnabled = true;
        }
    }
}
