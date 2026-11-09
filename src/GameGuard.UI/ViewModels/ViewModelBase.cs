using GameGuard.UI.Mvvm;

namespace GameGuard.UI.ViewModels;

public abstract class ViewModelBase : ObservableObject, IDisposable
{
    private string _title = string.Empty;
    private bool _isBusy;

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public virtual Task InitializeAsync() => Task.CompletedTask;

    public virtual void Dispose()
    {
    }
}
