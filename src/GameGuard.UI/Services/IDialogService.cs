using GameGuard.Core.Models;

namespace GameGuard.UI.Services;

public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message);
    Task ShowMessageAsync(string title, string message, bool isError = false);
    Task<string?> OpenFileDialogAsync(string title, string filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*");
    Task<string?> PromptPasswordAsync(string title, string message);
    Task<GameProfileEditModel?> PromptGameProfileDialogAsync(GameProfile? existing = null);
    Task<AdminOverrideCreateModel?> PromptAddOverrideDialogAsync(IReadOnlyList<GameProfile> games);
}
