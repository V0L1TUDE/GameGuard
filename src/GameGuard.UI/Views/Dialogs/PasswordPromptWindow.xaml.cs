using System.Windows;

namespace GameGuard.UI.Views.Dialogs;

public partial class PasswordPromptWindow : Window
{
    public string? EnteredPassword { get; private set; }

    public PasswordPromptWindow(string title, string message)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        Loaded += (s, e) => PasswordInput.Focus();
    }

    private void OnAuthenticateClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PasswordInput.Password))
        {
            ErrorText.Text = "Password cannot be empty.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        EnteredPassword = PasswordInput.Password;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
