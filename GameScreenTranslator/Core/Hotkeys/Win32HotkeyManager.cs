using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace GameScreenTranslator.Core.Hotkeys;

/// <summary>
/// Thin wrapper over Win32 RegisterHotKey + message pump (WM_HOTKEY).
/// </summary>
public sealed class Win32HotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    private int _nextId = 1;
    private bool _disposed;

    public event EventHandler<int>? HotkeyPressed;

    public int Register(uint modifiers, uint virtualKey)
    {
        ThrowIfDisposed();

        var id = Interlocked.Increment(ref _nextId);
        if (!RegisterHotKey(IntPtr.Zero, id, modifiers, virtualKey))
        {
            var err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"RegisterHotKey failed. Win32Error={err} (mod={modifiers}, vk={virtualKey})");
        }

        return id;
    }

    public void Unregister(int id)
    {
        if (_disposed) return;
        UnregisterHotKey(IntPtr.Zero, id);
    }

    public void RunMessageLoop(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        while (!cancellationToken.IsCancellationRequested && GetMessage(out var msg, IntPtr.Zero, 0, 0) != 0)
        {
            if (msg.message == WM_HOTKEY)
            {
                var id = (int)msg.wParam;
                HotkeyPressed?.Invoke(this, id);
            }

            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(Win32HotkeyManager));
    }

    public void Dispose()
    {
        _disposed = true;
    }

    // Win32

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage([In] ref MSG lpmsg);
}

/// <summary>
/// Win32 MOD_* flags for RegisterHotKey.
/// </summary>
public static class HotkeyModifiers
{
    public const uint Alt = 0x0001; // MOD_ALT
    public const uint Control = 0x0002; // MOD_CONTROL
    public const uint Shift = 0x0004; // MOD_SHIFT
    public const uint Win = 0x0008; // MOD_WIN
}