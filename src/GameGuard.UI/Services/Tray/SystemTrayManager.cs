using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;
using Microsoft.Extensions.Logging;

namespace GameGuard.UI.Services.Tray;

public sealed class SystemTrayManager : ISystemTrayManager
{
    private const int WM_APP = 0x8000;
    private const int WM_TRAY_CALLBACK = WM_APP + 101;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_LBUTTONDBLCLK = 0x0203;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;

    private const uint NIIF_NONE = 0x00000000;
    private const uint NIIF_INFO = 0x00000001;
    private const uint NIIF_WARNING = 0x00000002;
    private const uint NIIF_ERROR = 0x00000003;

    private static readonly IntPtr IDI_SHIELD = new(32518);
    private static readonly IntPtr IDI_APPLICATION = new(32512);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private readonly INotificationService _notificationService;
    private readonly IAdminAuthService _authService;
    private readonly IDialogService _dialogService;
    private readonly IAdminOverrideRepository _overrideRepository;
    private readonly ITimeTrackerService _timeTracker;
    private readonly ISystemClock _clock;
    private readonly ILogger<SystemTrayManager>? _logger;

    private Window? _mainWindow;
    private IntPtr _hwnd;
    private HwndSource? _hwndSource;
    private bool _isCreated;
    private bool _isDisposed;
    private ContextMenu? _contextMenu;
    private MenuItem? _statusMenuItem;
    private bool _allowShutdown;

    public bool AllowShutdown => _allowShutdown;

    public SystemTrayManager(
        INotificationService notificationService,
        IAdminAuthService authService,
        IDialogService dialogService,
        IAdminOverrideRepository overrideRepository,
        ITimeTrackerService timeTracker,
        ISystemClock clock,
        ILogger<SystemTrayManager>? logger = null)
    {
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _overrideRepository = overrideRepository ?? throw new ArgumentNullException(nameof(overrideRepository));
        _timeTracker = timeTracker ?? throw new ArgumentNullException(nameof(timeTracker));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger;

        _notificationService.NotificationDispatched += OnNotificationDispatched;
    }

    public void Initialize(Window mainWindow)
    {
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        var helper = new WindowInteropHelper(_mainWindow);
        _hwnd = helper.EnsureHandle();

        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);

        CreateTrayIcon();
        CreateContextMenu();

        // Hook main window close/minimize to tray
        _mainWindow.Closing += OnMainWindowClosing;
        _mainWindow.StateChanged += OnMainWindowStateChanged;
    }

    private void CreateTrayIcon()
    {
        var iconHandle = LoadIcon(IntPtr.Zero, IDI_SHIELD);
        if (iconHandle == IntPtr.Zero)
        {
            iconHandle = LoadIcon(IntPtr.Zero, IDI_APPLICATION);
        }

        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = (int)(NIF_MESSAGE | NIF_ICON | NIF_TIP),
            uCallbackMessage = WM_TRAY_CALLBACK,
            hIcon = iconHandle,
            szTip = "GameGuard - Offline Time Defense"
        };

        _isCreated = Shell_NotifyIcon(NIM_ADD, ref nid);
    }

    private void CreateContextMenu()
    {
        _contextMenu = new ContextMenu
        {
            Background = new SolidColorBrush(Color.FromRgb(0x13, 0x17, 0x22)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x25, 0x2C, 0x40)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4)
        };

        _statusMenuItem = new MenuItem
        {
            Header = "GameGuard: Protected - Gaming Idle",
            IsEnabled = false,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76))
        };
        _contextMenu.Items.Add(_statusMenuItem);
        _contextMenu.Items.Add(new Separator { Background = new SolidColorBrush(Color.FromRgb(0x25, 0x2C, 0x40)) });

        var openItem = new MenuItem { Header = "Open GameGuard" };
        openItem.Click += (_, _) => RestoreMainWindow();
        _contextMenu.Items.Add(openItem);

        var bonusItem = new MenuItem { Header = "Grant +15m Bonus" };
        bonusItem.Click += async (_, _) => await OnQuickBonusClickedAsync();
        _contextMenu.Items.Add(bonusItem);

        var lockItem = new MenuItem { Header = "Emergency Lock All" };
        lockItem.Click += async (_, _) => await OnEmergencyLockClickedAsync();
        _contextMenu.Items.Add(lockItem);

        _contextMenu.Items.Add(new Separator { Background = new SolidColorBrush(Color.FromRgb(0x25, 0x2C, 0x40)) });

        var exitItem = new MenuItem
        {
            Header = "Exit GameGuard",
            Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44))
        };
        exitItem.Click += async (_, _) => await OnExitClickedAsync();
        _contextMenu.Items.Add(exitItem);
    }

    public void UpdateStatus(string statusText)
    {
        if (_statusMenuItem != null)
        {
            _statusMenuItem.Header = statusText;
        }

        if (!_isCreated || _hwnd == IntPtr.Zero) return;

        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = (int)NIF_TIP,
            szTip = statusText.Length > 120 ? statusText[..120] : statusText
        };

        Shell_NotifyIcon(NIM_MODIFY, ref nid);
    }

    public void ShowBalloon(string title, string message, NotificationType type = NotificationType.Information)
    {
        if (!_isCreated || _hwnd == IntPtr.Zero) return;

        uint infoFlags = type switch
        {
            NotificationType.Warning => NIIF_WARNING,
            NotificationType.Danger => NIIF_ERROR,
            _ => NIIF_INFO
        };

        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = (int)NIF_INFO,
            szInfoTitle = title.Length > 60 ? title[..60] : title,
            szInfo = message.Length > 250 ? message[..250] : message,
            dwInfoFlags = (int)infoFlags
        };

        Shell_NotifyIcon(NIM_MODIFY, ref nid);
    }

    private void OnNotificationDispatched(object? sender, NotificationEventArgs e)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            ShowBalloon(e.Title, e.Message, e.Type);
        });
    }

    private void RestoreMainWindow()
    {
        if (_mainWindow == null) return;

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.ShowInTaskbar = true;
        _mainWindow.Activate();
    }

    private void OnMainWindowStateChanged(object? sender, EventArgs e)
    {
        if (_mainWindow?.WindowState == WindowState.Minimized)
        {
            _mainWindow.ShowInTaskbar = false;
        }
    }

    private void OnMainWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowShutdown)
        {
            // Minimize to tray instead of terminating
            e.Cancel = true;
            if (_mainWindow != null)
            {
                _mainWindow.WindowState = WindowState.Minimized;
                _mainWindow.ShowInTaskbar = false;
            }

            ShowBalloon(
                "GameGuard Active in Tray",
                "GameGuard is continuing to protect in the background. Click tray icon to open.",
                NotificationType.Information);
        }
    }

    private async Task OnQuickBonusClickedAsync()
    {
        bool hasPassword = await _authService.IsPasswordConfiguredAsync().ConfigureAwait(true);
        if (hasPassword)
        {
            var password = await _dialogService.PromptPasswordAsync(
                "Admin PIN Required",
                "Enter administrator password to authorize 15-minute playtime bonus:").ConfigureAwait(true);

            if (password == null) return;

            bool valid = await _authService.ValidatePasswordAsync(password).ConfigureAwait(true);
            if (!valid)
            {
                await _dialogService.ShowMessageAsync("Authentication Failed", "Incorrect administrator password.", isError: true).ConfigureAwait(true);
                return;
            }
        }

        var activeSessions = _timeTracker.GetActiveSessions();
        var active = activeSessions.FirstOrDefault();
        Guid? targetGameId = active?.GameProfileId;

        var bonus = AdminOverride.CreateBonusTime(
            targetGameId,
            15,
            _clock.UtcNow,
            TimeSpan.FromHours(4),
            "Quick +15m bonus from system tray");

        await _overrideRepository.AddOverrideAsync(bonus).ConfigureAwait(true);
        ShowBalloon("Bonus Granted", "+15 minutes granted successfully.", NotificationType.Success);
    }

    private async Task OnEmergencyLockClickedAsync()
    {
        bool hasPassword = await _authService.IsPasswordConfiguredAsync().ConfigureAwait(true);
        if (hasPassword)
        {
            var password = await _dialogService.PromptPasswordAsync(
                "Emergency Lockdown",
                "Enter administrator password to authorize emergency game lock:").ConfigureAwait(true);

            if (password == null) return;

            bool valid = await _authService.ValidatePasswordAsync(password).ConfigureAwait(true);
            if (!valid)
            {
                await _dialogService.ShowMessageAsync("Authentication Failed", "Incorrect administrator password.", isError: true).ConfigureAwait(true);
                return;
            }
        }

        var lockOverride = AdminOverride.CreateEmergencyLock(
            null,
            _clock.UtcNow,
            TimeSpan.FromHours(24),
            "Emergency lock triggered from system tray");

        await _overrideRepository.AddOverrideAsync(lockOverride).ConfigureAwait(true);
        ShowBalloon("Emergency Lockdown Active", "All monitored games are immediately locked.", NotificationType.Danger);
    }

    private async Task OnExitClickedAsync()
    {
        bool hasPassword = await _authService.IsPasswordConfiguredAsync().ConfigureAwait(true);
        if (hasPassword)
        {
            var password = await _dialogService.PromptPasswordAsync(
                "Exit GameGuard",
                "Administrator password required to exit GameGuard:").ConfigureAwait(true);

            if (password == null) return;

            bool valid = await _authService.ValidatePasswordAsync(password).ConfigureAwait(true);
            if (!valid)
            {
                await _dialogService.ShowMessageAsync("Authentication Failed", "Incorrect administrator password.", isError: true).ConfigureAwait(true);
                return;
            }
        }

        _allowShutdown = true;
        RemoveTrayIcon();
        Application.Current.Shutdown();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAY_CALLBACK)
        {
            int action = lParam.ToInt32();
            if (action == WM_LBUTTONUP || action == WM_LBUTTONDBLCLK)
            {
                RestoreMainWindow();
                handled = true;
            }
            else if (action == WM_RBUTTONUP && _contextMenu != null)
            {
                GetCursorPos(out POINT pt);
                _contextMenu.Placement = PlacementMode.AbsolutePoint;
                _contextMenu.HorizontalOffset = pt.X;
                _contextMenu.VerticalOffset = pt.Y;
                _contextMenu.IsOpen = true;
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    private void RemoveTrayIcon()
    {
        if (!_isCreated || _hwnd == IntPtr.Zero) return;

        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1
        };

        Shell_NotifyIcon(NIM_DELETE, ref nid);
        _isCreated = false;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _notificationService.NotificationDispatched -= OnNotificationDispatched;

        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        RemoveTrayIcon();
    }
}
