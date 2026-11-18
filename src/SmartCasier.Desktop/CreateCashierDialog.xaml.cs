using System.Windows;
using SmartCasier.Core;

namespace SmartCasier.Desktop;

public partial class CreateCashierDialog : Window
{
    private readonly AuthService _auth;
    private readonly AuthSession _admin;

    public CreateCashierDialog(PosDatabase database, AuthSession admin)
    {
        InitializeComponent();
        _auth = new AuthService(database);
        _admin = admin;
        Loaded += (_, _) => UsernameInput.Focus();
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var password = PasswordInput.Password;
            if (password != ConfirmInput.Password)
                throw new ArgumentException("Konfirmasi password tidak cocok.");
            _auth.CreateCashier(_admin, UsernameInput.Text, password);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            PasswordInput.Clear();
            ConfirmInput.Clear();
        }
    }
}
