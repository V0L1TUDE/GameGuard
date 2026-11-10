using GameGuard.Core.Models;
using GameGuard.UI.Services;

namespace GameGuard.Core.Tests.Mocks;

public sealed class MockDialogService : IDialogService
{
    public bool ConfirmResult { get; set; } = true;
    public string? PasswordToReturn { get; set; } = "ValidPassword123";
    public GameProfileEditModel? GameProfileResult { get; set; }
    public AdminOverrideCreateModel? AddOverrideResult { get; set; }
    public string? FileDialogResult { get; set; }

    public List<string> ShownMessages { get; } = new();
    public List<string> ConfirmedTitles { get; } = new();

    public Task<bool> ConfirmAsync(string title, string message)
    {
        ConfirmedTitles.Add(title);
        return Task.FromResult(ConfirmResult);
    }

    public Task ShowMessageAsync(string title, string message, bool isError = false)
    {
        ShownMessages.Add($"{title}: {message}");
        return Task.CompletedTask;
    }

    public Task<string?> OpenFileDialogAsync(string title, string filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*")
    {
        return Task.FromResult(FileDialogResult);
    }

    public Task<string?> PromptPasswordAsync(string title, string message)
    {
        return Task.FromResult(PasswordToReturn);
    }

    public Task<GameProfileEditModel?> PromptGameProfileDialogAsync(GameProfile? existing = null)
    {
        return Task.FromResult(GameProfileResult);
    }

    public Task<AdminOverrideCreateModel?> PromptAddOverrideDialogAsync(IReadOnlyList<GameProfile> games)
    {
        return Task.FromResult(AddOverrideResult);
    }
}
