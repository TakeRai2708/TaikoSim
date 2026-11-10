using System.Diagnostics;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace TakeTaikoSimulator.Diagnostics
{
    // キー入力が「どこまで届いているか」を記録する調べもの用の係（11/8）
    //
    //   Claude が PC を操作して TakeTaikoSimulator を録画しようとしたら、TaikøNauts には効いたキーが TakeTaikoSimulator には効かなかった。
    //   どこで止まっているか分からないので、3か所を全部ファイルに書き出す。
    //     ① Windows のメッセージ（WM_KEYDOWN など）が TakeTaikoSimulator のウィンドウに来ているか（WH_GETMESSAGE フック）
    //     ② Raylib（GLFW）がそのキーを「押された」と思っているか（IsKeyDown の変化）
    //     ③ ウィンドウが前に出ているか・フォーカスがあるか、フルスクリーンか など
    //
    //   Debug ビルドのときだけ動く（Release では何もしない）。
    //   書き出し先：exe の横の Logs/KeyProbe_日付_時刻.txt
    public static class KeyProbe
    {
        private const int WhGetMessage = 3;
        private const int PmRemove = 1;

        private static StreamWriter? writer;
        private static nint hook;
        private static HookProcedure? callback;   // GC に消されないように持っておく
        private static nint windowHandle;
        private static readonly Stopwatch clock = Stopwatch.StartNew();

        private static bool lastFocused, lastForeground;
        private static bool firstFrame = true;
        private static readonly Dictionary<KeyboardKey, bool> lastDown = new();

        // 見るキー（ドン・カッの DFJK・ERUI と、Enter・Esc・Space・矢印）
        private static readonly KeyboardKey[] Watched =
        {
            KeyboardKey.D, KeyboardKey.F, KeyboardKey.J, KeyboardKey.K,
            KeyboardKey.E, KeyboardKey.R, KeyboardKey.U, KeyboardKey.I,
            KeyboardKey.Enter, KeyboardKey.Escape, KeyboardKey.Space,
            KeyboardKey.Up, KeyboardKey.Down, KeyboardKey.Left, KeyboardKey.Right,
        };

        public static bool Enabled
        {
            get
            {
#if DEBUG
                return OperatingSystem.IsWindows();
#else
                return false;
#endif
            }
        }

        /// <summary>ウィンドウを作ったあとに1回呼ぶ（メインのスレッドで）</summary>
        public static void Start()
        {
            if (!Enabled || writer != null) return;
            try
            {
                string dir = Path.Combine(AppContext.BaseDirectory, "Logs");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"KeyProbe_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                writer = new StreamWriter(path, false, new System.Text.UTF8Encoding(false)) { AutoFlush = false };

                windowHandle = GetActiveWindow();
                if (windowHandle == 0) windowHandle = FindWindowW(null, "TakeTaikoSimulator");
                uint thread = GetCurrentThreadId();
                callback = OnMessage;
                hook = SetWindowsHookExW(WhGetMessage, callback, 0, thread);
                Write($"開始 pid={Environment.ProcessId} hwnd=0x{windowHandle:X} thread={thread} hook={(hook != 0 ? "OK" : $"失敗 {Marshal.GetLastWin32Error()}")}");
                Write($"画面 fullscreen={Raylib.IsWindowFullscreen()} undecorated={Raylib.IsWindowState(ConfigFlags.UndecoratedWindow)} " +
                      $"topmost={Raylib.IsWindowState(ConfigFlags.TopmostWindow)} pos={Raylib.GetWindowPosition()} " +
                      $"size={Raylib.GetScreenWidth()}x{Raylib.GetScreenHeight()} monitor={Raylib.GetCurrentMonitor()}");
                writer.Flush();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[KeyProbe] 開始できませんでした：{ex.Message}");
                writer = null;
            }
        }

        /// <summary>毎フレーム（シーンの Update の前）に呼ぶ</summary>
        public static void Update()
        {
            if (writer == null) return;

            bool focused = Raylib.IsWindowFocused();
            nint fg = GetForegroundWindow();
            bool foreground = fg == windowHandle;
            if (firstFrame || focused != lastFocused || foreground != lastForeground)
            {
                GetWindowThreadProcessId(fg, out uint fgPid);
                Write($"フォーカス raylib={focused} 前面={foreground} (前面のウィンドウ 0x{fg:X} pid={fgPid}) " +
                      $"fullscreen={Raylib.IsWindowFullscreen()} pos={Raylib.GetWindowPosition()}");
                lastFocused = focused;
                lastForeground = foreground;
            }

            foreach (KeyboardKey key in Watched)
            {
                bool down = Raylib.IsKeyDown(key);
                lastDown.TryGetValue(key, out bool was);
                if (down != was)
                {
                    Write($"Raylib {(down ? "押した" : "離した")} {key}");
                    lastDown[key] = down;
                }
            }

            firstFrame = false;
            writer.Flush();
        }

        public static void Stop()
        {
            if (hook != 0) { UnhookWindowsHookEx(hook); hook = 0; }
            if (writer != null) { Write("終了"); writer.Dispose(); writer = null; }
        }

        private static void Write(string line) =>
            writer?.WriteLine($"{clock.Elapsed.TotalSeconds,9:0.000} {line}");

        // Windows のメッセージ：キー・文字・フォーカス・クリック・生の入力
        private static nint OnMessage(int code, nint wParam, nint lParam)
        {
            try
            {
                if (writer != null && code >= 0 && wParam == PmRemove && lParam != 0)
                {
                    NativeMessage msg = Marshal.PtrToStructure<NativeMessage>(lParam);
                    string? name = msg.Message switch
                    {
                        0x0100 => "WM_KEYDOWN",
                        0x0101 => "WM_KEYUP",
                        0x0104 => "WM_SYSKEYDOWN",
                        0x0105 => "WM_SYSKEYUP",
                        0x0102 => "WM_CHAR",
                        0x0006 => "WM_ACTIVATE",
                        0x001C => "WM_ACTIVATEAPP",
                        0x0007 => "WM_SETFOCUS",
                        0x0008 => "WM_KILLFOCUS",
                        0x0201 => "WM_LBUTTONDOWN",
                        0x00FF => "WM_INPUT",
                        _ => null,
                    };
                    if (name != null)
                    {
                        long lp = msg.LParam.ToInt64();
                        uint scan = (uint)((lp >> 16) & 0xff);
                        bool extended = (lp & (1L << 24)) != 0;
                        bool repeat = (lp & (1L << 30)) != 0;
                        // GetMessageExtraInfo は、SendInput で送られた入力だと送った側が決めた値になることが多い（目安）
                        nint extra = GetMessageExtraInfo();
                        string target = msg.Hwnd == windowHandle ? "本体" : $"別0x{msg.Hwnd:X}";
                        Write($"Win {name} 宛先={target} wParam=0x{(ulong)msg.WParam:X} scan=0x{scan:X} ext={extended} repeat={repeat} extra=0x{extra:X}");
                    }
                }
            }
            catch
            {
                // 調べもの用なので、失敗してもゲームは止めない
            }
            return CallNextHookEx(0, code, wParam, lParam);
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

        [DllImport("user32.dll")] private static extern nint GetActiveWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowW(string? className, string windowName);
        [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
        [DllImport("user32.dll")] private static extern nint GetMessageExtraInfo();
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookExW(int idHook, HookProcedure procedure, nint module, uint threadId);
        [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(nint hook);
    }
}
