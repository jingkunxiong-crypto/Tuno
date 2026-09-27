using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Tuno.PlaybackProbe;

internal static class DesktopAcrylic
{
    private const int ImmersiveDarkMode = 20;
    private const int WindowCornerPreference = 33;
    private const int SystemBackdropType = 38;
    private const int RoundedCorners = 2;
    private const int TransientWindow = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins(int left, int right, int top, int bottom)
    {
        public int Left = left;
        public int Right = right;
        public int Top = top;
        public int Bottom = bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int valueSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(nint window, ref Margins margins);

    public static bool TryApply(Window window)
    {
        if(!OperatingSystem.IsWindowsVersionAtLeast(10,0,22621))return false;
        var handle = new WindowInteropHelper(window).Handle;
        if(handle == 0)return false;
        try
        {
            var dark = 1;
            _ = DwmSetWindowAttribute(handle, ImmersiveDarkMode, ref dark, sizeof(int));
            var corners = RoundedCorners;
            _ = DwmSetWindowAttribute(handle, WindowCornerPreference, ref corners, sizeof(int));

            var acrylic = TransientWindow;
            if(DwmSetWindowAttribute(handle, SystemBackdropType, ref acrylic, sizeof(int)) < 0)return false;

            // WPF normally paints an opaque client area. Extend the DWM frame and
            // leave its composition target clear so the system material stays visible.
            var source = HwndSource.FromHwnd(handle);
            if(source?.CompositionTarget is null)return false;
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
            var fullWindow = new Margins(-1,-1,-1,-1);
            return DwmExtendFrameIntoClientArea(handle, ref fullWindow) >= 0;
        }
        catch(DllNotFoundException){return false;}
        catch(EntryPointNotFoundException){return false;}
    }
}
