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

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    public static void Bounds(IntPtr hWnd, out int left, out int top, out int right, out int bottom)
    {
        SetProcessDPIAware();
        GetWindowRect(hWnd, out var rect);
        (left, top, right, bottom) = (rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    private delegate bool EnumProc(IntPtr hWnd, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc proc, IntPtr data);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    /// <summary>Visible top-level windows of a process, straight from Win32.</summary>
    public static List<IntPtr> Windows(int pid)
    {
        var found = new List<IntPtr>();
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out var owner);
            if (owner == pid && IsWindowVisible(hWnd)) found.Add(hWnd);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo { public int Size, Flags; public IntPtr Cursor; public int X, Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorInfo(ref CursorInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);

    /// <summary>Whether the cursor on screen right now is the given system cursor (e.g. 32648 = IDC_NO).</summary>
    public static bool CursorIs(int systemCursor)
    {
        var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        return GetCursorInfo(ref info) && info.Cursor == LoadCursor(IntPtr.Zero, new IntPtr(systemCursor));
    }

    /// <summary>PW_RENDERFULLCONTENT: also captures WPF and layered (transparent) windows.</summary>
    public static void Print(IntPtr hWnd, IntPtr hdc) => PrintWindow(hWnd, hdc, 2);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
}
