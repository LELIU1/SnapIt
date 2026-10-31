using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace SnapIt.Capture
{
    public class NativeWindowInfo
    {
        public IntPtr Handle;
        public string Title;
        public Rect Bounds;   // 物理像素坐标
    }

    /// <summary>
    /// 枚举可吸附的顶层窗口（可见、非挂起、非工具窗），使用 DWM 真实边界（去除阴影）。
    /// </summary>
    public static class WindowEnumerator
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int DWMWA_CLOAKED = 14;
        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int maxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        private static extern int DwmGetWindowAttributeRect(IntPtr hwnd, int attr, out RECT value, int size);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        public static void HideFromAltTab(IntPtr hwnd)
        {
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW);
        }

        /// <summary>screenPhys：整个虚拟屏幕的物理像素矩形。</summary>
        public static List<NativeWindowInfo> GetWindows(Rect screenPhys, IntPtr excludeSelf)
        {
            var list = new List<NativeWindowInfo>();
            EnumWindows((h, l) =>
            {
                if (h == excludeSelf || !IsWindowVisible(h)) return true;

                int cloaked;
                if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0) return true;

                var sb = new StringBuilder(256);
                if (GetWindowText(h, sb, 256) == 0) return true;

                int ex = GetWindowLong(h, GWL_EXSTYLE);
                if ((ex & WS_EX_TOOLWINDOW) != 0) return true;

                RECT r;
                if (DwmGetWindowAttributeRect(h, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(RECT))) != 0)
                    GetWindowRect(h, out r);

                var rect = new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                if (rect.Width < 10 || rect.Height < 10) return true;

                var inter = Rect.Intersect(screenPhys, rect);
                if (inter.IsEmpty || inter.Width < 10 || inter.Height < 10) return true;

                list.Add(new NativeWindowInfo { Handle = h, Title = sb.ToString(), Bounds = rect });
                return true;
            }, IntPtr.Zero);
            return list;
        }
    }
}
