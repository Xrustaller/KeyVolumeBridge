using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace KeyVolumeBridge.Reaper;

internal sealed class ReaperApi : IDisposable
{
    private const string ReaperProcessName = "reaper";
    private const int ReconnectIntervalMs = 2000;
    private readonly Action<string>? _log;

    private readonly object _sendSync = new();
    private bool _connectErrorLogged;
    private bool _disposed;
    private DateTime _nextReconnectUtc = DateTime.MinValue;
    private bool _sendErrorLogged;
    private UdpClient? _udpClient;

    public ReaperApi(string oscHost, int oscPort, Action<string>? log = null)
    {
        _log = log;
        OscHost = string.IsNullOrWhiteSpace(oscHost) ? "127.0.0.1" : oscHost.Trim();
        OscPort = oscPort is > 0 and <= 65535 ? oscPort : 8000;
        lock (_sendSync)
        {
            TryConnectLocked(true);
        }
    }

    public string OscHost { get; }

    public int OscPort { get; }

    public void Dispose()
    {
        lock (_sendSync)
        {
            if (_disposed) return;

            _disposed = true;
            _udpClient?.Dispose();
            _udpClient = null;
        }

        GC.SuppressFinalize(this);
    }

    public void Main_OnCommand(string? commandId)
    {
        string normalizedCommandId = NormalizeCommandId(commandId);
        if (string.IsNullOrEmpty(normalizedCommandId)) return;

        if (!IsReaperRunning()) return;

        byte[] payload = BuildOscActionMessage(normalizedCommandId);
        lock (_sendSync)
        {
            if (_disposed) return;

            if (!EnsureConnectedLocked()) return;

            try
            {
                _udpClient!.Send(payload, payload.Length);
                _sendErrorLogged = false;
            }
            catch (Exception ex)
            {
                HandleSendFailureLocked(ex);
            }
        }
    }

    private bool EnsureConnectedLocked()
    {
        if (_udpClient != null) return true;

        return TryConnectLocked(false);
    }

    private bool TryConnectLocked(bool force)
    {
        if (_disposed) return false;

        if (!force && DateTime.UtcNow < _nextReconnectUtc) return _udpClient != null;

        _udpClient?.Dispose();
        _udpClient = null;

        UdpClient? client = null;
        try
        {
            client = new UdpClient();
            client.Connect(OscHost, OscPort);
            _udpClient = client;
            _nextReconnectUtc = DateTime.MinValue;
            _connectErrorLogged = false;
            return true;
        }
        catch (Exception ex)
        {
            client?.Dispose();
            _nextReconnectUtc = DateTime.UtcNow.AddMilliseconds(ReconnectIntervalMs);
            if (!_connectErrorLogged)
            {
                _connectErrorLogged = true;
                _log?.Invoke($"Не удалось подключить OSC endpoint {OscHost}:{OscPort}: {ex.Message}");
            }

            return false;
        }
    }

    private void HandleSendFailureLocked(Exception ex)
    {
        _udpClient?.Dispose();
        _udpClient = null;
        _nextReconnectUtc = DateTime.UtcNow.AddMilliseconds(ReconnectIntervalMs);

        if (_sendErrorLogged) return;

        _sendErrorLogged = true;
        _log?.Invoke($"Ошибка отправки OSC в REAPER. Запущено переподключение: {ex.Message}");
    }

    private static bool IsReaperRunning()
    {
        Process[] processes = Process.GetProcessesByName(ReaperProcessName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (Process process in processes) process.Dispose();
        }
    }

    private static byte[] BuildOscActionMessage(string commandId)
    {
        // Для REAPER action-триггера используем путь /action/<id> без аргументов.
        // По спецификации OSC даже без аргументов добавляем type tags строку ",".
        string address = $"/action/{commandId}";
        byte[] addressBytes = EncodeOscString(address);
        byte[] typeTagBytes = EncodeOscString(",");

        byte[] packet = new byte[addressBytes.Length + typeTagBytes.Length];
        Buffer.BlockCopy(addressBytes, 0, packet, 0, addressBytes.Length);
        Buffer.BlockCopy(typeTagBytes, 0, packet, addressBytes.Length, typeTagBytes.Length);
        return packet;
    }

    private static byte[] EncodeOscString(string value)
    {
        byte[] utf8Bytes = Encoding.UTF8.GetBytes(value);
        int withNull = utf8Bytes.Length + 1;
        int paddedLength = Align4(withNull);
        byte[] result = new byte[paddedLength];
        Buffer.BlockCopy(utf8Bytes, 0, result, 0, utf8Bytes.Length);
        return result;
    }

    private static int Align4(int value)
    {
        return (value + 3) & ~3;
    }

    private static string NormalizeCommandId(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
