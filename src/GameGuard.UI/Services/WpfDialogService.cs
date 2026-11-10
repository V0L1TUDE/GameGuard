using System.Windows;
using GameGuard.Core.Models;
using GameGuard.UI.Views.Dialogs;
using Microsoft.Win32;

namespace GameGuard.UI.Services;

public sealed class WpfDialogService : IDialogService
{
    public Task<bool> ConfirmAsync(string title, string message)
    {
        return Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new MessageConfirmWindow(title, message, isConfirmation: true)
            {
                Owner = Application.Current.MainWindow
            };
            return Task.FromResult(dialog.ShowDialog() == true);
        });
    }

    public Task ShowMessageAsync(string title, string message, bool isError = false)
    {
        return Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new MessageConfirmWindow(title, message, isConfirmation: false)
            {
                Owner = Application.Current.MainWindow
            };
            dialog.ShowDialog();
            return Task.CompletedTask;
        });
    }

    public Task<string?> OpenFileDialogAsync(string title, string filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*")
    {
        return Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new OpenFileDialog
            {
                Title = title,
                Filter = filter,
                CheckFileExists = true
            };

            bool? result = dialog.ShowDialog(Application.Current.MainWindow);
            return Task.FromResult(result == true ? dialog.FileName : null);
        });
    }

    public Task<string?> PromptPasswordAsync(string title, string message)
    {
        return Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new PasswordPromptWindow(title, message)
            {
                Owner = Application.Current.MainWindow
            };
            bool? result = dialog.ShowDialog();
            return Task.FromResult(result == true ? dialog.EnteredPassword : null);
        });
    }

    public Task<GameProfileEditModel?> PromptGameProfileDialogAsync(GameProfile? existing = null)
    {
        return Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new GameProfileEditWindow(existing)
            {
                Owner = Application.Current.MainWindow
            };
            bool? result = dialog.ShowDialog();
            return Task.FromResult(result == true ? dialog.Result : null);
        });
    }

    public Task<AdminOverrideCreateModel?> PromptAddOverrideDialogAsync(IReadOnlyList<GameProfile> games)
    {
        return Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new AddOverrideWindow(games)
            {
                Owner = Application.Current.MainWindow
            };
            bool? result = dialog.ShowDialog();
            return Task.FromResult(result == true ? dialog.Result : null);
        });
    }
}
