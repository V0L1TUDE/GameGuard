using System.IO;
using System.Windows;
using GameGuard.Core.Models;
using GameGuard.UI.Services;
using Microsoft.Win32;

namespace GameGuard.UI.Views.Dialogs;

public partial class GameProfileEditWindow : Window
{
    private readonly Guid? _profileId;

    public GameProfileEditModel? Result { get; private set; }

    public GameProfileEditWindow(GameProfile? existing = null)
    {
        InitializeComponent();

        if (existing != null)
        {
            _profileId = existing.Id;
            TitleText.Text = $"Edit {existing.Name}";
            NameInput.Text = existing.Name;
            ExePathInput.Text = existing.ExecutablePath ?? string.Empty;
            ProcessNamesInput.Text = string.Join(", ", existing.ProcessNames);
            MonitoredCheck.IsChecked = existing.IsMonitored;
        }
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var openDialog = new OpenFileDialog
        {
            Title = "Select Game Executable",
            Filter = "Game Executables (*.exe)|*.exe|All Files (*.*)|*.*",
            CheckFileExists = true
        };

        if (openDialog.ShowDialog(this) == true)
        {
            ExePathInput.Text = openDialog.FileName;

            string fileName = Path.GetFileName(openDialog.FileName);
            string nameWithoutExt = Path.GetFileNameWithoutExtension(openDialog.FileName);

            if (string.IsNullOrWhiteSpace(NameInput.Text))
            {
                NameInput.Text = nameWithoutExt;
            }

            if (string.IsNullOrWhiteSpace(ProcessNamesInput.Text))
            {
                ProcessNamesInput.Text = fileName;
            }
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameInput.Text))
        {
            ErrorText.Text = "Game title is required.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        if (string.IsNullOrWhiteSpace(ProcessNamesInput.Text))
        {
            ErrorText.Text = "At least one process name is required.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        Result = new GameProfileEditModel
        {
            Id = _profileId,
            Name = NameInput.Text.Trim(),
            ExecutablePath = ExePathInput.Text.Trim(),
            ProcessNames = ProcessNamesInput.Text.Trim(),
            IsMonitored = MonitoredCheck.IsChecked == true
        };

        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
