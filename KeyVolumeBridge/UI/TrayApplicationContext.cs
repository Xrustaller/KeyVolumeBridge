using System.Diagnostics;
using KeyVolumeBridge.Config;
using KeyVolumeBridge.Input;
using KeyVolumeBridge.Processing;
using KeyVolumeBridge.Reaper;
using Microsoft.Win32;

namespace KeyVolumeBridge.UI;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string TrayIconRelativePath = "Assets\\tray.ico";
    private const string AppIconRelativePath = "Assets\\app.ico";
    private const string StartupRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "KeyVolumeBridge";

    private readonly AppConfig _config;
    private readonly ToolStripMenuItem _configItem;
    private readonly ToolStripMenuItem _lastEventItem;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _oscItem;
    private readonly bool _ownsTrayIcon;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _statusItem;

    private readonly SynchronizationContext _syncContext;
    private readonly Icon _trayIcon;
    private MediaKeyHook? _hook;
    private MuteClickProcessor? _muteClickProcessor;
    private ReaperApi? _reaperApi;

    public TrayApplicationContext()
    {
        _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
        _config = AppConfig.Load();

        _statusItem = new ToolStripMenuItem("Статус: инициализация...") { Enabled = false };
        _oscItem = new ToolStripMenuItem("OSC: -") { Enabled = false };
        _configItem = new ToolStripMenuItem("Открыть папку конфига");
        _startupItem = new ToolStripMenuItem("Запускать при старте Windows") { CheckOnClick = false };
        _lastEventItem = new ToolStripMenuItem("Последнее событие: -") { Enabled = false };

        _configItem.Click += (_, _) => OpenConfigFolder();
        _startupItem.Click += (_, _) => ToggleStartup();

        ToolStripMenuItem exitItem = new("Выход");
        exitItem.Click += (_, _) => ExitThread();

        ContextMenuStrip menu = new();
        menu.Items.Add(_statusItem);
        menu.Items.Add(_oscItem);
        menu.Items.Add(_lastEventItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_configItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        (_trayIcon, _ownsTrayIcon) = LoadTrayIcon();
        _notifyIcon = new NotifyIcon
        {
            Icon = _trayIcon,
            Text = "KeyVolumeBridge",
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => ShowSummaryBalloon();

        InitializeRuntime();
    }

    private void InitializeRuntime()
    {
        RefreshStartupMenuState();

        try
        {
            _reaperApi = new ReaperApi(_config.Osc.Host, _config.Osc.Port, LogFromAnyThread);
            _muteClickProcessor = new MuteClickProcessor(
                _reaperApi,
                _config.Commands.Mute.SingleClick,
                _config.Commands.Mute.DoubleClickExtra,
                _config.Commands.Mute.TripleClickExtra,
                _config.Click.MuteWindowMs,
                LogFromAnyThread);
            _hook = new MediaKeyHook(OnMediaKeyPressed, LogFromAnyThread);

            _oscItem.Text = $"OSC: {_reaperApi.OscHost}:{_reaperApi.OscPort}";
            _statusItem.Text = "Статус: работает";
            UpdateLastEvent("Приложение запущено, перехват клавиш активен.");

            _notifyIcon.ShowBalloonTip(
                2000,
                "KeyVolumeBridge",
                "Работает в фоне. Управление через иконку в трее.",
                ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _statusItem.Text = "Статус: ошибка инициализации";
            UpdateLastEvent($"Ошибка запуска: {ex.Message}");
        }
    }

    private void OnMediaKeyPressed(MediaKey key)
    {
        if (_reaperApi == null) return;

        if (key == MediaKey.VolumeMute)
        {
            _muteClickProcessor?.RegisterClick();
            UpdateLastEvent("Key: VolumeMute");
            return;
        }

        int commandId = key switch
        {
            MediaKey.VolumeUp => _config.Commands.VolumeUp,
            MediaKey.VolumeDown => _config.Commands.VolumeDown,
            _ => 0
        };

        if (commandId <= 0)
        {
            UpdateLastEvent($"Key: {key}, команда не задана");
            return;
        }

        UpdateLastEvent($"Key: {key}, REAPER Command ID: {commandId}");
        _reaperApi.Main_OnCommand(commandId);
    }

    private void OpenConfigFolder()
    {
        try
        {
            string configDirectory = Path.GetDirectoryName(AppConfig.ConfigPath) ?? AppContext.BaseDirectory;
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = configDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            UpdateLastEvent($"Не удалось открыть папку конфига: {ex.Message}");
        }
    }

    private void ShowSummaryBalloon()
    {
        _notifyIcon.ShowBalloonTip(
            1500,
            "KeyVolumeBridge",
            $"{_statusItem.Text}. {_oscItem.Text}.",
            ToolTipIcon.None);
    }

    private void LogFromAnyThread(string message)
    {
        _syncContext.Post(_ => UpdateLastEvent(message), null);
    }

    private void UpdateLastEvent(string message)
    {
        _lastEventItem.Text = $"Последнее событие: {message}";
    }

    private void ToggleStartup()
    {
        try
        {
            bool enable = !IsStartupEnabled();
            SetStartupEnabled(enable);
            RefreshStartupMenuState();
            UpdateLastEvent(enable ? "Автозапуск включен." : "Автозапуск отключен.");
        }
        catch (Exception ex)
        {
            UpdateLastEvent($"Не удалось изменить автозапуск: {ex.Message}");
        }
    }

    private void RefreshStartupMenuState()
    {
        bool enabled = false;
        try
        {
            enabled = IsStartupEnabled();
        }
        catch
        {
            // Оставляем disabled-состояние по умолчанию, чтобы не падать на чтении реестра.
        }

        _startupItem.Checked = enabled;
        _startupItem.Text = enabled
            ? "Запускать при старте Windows: включено"
            : "Запускать при старте Windows: выключено";
    }

    private static bool IsStartupEnabled()
    {
        using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(StartupRunKeyPath, false);
        string? value = runKey?.GetValue(StartupValueName) as string;
        if (string.IsNullOrWhiteSpace(value)) return false;

        string expected = BuildStartupCommand();
        return string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static void SetStartupEnabled(bool enabled)
    {
        using RegistryKey? runKey = Registry.CurrentUser.CreateSubKey(StartupRunKeyPath, true);
        if (runKey == null) throw new InvalidOperationException("Не удалось открыть ветку реестра автозапуска.");

        if (!enabled)
        {
            runKey.DeleteValue(StartupValueName, false);
            return;
        }

        runKey.SetValue(StartupValueName, BuildStartupCommand(), RegistryValueKind.String);
    }

    private static string BuildStartupCommand()
    {
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath)) throw new InvalidOperationException("Не удалось определить путь к исполняемому файлу.");

        return $"\"{exePath}\"";
    }

    private static (Icon Icon, bool OwnsIcon) LoadTrayIcon()
    {
        string trayIconPath = Path.Combine(AppContext.BaseDirectory, TrayIconRelativePath);
        if (File.Exists(trayIconPath)) return (new Icon(trayIconPath), true);

        string appIconPath = Path.Combine(AppContext.BaseDirectory, AppIconRelativePath);
        if (File.Exists(appIconPath)) return (new Icon(appIconPath), true);

        return (SystemIcons.Application, false);
    }

    protected override void ExitThreadCore()
    {
        _hook?.Dispose();
        _hook = null;

        _muteClickProcessor?.Dispose();
        _muteClickProcessor = null;

        _reaperApi?.Dispose();
        _reaperApi = null;

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        if (_ownsTrayIcon) _trayIcon.Dispose();

        base.ExitThreadCore();
    }
}