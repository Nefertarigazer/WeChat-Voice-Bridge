using System.Runtime.InteropServices;

/// <summary>
/// A real left click at screen coordinates. MenuItem's UIA InvokePattern does not
/// raise Click reliably for popup items, and a real click also proves the whole
/// row (padding included) is hit-testable.
/// </summary>
internal static class Native
{
    private const uint LeftDown = 0x0002;
    private const uint LeftUp = 0x0004;

    public static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        Thread.Sleep(120);
        mouse_event(LeftDown, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        mouse_event(LeftUp, 0, 0, 0, UIntPtr.Zero);
    }

    public static void Focus(IntPtr hWnd) => SetForegroundWindow(hWnd);

    private const int Restore = 9;

    /// <summary>Un-minimises the window. A minimised WPF window reports a bounding
    /// rectangle around -32000, so any hover or capture would aim at nothing.</summary>
    public static void Show(IntPtr hWnd)
    {
        ShowWindow(hWnd, Restore);
        SetForegroundWindow(hWnd);
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int cmd);

    /// <summary>Parks the cursor. Two hops, so the window definitely sees a WM_MOUSEMOVE
    /// and can raise its normal hover/tooltip logic.</summary>
    public static void Hover(int x, int y)
    {
        SetCursorPos(x - 6, y - 6);
        Thread.Sleep(80);
        SetCursorPos(x, y);
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
}
