using System.Windows;
using System.Windows.Controls;
using GameGuard.Core.Models;
using GameGuard.UI.Services;

namespace GameGuard.UI.Views.Dialogs;

public partial class AddOverrideWindow : Window
{
    private readonly IReadOnlyList<GameProfile> _games;

    public AdminOverrideCreateModel? Result { get; private set; }

    public AddOverrideWindow(IReadOnlyList<GameProfile> games)
    {
        InitializeComponent();
        _games = games;

        // Populate game selector
        GameSelector.Items.Add(new ComboBoxItem { Content = "All Monitored Games (Global)", Tag = null });
        foreach (var game in games)
        {
            GameSelector.Items.Add(new ComboBoxItem { Content = game.Name, Tag = game.Id });
        }
        GameSelector.SelectedIndex = 0;

        // Populate type selector
        TypeSelector.Items.Add(new ComboBoxItem { Content = "Bonus Playtime (+Minutes to Daily Limit)", Tag = OverrideType.BonusTime });
        TypeSelector.Items.Add(new ComboBoxItem { Content = "Temporary Unlock (Unconditional Play Window)", Tag = OverrideType.TemporaryUnlock });
        TypeSelector.Items.Add(new ComboBoxItem { Content = "Emergency Lockdown (Force Immediate Lock)", Tag = OverrideType.EmergencyLock });
        TypeSelector.SelectedIndex = 0;
    }

    private void OnTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TypeSelector.SelectedItem is ComboBoxItem item && item.Tag is OverrideType type)
        {
            if (type == OverrideType.BonusTime)
            {
                DurationLabel.Text = "Additional Playtime (Minutes)";
                MinutesInput.Text = "30";
                ReasonInput.Text = "Parent approved bonus time";
            }
            else if (type == OverrideType.TemporaryUnlock)
            {
                DurationLabel.Text = "Unlock Duration (Minutes)";
                MinutesInput.Text = "45";
                ReasonInput.Text = "Temporary playtime unlock";
            }
            else
            {
                DurationLabel.Text = "Lockdown Duration (Minutes)";
                MinutesInput.Text = "120";
                ReasonInput.Text = "Emergency lockdown enforced";
            }
        }
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(MinutesInput.Text.Trim(), out int minutes) || minutes <= 0)
        {
            ErrorText.Text = "Please enter a valid positive number of minutes.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        Guid? selectedGameId = null;
        if (GameSelector.SelectedItem is ComboBoxItem gameItem && gameItem.Tag is Guid gameId)
        {
            selectedGameId = gameId;
        }

        var selectedType = OverrideType.BonusTime;
        if (TypeSelector.SelectedItem is ComboBoxItem typeItem && typeItem.Tag is OverrideType type)
        {
            selectedType = type;
        }

        Result = new AdminOverrideCreateModel
        {
            GameProfileId = selectedGameId,
            Type = selectedType,
            BonusMinutes = selectedType == OverrideType.BonusTime ? minutes : 0,
            DurationMinutes = minutes,
            Reason = ReasonInput.Text.Trim()
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
