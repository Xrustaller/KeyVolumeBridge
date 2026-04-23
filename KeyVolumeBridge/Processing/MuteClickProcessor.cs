using KeyVolumeBridge.Reaper;
using Timer = System.Threading.Timer;

namespace KeyVolumeBridge.Processing;

internal sealed class MuteClickProcessor : IDisposable
{
    private readonly int _clickWindowMs;
    private readonly int _doubleClickExtraCommandId;
    private readonly Action<string>? _log;
    private readonly ReaperApi _reaperApi;
    private readonly int _singleClickCommandId;
    private readonly object _sync = new();
    private readonly int _tripleClickExtraCommandId;
    private int _clickCount;
    private bool _disposed;

    private Timer? _timer;

    public MuteClickProcessor(
        ReaperApi reaperApi,
        int singleClickCommandId,
        int doubleClickExtraCommandId,
        int tripleClickExtraCommandId,
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
            SendCommand(_singleClickCommandId, "Mute x3 base");
            SendCommand(_tripleClickExtraCommandId, "Mute x3 extra");
            return;
        }

        if (resolvedClicks == 2)
        {
            SendCommand(_singleClickCommandId, "Mute x2 base");
            SendCommand(_doubleClickExtraCommandId, "Mute x2 extra");
            return;
        }

        if (resolvedClicks == 1) SendCommand(_singleClickCommandId, "Mute x1");
    }

    private void SendCommand(int commandId, string source)
    {
        if (commandId <= 0)
        {
            _log?.Invoke($"{source}: extra command не задан (ID={commandId})");
            return;
        }

        _log?.Invoke($"{source}, REAPER Command ID: {commandId}");
        _reaperApi.Main_OnCommand(commandId);
    }
}