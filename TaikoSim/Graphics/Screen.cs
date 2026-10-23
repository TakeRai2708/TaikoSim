using System.Numerics;
using Raylib_cs;
using TaikoSim.Configs;

namespace TaikoSim.Graphics
{
    // 1920×1080のキャンバスに描いて、ウィンドウの大きさに合わせて表示する係
    public static class Screen
    {
        // キャンバスの大きさ(座標は全部この大きさで考える)
        public const int Width = 1920;
        public const int Height = 1080;

        private static RenderTexture2D canvas;

        // ウィンドウ設定を反映する予約(描画中に大きさを変えると危ないので、次のフレームの最初にやる)
        private static bool applyRequested = false;

        // スクリーンショットを保存するフォルダ(exeと同じ場所の Screenshots)
        public static readonly string ScreenshotFolder = Path.Combine(AppContext.BaseDirectory, "Screenshots");

        // スクリーンショットの予約
        private static bool screenshotRequested = false;

        // 準備(ウィンドウを作った後に呼ぶ)
        public static void Init()
        {
            canvas = Raylib.LoadRenderTexture(Width, Height);
            Raylib.SetTextureFilter(canvas.Texture, TextureFilter.Bilinear); // 縮小してもガタガタにしない
        }

        // 後片付け
        public static void Unload()
        {
            Raylib.UnloadRenderTexture(canvas);
        }

        // ここから先の描画はキャンバスに描かれる
        public static void BeginCanvas()
        {
            Raylib.BeginTextureMode(canvas);
        }

        // キャンバスへの描画を終わる
        public static void EndCanvas()
        {
            Raylib.EndTextureMode();
        }

        // キャンバスをウィンドウに表示する(縦横比が合わないときは黒帯)
        public static void DrawToWindow()
        {
            GetViewport(out float x, out float y, out float scale);

            // RenderTexture は上下が逆さまに保存されているので、高さをマイナスにして反転する
            Rectangle source = new Rectangle(0, 0, Width, -Height);
            Rectangle dest = new Rectangle(x, y, Width * scale, Height * scale);

            Raylib.DrawTexturePro(canvas.Texture, source, dest, Vector2.Zero, 0f, Color.White);
        }

        // マウスの位置をキャンバスの座標(1920×1080)に変換して返す
        public static Vector2 GetMousePosition()
        {
            Vector2 mouse = Raylib.GetMousePosition();
            GetViewport(out float x, out float y, out float scale);
            return new Vector2((mouse.X - x) / scale, (mouse.Y - y) / scale);
        }

        // ウィンドウ設定の反映を予約する
        public static void RequestApply()
        {
            applyRequested = true;
        }

        // 予約されていたら反映する(Game が毎フレーム最初に呼ぶ)
        public static void ApplyIfRequested()
        {
            if (!applyRequested) return;
            applyRequested = false;
            ApplyWindowMode();
        }

        // ウィンドウ設定を全部反映する(起動時に呼ぶ)
        public static void ApplyWindowSettings()
        {
            ApplyWindowMode();
            ApplyFrameSettings();
        }

        // 解像度とフルスクリーンを反映する
        public static void ApplyWindowMode()
        {
            GameConfig game = ConfigManager.Game;

            // 1. いったん普通のウィンドウに戻す
            if (Raylib.IsWindowFullscreen()) Raylib.ToggleFullscreen();
            if (Raylib.IsWindowState(ConfigFlags.BorderlessWindowMode)) Raylib.ToggleBorderlessWindowed();
            Raylib.ClearWindowState(ConfigFlags.UndecoratedWindow); // 枠を戻す

            int monitor = Raylib.GetCurrentMonitor();
            bool fillsMonitor = game.WindowWidth >= Raylib.GetMonitorWidth(monitor)
                             || game.WindowHeight >= Raylib.GetMonitorHeight(monitor);

            // 2. 表示のしかた
            if (game.Fullscreen && game.FullscreenType == FullscreenType.Exclusive)
            {
                // 排他フルスクリーン(本物のフルスクリーン)
                Raylib.SetWindowSize(game.WindowWidth, game.WindowHeight);
                Raylib.ToggleFullscreen();
            }
            else if (game.Fullscreen || fillsMonitor)
            {
                // ボーダーレス(枠なし・画面ぴったり)
                // フルスクリーン(ボーダーレス)のときと、ウィンドウがモニター以上の大きさのとき
                ApplyOwnBorderless(monitor);
            }
            else
            {
                // ふつうのウィンドウは真ん中に
                Raylib.SetWindowSize(game.WindowWidth, game.WindowHeight);
                CenterWindow();
            }
        }

        // 自前のボーダーレス(Raylibのボーダーレスは常に最前面になってしまうので自分で作る)
        private static void ApplyOwnBorderless(int monitor)
        {
            // ① 先に枠を消す(枠ありのまま大きくすると、Windowsに高さを縮められてしまう)
            Raylib.SetWindowState(ConfigFlags.UndecoratedWindow);

            // ② モニターと同じ大きさにする
            Raylib.SetWindowSize(Raylib.GetMonitorWidth(monitor), Raylib.GetMonitorHeight(monitor));

            // ③ モニターの左上にぴったり置く(モニターが2枚あっても大丈夫なように、モニターの位置を使う)
            Vector2 monitorPos = Raylib.GetMonitorPosition(monitor);
            Raylib.SetWindowPosition((int)monitorPos.X, (int)monitorPos.Y);
        }

        // 垂直同期と目標フレームレートを反映する
        public static void ApplyFrameSettings()
        {
            GameConfig game = ConfigManager.Game;

            if (game.VSync) Raylib.SetWindowState(ConfigFlags.VSyncHint);
            else Raylib.ClearWindowState(ConfigFlags.VSyncHint);

            Raylib.SetTargetFPS(game.TargetFps); // 0 は無制限
        }

        // スクリーンショットを予約する
        public static void RequestScreenshot()
        {
            screenshotRequested = true;
        }

        // 予約されていたら、キャンバス(1920×1080)を画像で保存する(EndCanvas の後に呼ぶ)
        // 保存したら結果のメッセージを返す。予約が無ければ null
        public static string? SaveScreenshotIfRequested()
        {
            if (!screenshotRequested) return null;
            screenshotRequested = false;

            Directory.CreateDirectory(ScreenshotFolder);
            string fileName = $"TaikoSim_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
            string path = Path.Combine(ScreenshotFolder, fileName);

            Image image = Raylib.LoadImageFromTexture(canvas.Texture);
            Raylib.ImageFlipVertical(ref image); // RenderTexture は上下逆さまなので戻す
            bool ok = Raylib.ExportImage(image, path);
            Raylib.UnloadImage(image);

            return ok ? $"スクリーンショットを保存しました：{fileName}" : "スクリーンショットの保存に失敗しました";
        }

        // ウィンドウを画面の真ん中に移動する
        public static void CenterWindow()
        {
            int monitor = Raylib.GetCurrentMonitor();
            int monitorWidth = Raylib.GetMonitorWidth(monitor);
            int monitorHeight = Raylib.GetMonitorHeight(monitor);

            int x = Math.Max(0, (monitorWidth - Raylib.GetScreenWidth()) / 2);
            int y = Math.Max(0, (monitorHeight - Raylib.GetScreenHeight()) / 2);
            Raylib.SetWindowPosition(x, y);
        }

        // キャンバスの座標(1920×1080)を、ウィンドウの中の座標に変換して返す(GetMousePosition の逆)
        // 日本語入力の候補の一覧を出す場所に使う
        public static Vector2 CanvasToWindow(Vector2 point)
        {
            GetViewport(out float x, out float y, out float scale);
            return new Vector2(x + point.X * scale, y + point.Y * scale);
        }

        // キャンバスをウィンドウのどこに、何倍で表示するか計算する
        private static void GetViewport(out float x, out float y, out float scale)
        {
            float windowWidth = Raylib.GetScreenWidth();
            float windowHeight = Raylib.GetScreenHeight();

            // 縦と横で、はみ出さない方の倍率を使う
            scale = Math.Min(windowWidth / Width, windowHeight / Height);

            // 真ん中に寄せる
            x = (windowWidth - Width * scale) / 2f;
            y = (windowHeight - Height * scale) / 2f;
        }
    }
}