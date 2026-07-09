using System.Windows.Input;
using GlassTodo.Interop;
using GlassTodo.Views;

namespace GlassTodo.Services;

/// <summary>Global hotkey via RegisterHotKey on the main window's HWND.</summary>
public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0xA11C;

    private readonly MainWindow _win;

    public event Action? Pressed;

    public bool Registered { get; private set; }
    public string? LastError { get; private set; }

    public HotkeyService(MainWindow win)
    {
        _win = win;
        _win.HotkeyPressed += id =>
        {
            if (id == HotkeyId) Pressed?.Invoke();
        };
    }

    public bool Register(string chord)
    {
        Unregister();
        if (!TryParse(chord, out uint mods, out uint vk))
        {
            LastError = "无法识别的热键组合";
            return false;
        }
        Registered = NativeMethods.RegisterHotKey(_win.Hwnd, HotkeyId, mods | NativeMethods.MOD_NOREPEAT, vk);
        if (!Registered) LastError = "热键已被其他程序占用";
        return Registered;
    }

    public void Unregister()
    {
        if (!Registered) return;
        NativeMethods.UnregisterHotKey(_win.Hwnd, HotkeyId);
        Registered = false;
    }

    public void Dispose() => Unregister();

    /// <summary>Parses chords like "Alt+Q" / "Ctrl+Shift+Space" / "F9".</summary>
    public static bool TryParse(string chord, out uint mods, out uint vk)
    {
        mods = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(chord)) return false;

        Key key = Key.None;
        foreach (string raw in chord.Split('+'))
        {
            string part = raw.Trim();
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= NativeMethods.MOD_CONTROL; break;
                case "alt": mods |= NativeMethods.MOD_ALT; break;
                case "shift": mods |= NativeMethods.MOD_SHIFT; break;
                case "win": mods |= NativeMethods.MOD_WIN; break;
                default:
                    if (part.Length == 1 && char.IsDigit(part[0])) part = "D" + part;
                    if (!Enum.TryParse(part, ignoreCase: true, out key)) return false;
                    break;
            }
        }

        if (key == Key.None) return false;
        bool isFunctionKey = key is >= Key.F1 and <= Key.F24;
        if (mods == 0 && !isFunctionKey) return false; // bare letters would hijack normal typing

        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return vk != 0;
    }

    /// <summary>Builds a chord string from live key state (used by the settings recorder).</summary>
    public static string? ChordFromKeyEvent(KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None)
            return null; // modifier alone — keep waiting

        var parts = new List<string>();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

        bool isFunctionKey = key is >= Key.F1 and <= Key.F24;
        if (parts.Count == 0 && !isFunctionKey) return null;

        string name = key.ToString();
        if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1])) name = name[1..];
        parts.Add(name);
        return string.Join("+", parts);
    }
}
