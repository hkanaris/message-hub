using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MessageHub;

static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    struct FlashInfo
    {
        public uint Size;
        public IntPtr Hwnd;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    const uint FlashTray = 0x2;
    const uint FlashUntilForeground = 0xC;

    [DllImport("user32.dll")]
    static extern bool FlashWindowEx(ref FlashInfo info);

    // Flashes the taskbar button until the window is brought to the front.
    public static void FlashTaskbar(Window window)
    {
        var info = new FlashInfo
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            Hwnd = new WindowInteropHelper(window).Handle,
            Flags = FlashTray | FlashUntilForeground,
        };
        FlashWindowEx(ref info);
    }
}
