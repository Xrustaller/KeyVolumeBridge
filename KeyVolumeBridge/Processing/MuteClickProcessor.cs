using KeyVolumeBridge.Reaper;
using Timer = System.Threading.Timer;

namespace KeyVolumeBridge.Processing;

internal sealed class MuteClickProcessor : IDisposable
{
    private readonly int _clickWindowMs;
    private readonly string _doubleClickExtraCommandId;
    private readonly Action<string>? _log;
    private readonly ReaperApi _reaperApi;
    private readonly string _singleClickCommandId;
    private readonly object _sync = new();
    private readonly string _tripleClickExtraCommandId;
    private int _clickCount;
    private bool _disposed;

    private Timer? _timer;

    public MuteClickProcessor(
        ReaperApi reaperApi,
        string singleClickCommandId,
        string doubleClickExtraCommandId,
        string tripleClickExtraCommandId,
        int clickWindowMs,
        Action<string>? log = null)
    {
        _reaperApi = reaperApi;
        _log = log;
        _singleClickCommandId = singleClickCommandId;
        _doubleClickExtraCommandId = doubleClickExtraCommandId;
        _tripleClickExtraCommandId = tripleClickExtraCommandId;
        _clickWindowMs = clickWindowMs < 100 ? 325 : clickWindowMs;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;

            _disposed = true;
            _timer?.Dispose();
            _timer = null;
            _clickCount = 0;
        }

        GC.SuppressFinalize(this);
    }

    public void RegisterClick()
    {
        lock (_sync)
        {
            if (_disposed) return;

            _clickCount = Math.Min(_clickCount + 1, 3);

            if (_timer == null)
                _timer = new Timer(OnClickWindowElapsed, null, _clickWindowMs, Timeout.Infinite);
            else
                _timer.Change(_clickWindowMs, Timeout.Infinite);
        }
    }

    private void OnClickWindowElapsed(object? _)
    {
        int resolvedClicks;

        lock (_sync)
        {
            if (_disposed) return;

            resolvedClicks = _clickCount;
            _clickCount = 0;
        }

        if (resolvedClicks >= 3)
        {
            SendCommand(_tripleClickExtraCommandId, "Mute x3");
            return;
        }

        if (resolvedClicks == 2)
        {
            SendCommand(_doubleClickExtraCommandId, "Mute x2");
            return;
        }

        if (resolvedClicks == 1) SendCommand(_singleClickCommandId, "Mute x1");
    }

    private void SendCommand(string? commandId, string source)
    {
        string normalizedCommandId = string.IsNullOrWhiteSpace(commandId) ? string.Empty : commandId.Trim();
        if (string.IsNullOrEmpty(normalizedCommandId))
        {
            _log?.Invoke($"{source}: команда не задана");
            return;
        }

        _log?.Invoke($"{source}, REAPER Command ID: {normalizedCommandId}");
        _reaperApi.Main_OnCommand(normalizedCommandId);
    }
}
