using System;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Input;
using Avalonia.Threading;

namespace Prometheus.Client.Services;

public sealed class GlobalShelfHotkey : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int WmQuit = 0x0012;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const ushort VkControl = 0x11;
    private const ushort VkAlt = 0x12;
    private const ushort VkShift = 0x10;
    private const ushort VkC = 0x43;
    private const uint KeyUp = 0x0002;
    private readonly Action _captured;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _threadReady = new();
    private readonly uint _virtualKey;
    private readonly uint _modifiers;
    private readonly string _shortcutLabel;
    private uint _threadId;
    private volatile bool _disposed;

    public GlobalShelfHotkey(Key key, KeyModifiers modifiers, Action captured, Action<string> report)
    {
        var keyName = key.ToString();
        if (keyName.Length != 1 || keyName[0] is < 'A' or > 'Z' || !modifiers.HasFlag(KeyModifiers.Control) ||
            !modifiers.HasFlag(KeyModifiers.Alt))
            throw new ArgumentException("El atajo global requiere Ctrl+Alt y una letra.");
        _virtualKey = keyName[0];
        _modifiers = ModControl | ModAlt | ModNoRepeat |
            (modifiers.HasFlag(KeyModifiers.Shift) ? ModShift : 0);
        _shortcutLabel = "Ctrl+Alt+" + (modifiers.HasFlag(KeyModifiers.Shift) ? "Mayús+" : "") + key;
        _captured = captured;
        _thread = new Thread(() => Run(report)) { IsBackground = true, Name = "Prometheus link shelf hotkey" };
        _thread.Start();
    }

    private void Run(Action<string> report)
    {
        _threadId = GetCurrentThreadId();
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
        _threadReady.Set();
        if (_disposed) return;
        if (!RegisterHotKey(IntPtr.Zero, 1, _modifiers, _virtualKey))
        {
            var error = Marshal.GetLastWin32Error();
            Dispatcher.UIThread.Post(() => report(error == 1409 ? $"{_shortcutLabel} está ocupado por otra aplicación; elige otro atajo en Ajustes" : $"No se pudo activar {_shortcutLabel} (Windows {error})"));
            return;
        }
        if (_disposed) { UnregisterHotKey(IntPtr.Zero, 1); return; }
        try
        {
            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.message != WmHotkey) continue;
                for (var attempt = 0; attempt < 20 &&
                     (IsDown(VkControl) || IsDown(VkAlt) || IsDown(VkShift) || IsDown((int)_virtualKey)); attempt++)
                    Thread.Sleep(25);
                if (IsDown(VkControl) || IsDown(VkAlt) || IsDown(VkShift) || IsDown((int)_virtualKey))
                { Dispatcher.UIThread.Post(() => report("Suelta las teclas para capturar la selección")); continue; }
                var oldSequence = GetClipboardSequenceNumber();
                var inputs = new[] {
                    Keyboard(VkControl, 0), Keyboard(VkC, 0), Keyboard(VkC, KeyUp), Keyboard(VkControl, KeyUp)
                };
                if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>()) != inputs.Length) continue;
                for (var i = 0; i < 12 && GetClipboardSequenceNumber() == oldSequence; i++) Thread.Sleep(50);
                if (GetClipboardSequenceNumber() != oldSequence) Dispatcher.UIThread.Post(_captured);
                else Dispatcher.UIThread.Post(() => report("No hay una selección nueva para la Bandeja de enlaces"));
            }
        }
        finally { UnregisterHotKey(IntPtr.Zero, 1); }
    }

    private static NativeInput Keyboard(ushort key, uint flags) => new() { type = 1, wVk = key, dwFlags = flags };
    private static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _threadReady.Wait(1000);
        if (_threadId != 0) PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        if (Thread.CurrentThread != _thread) _thread.Join(1000);
        if (!_thread.IsAlive) _threadReady.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct NativeInput
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public ushort wVk;
        [FieldOffset(10)] public ushort wScan;
        [FieldOffset(12)] public uint dwFlags;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] private static extern int GetMessage(out NativeMessage message, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out NativeMessage message, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, NativeInput[] inputs, int size);
}
