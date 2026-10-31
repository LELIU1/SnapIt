using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SnapIt.Services
{
    /// <summary>
    /// 全局热键服务：默认 Alt+A，被占用时回退 Ctrl+Alt+A / Ctrl+Shift+A；不接管 PrintScreen。
    /// 基于 WinForms NativeWindow 消息循环，回调在 UI 线程触发。
    /// </summary>
    public class HotkeyService : NativeWindow, IDisposable
    {
        public event Action HotkeyPressed;

        private const uint MOD_ALT = 0x1;
        private const uint MOD_CONTROL = 0x2;
        private const uint MOD_SHIFT = 0x4;
        private const int WM_HOTKEY = 0x0312;
        private const uint VK_A = 0x41;

        internal struct HotkeyCandidate
        {
            public readonly string Name;
            public readonly uint Modifiers;
            public readonly uint VirtualKey;

            public HotkeyCandidate(string name, uint modifiers, uint virtualKey)
            {
                Name = name;
                Modifiers = modifiers;
                VirtualKey = virtualKey;
            }
        }

        internal static readonly HotkeyCandidate[] Candidates =
        {
            new HotkeyCandidate("Alt+A", MOD_ALT, VK_A),
            new HotkeyCandidate("Ctrl+Alt+A", MOD_CONTROL | MOD_ALT, VK_A),
            new HotkeyCandidate("Ctrl+Shift+A", MOD_CONTROL | MOD_SHIFT, VK_A)
        };

        private int _id = 1;
        private bool _registered;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modKeys, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public HotkeyService()
        {
            CreateHandle(new CreateParams());
        }

        /// <summary>注册默认热键，返回实际注册成功的组合名；全部失败返回 null。</summary>
        public string RegisterDefault()
        {
            foreach (var candidate in Candidates)
            {
                if (!RegisterHotKey(Handle, _id, candidate.Modifiers, candidate.VirtualKey)) continue;
                _registered = true;
                return candidate.Name;
            }
            return null;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && (int)m.WParam == _id)
                HotkeyPressed?.Invoke();
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (_registered) UnregisterHotKey(Handle, _id);
            DestroyHandle();
            GC.SuppressFinalize(this);
        }
    }
}
