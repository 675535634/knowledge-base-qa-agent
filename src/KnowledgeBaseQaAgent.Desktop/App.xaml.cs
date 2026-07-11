using System.Windows;
using KnowledgeBaseQaAgent.Desktop.Services;
using KnowledgeBaseQaAgent.Desktop.ViewModels;
using System.Windows.Interop;
using System.Windows.Input;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace KnowledgeBaseQaAgent.Desktop;

public partial class App
{
    private const int AdminHotKeyId = 0x5141;
    private const int AdminNumpadHotKeyId = 0x5142;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;
    private const int WmHotKey = 0x0312;
    private static readonly TimeSpan GreetingCooldown = TimeSpan.FromSeconds(10);

    private DesktopPetWindow? _petWindow;
    private VisitorWindow? _visitorWindow;
    private MainWindow? _adminWindow;
    private MainViewModel? _mainViewModel;
    private DesktopPetViewModel? _desktopPetViewModel;
    private WakeWordService? _wakeWordService;
    private Models.AppSettings? _currentSettings;
    private Forms.NotifyIcon? _notifyIcon;
    private HwndSource? _hotKeySource;
    private IntPtr _hotKeyWindowHandle;
    private DateTimeOffset _lastGreetingStartedAt = DateTimeOffset.MinValue;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var paths = AppPaths.FromStartupArgs(e.Args);
        var settingsService = new SettingsService(paths);
        ICredentialService credentialService = paths.IsPortable
            ? new PortableCredentialService(paths)
            : new CredentialManagerService();
        var settings = await settingsService.LoadAsync();
        _currentSettings = settings;
        var providerRegistry = new ProviderRegistry(settingsService, credentialService, settings);
        var knowledgeStore = new SqliteKnowledgeStore(paths);
        await knowledgeStore.InitializeAsync();

        var ragService = new RagService(knowledgeStore, new DocumentParser(), new TextChunker(), providerRegistry);
        var speechService = new SpeechCoordinator(providerRegistry);
        var llmProviderCatalog = new LlmProviderCatalogService(credentialService);
        var avatar = new AvatarStateService();

        _mainViewModel = new MainViewModel(
            settings,
            paths,
            settingsService,
            credentialService,
            providerRegistry,
            knowledgeStore,
            ragService,
            speechService,
            llmProviderCatalog,
            avatar);
        _mainViewModel.SettingsApplied += (_, updatedSettings) =>
        {
            _currentSettings = updatedSettings;
            _desktopPetViewModel?.ApplySettings(updatedSettings);
            if (_notifyIcon is not null)
            {
                _notifyIcon.Text = TrimNotifyText(updatedSettings.AssistantName);
            }

            RestartWakeWordService(updatedSettings);
        };

        _desktopPetViewModel = new DesktopPetViewModel(avatar, settings);
        _petWindow = new DesktopPetWindow
        {
            DataContext = _desktopPetViewModel
        };
        _petWindow.InteractionRequested += (_, _) => ShowVisitorPanel(playGreeting: true);
        _petWindow.Show();
        RegisterAdminHotKey();

        CreateTrayIcon();
        RestartWakeWordService(settings);
    }

    public void ShowVisitorPanel(bool playGreeting)
    {
        if (_mainViewModel is null)
        {
            return;
        }

        _visitorWindow ??= new VisitorWindow
        {
            DataContext = _mainViewModel
        };
        _visitorWindow.WindowState = WindowState.Maximized;
        _visitorWindow.Show();
        _visitorWindow.Activate();
        _petWindow?.ForceTopMost();

        if (playGreeting)
        {
            var now = DateTimeOffset.UtcNow;
            if (now - _lastGreetingStartedAt >= GreetingCooldown)
            {
                _lastGreetingStartedAt = now;
                _ = _mainViewModel.PlayGreetingCommand.ExecuteAsync(null);
            }
        }
    }

    public void ShowAdminWindow()
    {
        if (_mainViewModel is null)
        {
            return;
        }

        if (_visitorWindow?.IsVisible == true)
        {
            _visitorWindow.Hide();
        }

        _adminWindow ??= new MainWindow
        {
            DataContext = _mainViewModel
        };
        MainWindow = _adminWindow;
        _adminWindow.Show();
        _adminWindow.WindowState = WindowState.Normal;
        _adminWindow.Activate();
        _adminWindow.Focus();
        _petWindow?.ForceTopMost();
    }

    public void RequestAdminLogin(Window? owner)
    {
        if (_mainViewModel is null)
        {
            return;
        }

        var login = new AdminLoginWindow(_mainViewModel)
        {
            Topmost = true,
            ShowInTaskbar = false
        };
        if (owner is not null && owner.IsVisible)
        {
            login.Owner = owner;
        }

        if (login.ShowDialog() == true)
        {
            ShowAdminWindow();
        }
    }

    public void ExitApplication()
    {
        Shutdown();
    }

    private void CreateTrayIcon()
    {
        _notifyIcon?.Dispose();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开问答", null, (_, _) => Dispatcher.Invoke(() => ShowVisitorPanel(playGreeting: true)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = TrimNotifyText(_mainViewModel?.AssistantName ?? Models.AppSettings.DefaultAssistantName),
            Icon = LoadTrayIcon(),
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => Dispatcher.Invoke(() => ShowVisitorPanel(playGreeting: true));
    }

    private static string TrimNotifyText(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? Models.AppSettings.DefaultAssistantName
            : value.Length <= 63 ? value : value[..63];

    private void RegisterAdminHotKey()
    {
        if (_petWindow is null)
        {
            return;
        }

        _hotKeyWindowHandle = new WindowInteropHelper(_petWindow).Handle;
        if (_hotKeyWindowHandle == IntPtr.Zero)
        {
            return;
        }

        _hotKeySource = HwndSource.FromHwnd(_hotKeyWindowHandle);
        _hotKeySource?.AddHook(WndProc);
        RegisterHotKey(_hotKeyWindowHandle, AdminHotKeyId, ModControl | ModNoRepeat, (uint)KeyInterop.VirtualKeyFromKey(Key.D1));
        RegisterHotKey(_hotKeyWindowHandle, AdminNumpadHotKeyId, ModControl | ModNoRepeat, (uint)KeyInterop.VirtualKeyFromKey(Key.NumPad1));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && (wParam.ToInt32() == AdminHotKeyId || wParam.ToInt32() == AdminNumpadHotKeyId))
        {
            handled = true;
            RequestAdminLogin(_petWindow);
        }

        return IntPtr.Zero;
    }

    private static Drawing.Icon LoadTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", "logo.ico");
        return File.Exists(iconPath)
            ? new Drawing.Icon(iconPath)
            : Drawing.SystemIcons.Application;
    }

    private void RestartWakeWordService(Models.AppSettings settings)
    {
        _wakeWordService?.Dispose();
        _wakeWordService = new WakeWordService();
        if (_wakeWordService.TryStart(settings.WakeWords))
        {
            _wakeWordService.WakeWordDetected += (_, wakeText) => Dispatcher.Invoke(() => _ = HandleWakeWordDetectedAsync(wakeText));
        }
    }

    private async Task HandleWakeWordDetectedAsync(string wakeText)
    {
        if (_mainViewModel is null)
        {
            return;
        }

        _wakeWordService?.Dispose();
        _wakeWordService = null;
        ShowVisitorPanel(playGreeting: false);
        try
        {
            if (!string.IsNullOrWhiteSpace(wakeText))
            {
                _mainViewModel.Question = wakeText.Trim();
                if (_mainViewModel.AskCommand.CanExecute(null))
                {
                    await _mainViewModel.AskCommand.ExecuteAsync(null);
                }
            }
        }
        finally
        {
            if (_currentSettings is not null)
            {
                RestartWakeWordService(_currentSettings);
            }
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _wakeWordService?.Dispose();
        _notifyIcon?.Dispose();
        if (_hotKeyWindowHandle != IntPtr.Zero)
        {
            UnregisterHotKey(_hotKeyWindowHandle, AdminHotKeyId);
            UnregisterHotKey(_hotKeyWindowHandle, AdminNumpadHotKeyId);
        }

        _hotKeySource?.RemoveHook(WndProc);
        if (_mainViewModel is not null)
        {
            await _mainViewModel.DisposeAsync();
        }

        base.OnExit(e);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
