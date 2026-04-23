using System.Runtime.InteropServices;
using KeyVolumeBridge.Interop;

namespace KeyVolumeBridge.Input;

internal sealed class MediaKeyHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;

    private const int VkVolumeMute = 0xAD;
    private const int VkVolumeDown = 0xAE;
    private const int VkVolumeUp = 0xAF;
    private readonly Action<string>? _log;
    private readonly Action<MediaKey> _onMediaKey;

    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private IntPtr _hookHandle;

    public MediaKeyHook(Action<MediaKey> onMediaKey, Action<string>? log = null)
    {
        _onMediaKey = onMediaKey;
        _log = log;
        _proc = HookCallback;
        _hookHandle = NativeMethods.SetWindowsHookEx(WhKeyboardLl, _proc, IntPtr.Zero, 0);

        if (_hookHandle == IntPtr.Zero) throw new InvalidOperationException("Не удалось установить глобальный low-level keyboard hook.");
    }

    public void Dispose()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        GC.SuppressFinalize(this);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == WmKeyDown || wParam == WmSysKeyDown))
        {
            int vkCode = Marshal.ReadInt32(lParam);
            switch (vkCode)
            {
                case VkVolumeUp:
                    SafeInvoke(MediaKey.VolumeUp);
                    return 1; // Блокируем стандартную реакцию Windows.
                case VkVolumeDown:
                    SafeInvoke(MediaKey.VolumeDown);
                    return 1;
                case VkVolumeMute:
                    SafeInvoke(MediaKey.VolumeMute);
                    return 1;
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private void SafeInvoke(MediaKey key)
    {
        try
        {
            _onMediaKey(key);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Ошибка обработки клавиши {key}: {ex.Message}");
        }
    }
}