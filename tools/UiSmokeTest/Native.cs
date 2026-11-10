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

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
}
