using System.Runtime.InteropServices;
using Temhalo.Infrastructure.Windows.Settings;

namespace Temhalo.Infrastructure.Windows.Hotkeys;

/// <summary>UI-thread owned HWND subclass. MOD_NOREPEAT prevents repeated commands while held.</summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const uint WmHotkey = 0x0312, WmNcDestroy = 0x0082;
    private const int BaseId = 0x5A10;
    private readonly nint _window;
    private readonly SubclassProc _callback;
    private readonly Dictionary<int, HotkeyAction> _registered = new();
    private HotkeySettings _active = new() { Enabled = false };
    private bool _disposed;
    public event Action<HotkeyAction>? Invoked;

    public GlobalHotkeyService(nint window)
    {
        _window = window;
        _callback = WindowProc;
        if (!SetWindowSubclass(window, _callback, BaseId, 0))
            throw new InvalidOperationException("Les raccourcis globaux ne peuvent pas être reliés à cette fenêtre.");
    }

    public void Apply(HotkeySettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        settings.Validate();
        var previous = _active;
        UnregisterAll();
        try { Register(settings); _active = settings; }
        catch (InvalidOperationException error)
        {
            UnregisterAll();
            try { Register(previous); }
            catch (InvalidOperationException)
            { UnregisterAll(); _active = previous with { Enabled = false }; throw new InvalidOperationException(error.Message + " Les anciens raccourcis n'ont pas pu être restaurés ; ils sont désactivés."); }
            throw;
        }
    }

    private void Register(HotkeySettings settings)
    {
        if (!settings.Enabled) return;
        foreach (var (action, gesture) in settings.Gestures())
        {
            int id = BaseId + (int)action;
            if (!RegisterHotKey(_window, id, gesture.Modifiers | 0x4000, gesture.VirtualKey))
                throw new InvalidOperationException($"Le raccourci « {Label(action)} » est indisponible (code Windows {Marshal.GetLastWin32Error()}). Choisissez une autre combinaison.");
            _registered.Add(id, action);
        }
    }
    public static string Label(HotkeyAction action) => action switch
    { HotkeyAction.Protection => "Preset de protection", HotkeyAction.Zero => "Délai zéro", HotkeyAction.Increase => "+10 secondes", _ => "−10 secondes" };

    private nint WindowProc(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == WmHotkey && _registered.TryGetValue((int)wParam, out var action))
        {
            // No exception may cross the unmanaged window procedure boundary.
            try { Invoked?.Invoke(action); } catch (Exception) { }
            return 0;
        }
        if (message == WmNcDestroy) Dispose();
        return DefSubclassProc(window, message, wParam, lParam);
    }
    private void UnregisterAll()
    { foreach (int id in _registered.Keys) _ = UnregisterHotKey(_window, id); _registered.Clear(); }
    public void Dispose()
    {
        if (_disposed) return;
        UnregisterAll();
        _ = RemoveWindowSubclass(_window, _callback, BaseId);
        _disposed = true;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProc(nint hwnd, uint msg, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint virtualKey);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint hwnd, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint hwnd, uint msg, nuint wParam, nint lParam);
}
