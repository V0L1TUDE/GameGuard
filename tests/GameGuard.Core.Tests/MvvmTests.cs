using GameGuard.UI.Mvvm;
using GameGuard.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameGuard.Core.Tests;

public sealed class MvvmTests
{
    private sealed class TestViewModel : ViewModelBase
    {
        private string _name = string.Empty;
        public bool WasDisposed { get; private set; }
        public bool WasInitialized { get; private set; }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public override Task InitializeAsync()
        {
            WasInitialized = true;
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
            WasDisposed = true;
            base.Dispose();
        }
    }

    private sealed class OtherViewModel : ViewModelBase
    {
    }

    [Fact]
    public void ObservableObject_SetProperty_FiresPropertyChangedOnlyWhenValueChanged()
    {
        var vm = new TestViewModel();
        var changedProperties = new List<string?>();
        vm.PropertyChanged += (s, e) => changedProperties.Add(e.PropertyName);

        vm.Name = "NewName";
        Assert.Single(changedProperties);
        Assert.Equal("Name", changedProperties[0]);

        // Setting identical value should not fire PropertyChanged
        vm.Name = "NewName";
        Assert.Single(changedProperties);
    }

    [Fact]
    public void RelayCommand_ExecutesAndRespectsCanExecute()
    {
        bool executed = false;
        bool canRun = false;

        var command = new RelayCommand(() => executed = true, () => canRun);

        Assert.False(command.CanExecute(null));

        canRun = true;
        Assert.True(command.CanExecute(null));

        command.Execute(null);
        Assert.True(executed);
    }

    [Fact]
    public async Task AsyncRelayCommand_TracksIsExecutingAndExecutesTask()
    {
        var tcs = new TaskCompletionSource();
        var command = new AsyncRelayCommand(async () => await tcs.Task);

        Assert.False(command.IsExecuting);
        Assert.True(command.CanExecute(null));

        command.Execute(null);

        Assert.True(command.IsExecuting);
        Assert.False(command.CanExecute(null));

        tcs.SetResult();
        await Task.Yield();

        // Give dispatcher loop a tick
        for (int i = 0; i < 10 && command.IsExecuting; i++)
        {
            await Task.Delay(20);
        }

        Assert.False(command.IsExecuting);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public void AsyncRelayCommand_HandlesExceptionViaCallback()
    {
        Exception? caughtException = null;
        var command = new AsyncRelayCommand(
            () => throw new InvalidOperationException("Test error"),
            onException: ex => caughtException = ex);

        command.Execute(null);

        Assert.NotNull(caughtException);
        Assert.IsType<InvalidOperationException>(caughtException);
    }

    [Fact]
    public void NavigationService_NavigatesAndManagesLifecycle()
    {
        var services = new ServiceCollection();
        services.AddTransient<TestViewModel>();
        services.AddTransient<OtherViewModel>();
        services.AddSingleton<INavigationService, NavigationService>();

        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        bool changedFired = false;
        nav.CurrentViewModelChanged += (s, e) => changedFired = true;

        nav.NavigateTo<TestViewModel>();

        Assert.True(changedFired);
        Assert.IsType<TestViewModel>(nav.CurrentViewModel);
        var firstVm = (TestViewModel)nav.CurrentViewModel!;
        Assert.True(firstVm.WasInitialized);
        Assert.False(firstVm.WasDisposed);

        // Navigate to other view model
        nav.NavigateTo<OtherViewModel>();
        Assert.IsType<OtherViewModel>(nav.CurrentViewModel);
        Assert.True(firstVm.WasDisposed);
    }
}
