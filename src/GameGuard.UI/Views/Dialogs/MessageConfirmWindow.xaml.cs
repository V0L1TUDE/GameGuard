using System.Windows;

namespace GameGuard.UI.Views.Dialogs;

public partial class MessageConfirmWindow : Window
{
    public MessageConfirmWindow(string title, string message, bool isConfirmation = true)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;

        if (!isConfirmation)
        {
            CancelBtn.Visibility = Visibility.Collapsed;
            OkBtn.Content = "OK";
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
