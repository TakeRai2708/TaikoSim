using System.Diagnostics;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace TakeTaikoSimulator.Input
{
    // Windows側の押下時刻を記録する係。Raylibへの入力配送自体は変更しない。
    // このクラスは演奏画面と同じスレッドで動くため、別スレッドでのキー処理は不要。
    // WH_GETMESSAGEフックで、Raylibがメッセージを処理する前に時刻を記録する。
    public sealed class WindowsKeyCapture : IDisposable
    {
        private const int WhGetMessage = 3;
        private const int PmRemove = 1;
        private const uint WmKeyDown = 0x0100;
        private const uint WmKeyUp = 0x0101;
        private const uint WmSysKeyDown = 0x0104;
        private const uint WmSysKeyUp = 0x0105;
        private const int MaxBufferedEvents = 4096;
        private const double MatchTimeoutMs = 500.0;

        private readonly List<WindowsKeyEvent> pendingEvents = new();
        private readonly List<WindowsKeyEvent> waitingPresses = new();
        private readonly HookProcedure callback;
        private nint hookHandle;
        private nint windowHandle;

        public bool CaptureEnabled { get; set; }
        public bool IsActive => hookHandle != 0;
        public string Status { get; private set; } = "未開始";
        public int DroppedEvents { get; private set; }

        public WindowsKeyCapture()
        {
            // ネイティブコードに渡した関数がGCで消えないように、フィールドで保持する。
            callback = OnMessage;
        }

        // Raylibが作ったWindowsウィンドウのスレッドだけを監視する。
        // Raylib.GetWindowHandle()はGLFWのポインタなので、Win32のHWNDとして使わない。
        public bool Start()
        {
            if (IsActive) return true;
            if (!OperatingSystem.IsWindows())
            {
                Status = "Windows以外のため利用不可";
                return false;
            }

            nint hwnd = GetActiveWindow();
            if (hwnd == 0) hwnd = FindWindowW(null, "TakeTaikoSimulator");
            if (hwnd == 0)
            {
                Status = "TaikoSimのウィンドウが見つかりません";
                return false;
            }
            uint windowThread = GetWindowThreadProcessId(hwnd, out uint processId);
            if (processId != (uint)Environment.ProcessId || windowThread != GetCurrentThreadId())
            {
                Status = "ウィンドウのスレッドを確認できません";
                return false;
            }

            windowHandle = hwnd;
            // 同じスレッドだけを監視するので、グローバルキーボードフックではない。
            hookHandle = SetWindowsHookExW(WhGetMessage, callback, 0, windowThread);
            if (hookHandle == 0)
            {
                Status = $"Windows入力監視を開始できません（Win32: {Marshal.GetLastWin32Error()}）";
                return false;
            }
            Status = "Windowsキー監視：動作中";
            return true;
        }

        public void Reset()
        {
            pendingEvents.Clear();
            waitingPresses.Clear();
            DroppedEvents = 0;
        }

        // Windowsから来たイベントを、そのフレームの診断ログに記録する。
        public void Drain(InputDiagnostics? diagnostics, long frame, double gameMs)
        {
            if (pendingEvents.Count == 0) return;
            foreach (WindowsKeyEvent item in pendingEvents)
            {
                diagnostics?.RecordWindowsEvent(frame, gameMs, item);
                if (item.IsDown && !item.IsRepeat && item.Key.HasValue)
                {
                    waitingPresses.Add(item);
                }
            }
            pendingEvents.Clear();

            // 一致しなかった古い押下イベントを蓄積しない。
            long now = Stopwatch.GetTimestamp();
            waitingPresses.RemoveAll(item =>
                Stopwatch.GetElapsedTime(item.CapturedAt, now).TotalMilliseconds > MatchTimeoutMs);
            if (waitingPresses.Count > MaxBufferedEvents)
                waitingPresses.RemoveRange(0, waitingPresses.Count - MaxBufferedEvents);
        }

        // Raylibが返した押下と、Windows側の同じキーの押下を1対1で照合する。
        // 照合できないキーもあり得るため、見つからなければnullを返す。
        public WindowsKeyEvent? Match(KeyboardKey key, long collectedAt)
        {
            for (int i = 0; i < waitingPresses.Count; i++)
            {
                WindowsKeyEvent item = waitingPresses[i];
                if (item.Key != key || item.CapturedAt > collectedAt) continue;
                waitingPresses.RemoveAt(i);
                return item;
            }
            return null;
        }

        private nint OnMessage(int code, nint wParam, nint lParam)
        {
            try
            {
                // PM_REMOVEのメッセージだけを見る。Raylib自身の処理は邪魔しない。
                if (CaptureEnabled && code >= 0 && wParam == PmRemove && lParam != 0)
                {
                    NativeMessage msg = Marshal.PtrToStructure<NativeMessage>(lParam);
                    if (msg.Hwnd == windowHandle &&
                        msg.Message is WmKeyDown or WmKeyUp or WmSysKeyDown or WmSysKeyUp)
                    {
                        bool down = msg.Message is WmKeyDown or WmSysKeyDown;
                        bool repeat = down && ((msg.LParam.ToInt64() & (1L << 30)) != 0);
                        uint vk = (uint)msg.WParam;
                        uint scan = (uint)((msg.LParam.ToInt64() >> 16) & 0xff);
                        bool extended = (msg.LParam.ToInt64() & (1L << 24)) != 0;
                        long captured = Stopwatch.GetTimestamp();

                        // MSG.timeはOSのメッセージ時刻（ミリ秒精度）。
                        // Stopwatchと同じ時間軸への変換は概算であり、物理スイッチの作動時刻ではない。
                        uint age = unchecked((uint)Environment.TickCount64 - msg.Time);
                        double queueAgeMs = age <= 60000 ? age : 0;
                        long osTime = captured - (long)Math.Round(queueAgeMs * Stopwatch.Frequency / 1000.0);
                        var item = new WindowsKeyEvent(vk, scan, MapKey(vk, scan, extended), down,
                            repeat, captured, osTime, queueAgeMs);
                        if (pendingEvents.Count < MaxBufferedEvents) pendingEvents.Add(item);
                        else DroppedEvents++;
                    }
                }
            }
            catch (Exception ex)
            {
                // ネイティブコールバックから例外を投げるとゲームが落ちるため、監視のみ停止する。
                CaptureEnabled = false;
                Status = $"Windows入力監視エラー：{ex.GetType().Name}";
            }
            return CallNextHookEx(hookHandle, code, wParam, lParam);
        }

        // まず英数字・ファンクションキーなど、確実に対応が分かるキーだけを照合する。
        // 日本語キーボードの記号キーなどは誤照合せず、VK番号のままCSVに保存する。
        private static KeyboardKey? MapKey(uint vk, uint scan, bool extended)
        {
            if (vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A)
                return (KeyboardKey)(int)vk;
            if (vk is >= 0x60 and <= 0x69) return (KeyboardKey)(int)(320 + vk - 0x60);
            if (vk is >= 0x70 and <= 0x7B) return (KeyboardKey)(int)(290 + vk - 0x70);
            return vk switch
            {
                0x20 => (KeyboardKey)32,    // Space
                0x1B => (KeyboardKey)256,   // Escape
                0x0D => (KeyboardKey)(extended ? 335 : 257), // Enter / テンキーEnter
                0x09 => (KeyboardKey)258,   // Tab
                0x08 => (KeyboardKey)259,   // Backspace
                0x2D => (KeyboardKey)260,   // Insert
                0x2E => (KeyboardKey)261,   // Delete
                0x27 => (KeyboardKey)262,   // 右矢印
                0x25 => (KeyboardKey)263,   // 左矢印
                0x28 => (KeyboardKey)264,   // 下矢印
                0x26 => (KeyboardKey)265,   // 上矢印
                0x21 => (KeyboardKey)266,   // PageUp
                0x22 => (KeyboardKey)267,   // PageDown
                0x24 => (KeyboardKey)268,   // Home
                0x23 => (KeyboardKey)269,   // End
                0xA0 => (KeyboardKey)340,   // 左Shift
                0xA1 => (KeyboardKey)344,   // 右Shift
                0x10 => (KeyboardKey)(scan == 0x36 ? 344 : 340),
                0xA2 => (KeyboardKey)341,   // 左Ctrl
                0xA3 => (KeyboardKey)345,   // 右Ctrl
                0x11 => (KeyboardKey)(extended ? 345 : 341),
                0xA4 => (KeyboardKey)342,   // 左Alt
                0xA5 => (KeyboardKey)346,   // 右Alt
                0x12 => (KeyboardKey)(extended ? 346 : 342),
                _ => null
            };
        }

        public void Dispose()
        {
            CaptureEnabled = false;
            if (hookHandle != 0)
            {
                UnhookWindowsHookEx(hookHandle);
                hookHandle = 0;
            }
            pendingEvents.Clear();
            waitingPresses.Clear();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMessage
        {
            public nint Hwnd;
            public uint Message;
            public nuint WParam;
            public nint LParam;
            public uint Time;
            public NativePoint Point;
            public uint Private;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate nint HookProcedure(int code, nint wParam, nint lParam);

        [DllImport("user32.dll", EntryPoint = "GetActiveWindow")]
        private static extern nint GetActiveWindow();

        [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)]
        private static extern nint FindWindowW(string? className, string windowName);

        [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
        private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

        [DllImport("kernel32.dll", EntryPoint = "GetCurrentThreadId")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
        private static extern nint SetWindowsHookExW(int idHook, HookProcedure procedure, nint module, uint threadId);

        [DllImport("user32.dll", EntryPoint = "CallNextHookEx")]
        private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

        [DllImport("user32.dll", EntryPoint = "UnhookWindowsHookEx")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(nint hook);
    }

    // Windowsメッセージ1件。時刻はStopwatchと同じ時間軸（Tick）に直して保持する。
    public readonly record struct WindowsKeyEvent(
        uint VirtualKey, uint ScanCode, KeyboardKey? Key, bool IsDown, bool IsRepeat,
        long CapturedAt, long OsEstimatedAt, double QueueAgeMs);
}
