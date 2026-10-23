using System.Numerics;
using System.Runtime.InteropServices;
using Raylib_cs;
using TaikoSim.Graphics;

namespace TaikoSim.Input
{
    // 日本語入力(IME)の係。Windows のときだけ動く(ほかの OS では何もしない)
    //
    // Raylib の中の GLFW は IME のメッセージ(WM_IME_*)を何も処理しないので、
    // そのままだと変換中の文字が Windows の小さい窓(ウィンドウの左上)に出てしまう。
    // そこで、ウィンドウに届くメッセージを先に受け取って(ウィンドウプロシージャのサブクラス化)、
    //   ・変換中の文字とカーソル位置を覚える(画面にはこっちで描く)
    //   ・確定した文字を覚える(TakeResultText で取り出す)
    //   ・Windows の変換中の窓は出さない。候補の一覧は、指定した場所(検索の箱の下)に出してもらう
    // をする。
    //
    // 使い方
    //   ・起動時に Init()、終了時に Shutdown()(Game がやる)
    //   ・文字を入力させたい画面は、そのフレームの Update の中で RequestTextInput(...) を呼ぶ
    //   ・Game が毎フレーム EndFrame() を呼ぶ。そのフレームに RequestTextInput が無ければ IME を切る
    //     → 演奏中・設定パネル・数字の箱では IME が使えない(叩くキーが IME に食われない)
    public static class WindowsIme
    {
        // ----- Windows のメッセージの番号など -----
        private const int GwlpWndProc = -4;
        private const uint WmImeSetContext = 0x0281;
        private const uint WmImeStartComposition = 0x010D;
        private const uint WmImeEndComposition = 0x010E;
        private const uint WmImeComposition = 0x010F;
        private const long IscShowUiCompositionWindow = 0x80000000;
        private const uint GcsCompStr = 0x0008;
        private const uint GcsCursorPos = 0x0080;
        private const uint GcsResultStr = 0x0800;
        private const uint IaceDefault = 0x0010;
        private const uint NiCompositionStr = 0x0015;
        private const uint CpsCancel = 0x0004;
        private const uint CfsPoint = 0x0002;
        private const uint CfsExclude = 0x0080;

        private static nint windowHandle;
        private static nint originalProc;
        private static readonly WndProcedure ownProc = WndProc; // GC に消されないように持っておく
        private static bool enabled = true; // Init で切る(最初は IME なし)

        // このフレームで文字の入力を頼まれたか・候補の一覧を出す場所(キャンバスの座標)
        private static bool requested;
        private static Rectangle requestArea;
        private static Vector2 requestCaret;

        // 確定した文字(取り出されるまでためておく)
        private static string resultText = "";

        // 今変換中か・変換中の文字・その中のカーソルの位置(文字数)
        public static bool IsComposing { get; private set; }
        public static string CompositionText { get; private set; } = "";
        public static int CompositionCursor { get; private set; }

        // IME の係が動いているか(Windows 以外や、準備に失敗したときは false)
        public static bool IsActive => originalProc != 0;

        // ===== 準備・片付け =====

        // ウィンドウを作った後に1回呼ぶ。失敗してもゲームは今まで通り動く(IME の文字が左上に出るだけ)
        public static void Init()
        {
            if (!OperatingSystem.IsWindows() || IsActive) return;
            try
            {
                // Raylib が作ったウィンドウを探す(WindowsKeyCapture と同じ探し方)
                nint hwnd = GetActiveWindow();
                if (hwnd == 0) hwnd = FindWindowW(null, "TaikoSim");
                if (hwnd == 0) return;
                uint thread = GetWindowThreadProcessId(hwnd, out uint processId);
                if (processId != (uint)Environment.ProcessId || thread != GetCurrentThreadId()) return;

                windowHandle = hwnd;
                originalProc = SetWindowLongPtrW(hwnd, GwlpWndProc, Marshal.GetFunctionPointerForDelegate(ownProc));
                if (originalProc == 0)
                {
                    Console.WriteLine($"[IME] 準備できませんでした（Win32: {Marshal.GetLastWin32Error()}）");
                    return;
                }

                SetEnabled(false); // 最初は IME なし(検索の箱に入ったら使えるようにする)
                Console.WriteLine("[IME] 準備できました");
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                Console.WriteLine($"[IME] 準備できませんでした（{ex.Message}）");
                originalProc = 0;
            }
        }

        // 終了時に呼ぶ(ウィンドウを閉じる前)。元のウィンドウプロシージャと IME を元に戻す
        public static void Shutdown()
        {
            if (!IsActive) return;
            SetEnabled(true);
            SetWindowLongPtrW(windowHandle, GwlpWndProc, originalProc);
            originalProc = 0;
            windowHandle = 0;
        }

        // ===== 毎フレーム =====

        // 文字を入力させたい画面が、そのフレームの Update で呼ぶ
        //   area  … 入力の箱(キャンバスの座標)。候補の一覧はここに重ならないように出る
        //   caret … 変換中の文字を描き始める位置(キャンバスの座標。文字の左上)
        public static void RequestTextInput(Rectangle area, Vector2 caret)
        {
            requested = true;
            requestArea = area;
            requestCaret = caret;
        }

        // Game が Update の後に毎フレーム呼ぶ。頼まれていれば IME を使えるように、無ければ切る
        public static void EndFrame()
        {
            if (!IsActive)
            {
                requested = false;
                return;
            }

            SetEnabled(requested);
            if (requested) PlaceCandidateWindow();
            requested = false;
        }

        // 確定した文字を取り出す(取り出したら空になる)
        public static string TakeResultText()
        {
            string text = resultText;
            resultText = "";
            return text;
        }

        // ===== 中の処理 =====

        private static void SetEnabled(bool value)
        {
            if (enabled == value) return;
            enabled = value;

            if (!value)
            {
                // 変換の途中なら取り消してから切る(切ったあとに勝手に確定しないように)
                nint himc = ImmGetContext(windowHandle);
                if (himc != 0)
                {
                    ImmNotifyIME(himc, NiCompositionStr, CpsCancel, 0);
                    ImmReleaseContext(windowHandle, himc);
                }
                ImmAssociateContextEx(windowHandle, 0, 0); // 0 = IME を使わない
                ClearComposition();
                resultText = "";
            }
            else
            {
                ImmAssociateContextEx(windowHandle, 0, IaceDefault); // いつもの IME に戻す
            }
        }

        // 候補の一覧を、入力の箱の下に出してもらう(座標はウィンドウの中の座標に直して渡す)
        private static void PlaceCandidateWindow()
        {
            nint himc = ImmGetContext(windowHandle);
            if (himc == 0) return;

            Vector2 caret = Screen.CanvasToWindow(requestCaret);
            Vector2 areaTopLeft = Screen.CanvasToWindow(new Vector2(requestArea.X, requestArea.Y));
            Vector2 areaBottomRight = Screen.CanvasToWindow(new Vector2(requestArea.X + requestArea.Width, requestArea.Y + requestArea.Height));
            NativeRect area = new()
            {
                Left = (int)areaTopLeft.X,
                Top = (int)areaTopLeft.Y,
                Right = (int)areaBottomRight.X,
                Bottom = (int)areaBottomRight.Y,
            };
            NativePoint point = new() { X = (int)caret.X, Y = (int)caret.Y };

            // 変換中の窓の位置(出さないけど、IME によっては候補の一覧をこの位置の近くに出すので入れておく)
            CompositionForm composition = new() { Style = CfsPoint, CurrentPos = point, Area = area };
            ImmSetCompositionWindow(himc, ref composition);

            // 候補の一覧：箱(area)に重ならないように、箱の下に出す
            CandidateForm candidate = new() { Index = 0, Style = CfsExclude, CurrentPos = point, Area = area };
            ImmSetCandidateWindow(himc, ref candidate);

            ImmReleaseContext(windowHandle, himc);
        }

        private static void ClearComposition()
        {
            IsComposing = false;
            CompositionText = "";
            CompositionCursor = 0;
        }

        // ウィンドウに届くメッセージを、GLFW より先に見る(メインの流れ(PollEvents の中)で呼ばれる)
        private static nint WndProc(nint hwnd, uint message, nint wParam, nint lParam)
        {
            try
            {
                switch (message)
                {
                    case WmImeSetContext:
                        // Windows の「変換中の窓」を出さない(こっちで描くので)
                        if (wParam != 0) lParam = (nint)(lParam.ToInt64() & ~IscShowUiCompositionWindow);
                        break;

                    case WmImeStartComposition:
                        IsComposing = true;
                        CompositionText = "";
                        CompositionCursor = 0;
                        return 0; // 元に渡すと Windows の変換中の窓が出るので渡さない

                    case WmImeComposition:
                        ReadComposition(hwnd, (uint)lParam.ToInt64());
                        return 0; // 確定した文字はこっちで受け取ったので、元には渡さない(二重に入らないように)

                    case WmImeEndComposition:
                        ClearComposition();
                        return 0;
                }
            }
            catch (Exception ex)
            {
                // ネイティブから呼ばれる関数で例外を投げるとゲームが落ちるので、ここで止める
                Console.WriteLine($"[IME] {ex.GetType().Name}: {ex.Message}");
            }
            return CallWindowProcW(originalProc, hwnd, message, wParam, lParam);
        }

        // 変換中の文字・確定した文字を読む
        private static void ReadComposition(nint hwnd, uint flags)
        {
            nint himc = ImmGetContext(hwnd);
            if (himc == 0) return;
            try
            {
                // 確定した文字(変換中の文字と一緒に来ることもあるので、先に読む)
                if ((flags & GcsResultStr) != 0)
                {
                    resultText += GetString(himc, GcsResultStr);
                }

                if ((flags & GcsCompStr) != 0)
                {
                    CompositionText = GetString(himc, GcsCompStr);
                    int cursor = ImmGetCompositionStringW(himc, GcsCursorPos, null, 0);
                    CompositionCursor = Math.Clamp(cursor, 0, CompositionText.Length);
                }
                else if ((flags & GcsResultStr) != 0 || flags == 0)
                {
                    // 確定だけのとき・取り消したとき(何も付いていない)：変換中の文字は無くなった
                    CompositionText = "";
                    CompositionCursor = 0;
                }
                IsComposing = CompositionText.Length > 0;
            }
            finally
            {
                ImmReleaseContext(hwnd, himc);
            }
        }

        // ImmGetCompositionStringW で文字を取る(長さはバイト数で返ってくる)
        private static string GetString(nint himc, uint index)
        {
            int bytes = ImmGetCompositionStringW(himc, index, null, 0);
            if (bytes <= 0) return "";
            char[] buffer = new char[bytes / 2];
            int got = ImmGetCompositionStringW(himc, index, buffer, (uint)bytes);
            return got <= 0 ? "" : new string(buffer, 0, got / 2);
        }

        // ===== Windows の関数 =====

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct CompositionForm
        {
            public uint Style;
            public NativePoint CurrentPos;
            public NativeRect Area;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CandidateForm
        {
            public uint Index;
            public uint Style;
            public NativePoint CurrentPos;
            public NativeRect Area;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate nint WndProcedure(nint hwnd, uint message, nint wParam, nint lParam);

        [DllImport("user32.dll", EntryPoint = "GetActiveWindow")]
        private static extern nint GetActiveWindow();

        [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)]
        private static extern nint FindWindowW(string? className, string windowName);

        [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
        private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

        [DllImport("kernel32.dll", EntryPoint = "GetCurrentThreadId")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern nint SetWindowLongPtrW(nint hwnd, int index, nint newValue);

        [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
        private static extern nint CallWindowProcW(nint previous, nint hwnd, uint message, nint wParam, nint lParam);

        [DllImport("imm32.dll", EntryPoint = "ImmGetContext")]
        private static extern nint ImmGetContext(nint hwnd);

        [DllImport("imm32.dll", EntryPoint = "ImmReleaseContext")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ImmReleaseContext(nint hwnd, nint himc);

        [DllImport("imm32.dll", EntryPoint = "ImmGetCompositionStringW", CharSet = CharSet.Unicode)]
        private static extern int ImmGetCompositionStringW(nint himc, uint index, [Out] char[]? buffer, uint bufferBytes);

        [DllImport("imm32.dll", EntryPoint = "ImmAssociateContextEx")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ImmAssociateContextEx(nint hwnd, nint himc, uint flags);

        [DllImport("imm32.dll", EntryPoint = "ImmNotifyIME")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ImmNotifyIME(nint himc, uint action, uint index, uint value);

        [DllImport("imm32.dll", EntryPoint = "ImmSetCompositionWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ImmSetCompositionWindow(nint himc, ref CompositionForm form);

        [DllImport("imm32.dll", EntryPoint = "ImmSetCandidateWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ImmSetCandidateWindow(nint himc, ref CandidateForm form);
    }
}