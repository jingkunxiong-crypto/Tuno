using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Tuno.PlaybackProbe;

internal static class WindowChromeBounds
{
    private const int WmGetMinMaxInfo=0x0024;
    private const int MonitorDefaultToNearest=2;

    public static void Attach(Window window)
    {
        var hwnd=new WindowInteropHelper(window).Handle;
        if(hwnd==IntPtr.Zero)return;
        if(HwndSource.FromHwnd(hwnd) is { } source)source.AddHook(WndProc);
    }

    private static IntPtr WndProc(IntPtr hwnd,int msg,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(msg!=WmGetMinMaxInfo)return IntPtr.Zero;
        var monitor=MonitorFromWindow(hwnd,MonitorDefaultToNearest);
        var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
        if(monitor==IntPtr.Zero || !GetMonitorInfo(monitor,ref info))return IntPtr.Zero;

        var limits=Marshal.PtrToStructure<MinMaxInfo>(lParam);
        limits.MaxPosition.X=info.Work.Left-info.Monitor.Left;
        limits.MaxPosition.Y=info.Work.Top-info.Monitor.Top;
        limits.MaxSize.X=info.Work.Right-info.Work.Left;
        limits.MaxSize.Y=info.Work.Bottom-info.Work.Top;
        Marshal.StructureToPtr(limits,lParam,false);
        handled=true;
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X,Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left,Top,Right,Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaxSize;
        public Point MaxPosition;
        public Point MinTrackSize;
        public Point MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public int Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd,int flags);

    [DllImport("user32.dll",CharSet=CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
}
