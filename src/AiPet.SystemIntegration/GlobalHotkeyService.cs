using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace AiPet.SystemIntegration;

[Flags]
public enum GlobalHotkeyModifiers : uint
{
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008,
    NoRepeat = 0x4000,
}

public sealed record GlobalHotkeyGesture(GlobalHotkeyModifiers Modifiers, int VirtualKey, string DisplayText)
{
    private static readonly IReadOnlyDictionary<string, Key> KeyAliases =
        new Dictionary<string, Key>(StringComparer.OrdinalIgnoreCase)
        {
            ["space"] = Key.Space,
            ["pageup"] = Key.PageUp,
            ["pagedown"] = Key.PageDown,
            ["home"] = Key.Home,
            ["end"] = Key.End,
            ["insert"] = Key.Insert,
            ["left"] = Key.Left,
            ["right"] = Key.Right,
            ["up"] = Key.Up,
            ["down"] = Key.Down,
        };

    public static bool TryParse(string? text, out GlobalHotkeyGesture? gesture, out string error)
    {
        gesture = null;
        error = string.Empty;
        var parts = (text ?? string.Empty).Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts.Length > 5 || parts.Any(string.IsNullOrWhiteSpace))
        {
            error = "请使用 Ctrl+Alt+Space 这样的组合。";
            return false;
        }

        var modifiers = (GlobalHotkeyModifiers)0;
        Key? key = null;
        var modifierTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in parts)
        {
            var normalized = part.ToLowerInvariant();
            GlobalHotkeyModifiers? modifier = normalized switch
            {
                "ctrl" or "control" => GlobalHotkeyModifiers.Control,
                "alt" => GlobalHotkeyModifiers.Alt,
                "shift" => GlobalHotkeyModifiers.Shift,
                "win" or "windows" => GlobalHotkeyModifiers.Windows,
                _ => null,
            };
            if (modifier is { } value)
            {
                if (!modifierTokens.Add(value.ToString()))
                {
                    error = "组合键中不能重复修饰键。";
                    return false;
                }
                modifiers |= value;
                continue;
            }
            if (key is not null || !TryParseKey(part, out var parsed))
            {
                error = "组合键只能包含一个普通键（A-Z、0-9、F1-F24、Space 或导航键）。";
                return false;
            }
            key = parsed;
        }

        if (modifiers == 0 || key is null)
        {
            error = "组合键至少需要一个修饰键和一个普通键。";
            return false;
        }
        if (key is Key.Escape or Key.Tab or Key.Enter or Key.Return or Key.Back or Key.Delete)
        {
            error = "Esc、Tab、Enter、Backspace 和 Delete 不能用作全局快捷键。";
            return false;
        }

        var virtualKey = KeyInterop.VirtualKeyFromKey(key.Value);
        if (virtualKey <= 0)
        {
            error = "无法识别该普通键。";
            return false;
        }
        var display = string.Join("+", new[]
        {
            modifiers.HasFlag(GlobalHotkeyModifiers.Control) ? "Ctrl" : null,
            modifiers.HasFlag(GlobalHotkeyModifiers.Alt) ? "Alt" : null,
            modifiers.HasFlag(GlobalHotkeyModifiers.Shift) ? "Shift" : null,
            modifiers.HasFlag(GlobalHotkeyModifiers.Windows) ? "Win" : null,
            DisplayKey(key.Value),
        }.Where(value => value is not null));
        gesture = new GlobalHotkeyGesture(modifiers, virtualKey, display);
        return true;
    }

    public static bool TryParsePair(string search, string quickTodo, out GlobalHotkeyGesture? searchGesture,
        out GlobalHotkeyGesture? todoGesture, out string error)
    {
        todoGesture = null;
        if (!TryParse(search, out searchGesture, out error)) return false;
        if (!TryParse(quickTodo, out todoGesture, out error)) return false;
        if (searchGesture!.Modifiers == todoGesture!.Modifiers && searchGesture.VirtualKey == todoGesture.VirtualKey)
        {
            error = "搜索和快速待办不能使用同一个组合键。";
            return false;
        }
        return true;
    }

    private static bool TryParseKey(string text, out Key key)
    {
        if (KeyAliases.TryGetValue(text.Replace(" ", string.Empty), out key)) return true;
        if (text.Length == 1 && char.IsLetter(text[0])) return Enum.TryParse(text.ToUpperInvariant(), out key);
        if (text.Length == 1 && char.IsDigit(text[0])) return Enum.TryParse("D" + text, out key);
        if (text.Length is 2 or 3 && text[0] is 'f' or 'F' && int.TryParse(text[1..], out var function) && function is >= 1 and <= 24)
            return Enum.TryParse("F" + function, out key);
        key = Key.None;
        return false;
    }

    private static string DisplayKey(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
        Key.Space => "Space",
        _ => key.ToString(),
    };
}

public sealed record GlobalHotkeyDefinition(int Id, GlobalHotkeyGesture Gesture, Action Callback);
public sealed record GlobalHotkeyApplyResult(bool Success, string Message);

/// <summary>Registers fixed combinations through WM_HOTKEY. It never installs a keyboard hook.</summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _callbacks = new();
    private bool _disposed;

    public GlobalHotkeyService()
    {
        _source = new HwndSource(new HwndSourceParameters("AiPet.GlobalHotkeys")
        {
            ParentWindow = new IntPtr(-3),
            WindowStyle = 0,
            Width = 0,
            Height = 0,
        });
        _source.AddHook(WndProc);
    }

    public GlobalHotkeyApplyResult Apply(IReadOnlyList<GlobalHotkeyDefinition> definitions)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        UnregisterAll();
        foreach (var definition in definitions)
        {
            var modifiers = definition.Gesture.Modifiers | GlobalHotkeyModifiers.NoRepeat;
            if (!RegisterHotKey(_source.Handle, definition.Id, (uint)modifiers, (uint)definition.Gesture.VirtualKey))
            {
                UnregisterAll();
                return new(false, $"快捷键 {definition.Gesture.DisplayText} 已被其他程序占用或被系统拒绝。已保持全部全局快捷键停用。");
            }
            _callbacks.Add(definition.Id, definition.Callback);
        }
        return new(true, definitions.Count == 0 ? "全局快捷键已停用。" : "全局快捷键已启用。应用不会记录其他按键。");
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && _callbacks.TryGetValue(wParam.ToInt32(), out var callback))
        {
            handled = true;
            try { callback(); }
            catch { /* Never propagate application exceptions across the native window procedure. */ }
        }
        return IntPtr.Zero;
    }

    private void UnregisterAll()
    {
        foreach (var id in _callbacks.Keys.ToArray()) UnregisterHotKey(_source.Handle, id);
        _callbacks.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
        _disposed = true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
