using GameGuard.UI.ViewModels;

namespace GameGuard.UI.Mvvm;

public interface INavigationService
{
    ViewModelBase? CurrentViewModel { get; }
    event EventHandler? CurrentViewModelChanged;

    void NavigateTo<TViewModel>() where TViewModel : ViewModelBase;
    void NavigateTo(ViewModelBase viewModel);
}
