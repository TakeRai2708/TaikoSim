using System.Numerics;
using Raylib_cs;
using TakeTaikoSimulator.Configs;

namespace TakeTaikoSimulator.Graphics
{
    // 1920×1080の論理座標を、実際の画面解像度へ直接描く係
    public static class Screen
    {
        // キャンバスの大きさ(座標は全部この大きさで考える)
        public const int Width = 1920;
        public const int Height = 1080;

        private static RenderTexture2D canvas;
        private static float canvasX, canvasY, canvasScale = 1;
        public static bool IsDrawingCanvas { get; private set; }
        public static float TextRasterScale => IsDrawingCanvas ? canvasScale : 1;
        public static Vector2 SnapTextPosition(Vector2 position) => !IsDrawingCanvas ? position : new(
            (MathF.Round(canvasX + position.X * canvasScale) - canvasX) / canvasScale,
            (MathF.Round(canvasY + position.Y * canvasScale) - canvasY) / canvasScale);

        // 11/22：画面が 1920×1080 より小さいとき(ふつうのウィンドウ)は、1920×1080 のまま描いて最後に画面ごと縮める(TaikøNauts と同じ)。
        //   画面の大きさで直接描くと、絵を 0.9 倍などに縮めて描くことになり、
        //   ・数字やゲージのように1枚に何コマも並べた絵で、となりのコマのはしが混ざって黒い線が出る
        //   ・魂ゲージの細い仕切りの太さが場所でちがって、マスの幅がバラバラに見える
        //   ということが起きていた(たけらいの 11/21 のスクショ)。
        //   1920×1080 以上(フルスクリーンなど)は、今までどおり画面の大きさで直接描く(文字がくっきりする)。
        private static bool downscaled;   // true：1920×1080 で描いて、DrawToWindow で縮めて出す

        private static void PrepareCanvas(int width, int height)
        {
            width = Math.Max(1, width); height = Math.Max(1, height);
            downscaled = Math.Min(width / (float)Width, height / (float)Height) < 1f;
            if (downscaled) { width = Width; height = Height; }
            if (canvas.Texture.Width != width || canvas.Texture.Height != height)
            {
                if (canvas.Id != 0) Raylib.UnloadRenderTexture(canvas);
                canvas = Raylib.LoadRenderTexture(width, height);
            }
            // 縮めて出すときはなめらかに(Bilinear)。同じ大きさで写すときは1画素ずつそのまま(Point)
            Raylib.SetTextureFilter(canvas.Texture, downscaled ? TextureFilter.Bilinear : TextureFilter.Point);
            canvasScale = Math.Min(width / (float)Width, height / (float)Height);
            canvasX = (width - Width * canvasScale) / 2;
            canvasY = (height - Height * canvasScale) / 2;
        }

        // 描画座標は従来の1920×1080。切り取りだけを実際の描画先の画素へ変換する。
        public static void BeginScissorMode(int x, int y, int width, int height)
        {
            if (!IsDrawingCanvas) { Raylib.BeginScissorMode(x, y, width, height); return; }
            int left = (int)MathF.Floor(canvasX + x * canvasScale), top = (int)MathF.Floor(canvasY + y * canvasScale);
            int right = (int)MathF.Ceiling(canvasX + (x + width) * canvasScale), bottom = (int)MathF.Ceiling(canvasY + (y + height) * canvasScale);
            Raylib.BeginScissorMode(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        }
        public static void EndScissorMode() => Raylib.EndScissorMode();

        // ウィンドウ設定を反映する予約(描画中に大きさを変えると危ないので、次のフレームの最初にやる)
        private static bool applyRequested = false;

        // スクリーンショットを保存するフォルダ(exeと同じ場所の Screenshots)
        public static string ScreenshotFolder => Configs.DataPaths.Data("Screenshots");

        // スクリーンショットの予約
        private static bool screenshotRequested = false;

        // 準備(ウィンドウを作った後に呼ぶ)
        public static void Init()
        {
            PrepareCanvas(Raylib.GetRenderWidth(), Raylib.GetRenderHeight());
            SmoothShapes.Preload(); // 設定を開いた瞬間にシェーダーを準備して止まらないようにする
        }

        // 後片付け
        public static void Unload()
        {
            SmoothShapes.Unload();
            Raylib.UnloadRenderTexture(canvas);
            canvas = default; IsDrawingCanvas = false;
        }

        // ここから先の描画はキャンバスに描かれる
        public static void BeginCanvas() => BeginCanvas(Raylib.GetRenderWidth(), Raylib.GetRenderHeight());

        internal static void BeginCanvas(int width, int height)
        {
            PrepareCanvas(width, height);
            Raylib.BeginTextureMode(canvas);
            Raylib.BeginMode2D(new Camera2D { Offset = new(canvasX, canvasY), Target = Vector2.Zero, Rotation = 0, Zoom = canvasScale });
            IsDrawingCanvas = true;
        }

        // キャンバスへの描画を終わる
        public static void EndCanvas()
        {
            Raylib.EndMode2D();
            IsDrawingCanvas = false;
            // ClearBackgroundを使う画面でも、16:9の外は黒帯にする。
            int left = (int)MathF.Floor(canvasX), top = (int)MathF.Floor(canvasY);
            if (left > 0)
            {
                Raylib.DrawRectangle(0, 0, left, canvas.Texture.Height, Color.Black);
                int right = (int)MathF.Ceiling(canvasX + Width * canvasScale);
                Raylib.DrawRectangle(right, 0, canvas.Texture.Width - right, canvas.Texture.Height, Color.Black);
            }
            if (top > 0)
            {
                Raylib.DrawRectangle(0, 0, canvas.Texture.Width, top, Color.Black);
                int bottom = (int)MathF.Ceiling(canvasY + Height * canvasScale);
                Raylib.DrawRectangle(0, bottom, canvas.Texture.Width, canvas.Texture.Height - bottom, Color.Black);
            }
            Raylib.EndTextureMode();
        }

        // キャンバスをウィンドウに表示する(縦横比が合わないときは黒帯)
        public static void DrawToWindow()
        {
            // RenderTexture は上下が逆さまに保存されているので、高さをマイナスにして反転する
            Rectangle source = new Rectangle(0, 0, canvas.Texture.Width, -canvas.Texture.Height);
            Rectangle dest = new Rectangle(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
            if (downscaled)
            {
                // 1920×1080 のキャンバスを、縦横比を保って真ん中に縮めて出す(まわりの黒帯は Game が先に塗っている)
                GetViewport(out float vx, out float vy, out float vscale);
                dest = new Rectangle(vx, vy, Width * vscale, Height * vscale);
            }

            // キャンバスの色(RGB)をそのまま写す。キャンバスの透明度(A)は使わない。
            //   半透明の絵(フェード中の光・顔など)をふつうの合成でキャンバスに描くと、
            //   色は正しく混ざるのに、キャンバスの A だけが 1 より小さくなってしまう
            //   (例：50% で描くと A = 0.5×0.5 + 1×0.5 = 0.75)。
            //   そのまま A を使って黒いウィンドウに描くと、その分だけ暗く見えていた(魂ゲージの光の途中など)。
            //   そこで「元の色 × 1 ＋ 下の色 × 0」で写して、A を無視する。
            const int GlZero = 0; // OpenGL の GL_ZERO
            Rlgl.SetBlendFactorsSeparate(Rlgl.ONE, GlZero, Rlgl.ONE, GlZero, Rlgl.FUNC_ADD, Rlgl.FUNC_ADD);
            Raylib.BeginBlendMode(BlendMode.CustomSeparate);
            Raylib.DrawTexturePro(canvas.Texture, source, dest, Vector2.Zero, 0f, Color.White);
            Raylib.EndBlendMode();
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
        //   11/8：OpenTaiko と同じ「枠ありのウィンドウ」か「本物のフルスクリーン」の2つだけにした。
        //   前は自前のボーダーレス（枠なしで画面とぴったり同じ大きさ）もあったけど、
        //   Windows がそれをフルスクリーンのゲームあつかいして、画面の出し方を特別な近道に切りかえるらしく、
        //   Claude の操作で撮るスクショが古い画面のままになったり、クリックが「デスクトップ」あつかいになったりした（推測。11/8 の調べもの）。
        //   OpenTaiko（Silk.NET + GLFW）にも自前のボーダーレスは無い。
        public static void ApplyWindowMode()
        {
            GameConfig game = ConfigManager.Game;
            Diagnostics.DebugLog.Event("画面の設定", $"ウィンドウ {game.WindowWidth}×{game.WindowHeight}　フルスクリーン {game.Fullscreen}");

            // 1. いったん普通の枠ありウィンドウに戻す（前のバージョンの枠なし・Raylib のボーダーレスも外す）
            if (Raylib.IsWindowFullscreen()) Raylib.ToggleFullscreen();
            if (Raylib.IsWindowState(ConfigFlags.BorderlessWindowMode)) Raylib.ToggleBorderlessWindowed();
            if (Raylib.IsWindowState(ConfigFlags.UndecoratedWindow)) Raylib.ClearWindowState(ConfigFlags.UndecoratedWindow);

            int monitor = Raylib.GetCurrentMonitor();
            int monitorWidth = Raylib.GetMonitorWidth(monitor), monitorHeight = Raylib.GetMonitorHeight(monitor);

            if (game.Fullscreen)
            {
                // 2. 本物のフルスクリーン。解像度はモニターのまま（切り替えない）
                //    モニターの解像度を切り替えるのは、落ちたり画面が真っ暗のまま戻らなかったりしやすいので、やらない。
                //    1920×1080 のキャンバスを、モニターの大きさに合わせて表示する（DrawToWindow）
                Raylib.SetWindowSize(monitorWidth, monitorHeight);
                Raylib.ToggleFullscreen();
                return;
            }

            int width = game.WindowWidth, height = game.WindowHeight;

            // 3. モニターと同じ大きさ(以上)のウィンドウ → 枠なしでモニターにぴったり重ねる（OpenTaiko の「ボーダーレス」と同じ）
            //    11/22：枠ありのまま 1920×1080 にすると、Windows がタイトルバーのぶん高さを縮めて(左右に帯)、タスクバーも上に乗った。
            //    TaikøNauts の「ウィンドウで 1920×1080 にするとフルスクリーンっぽく出る」に合わせる。
            //    ※ 11/8 に自前のボーダーレスをやめたのは、Claude の画面操作（スクショ・クリック）がおかしくなったから。
            //      それが起きるのはこの大きさのときだけ。困ったらウィンドウを小さくすればいい
            if (width >= monitorWidth && height >= monitorHeight)
            {
                Raylib.SetWindowState(ConfigFlags.UndecoratedWindow);
                Raylib.SetWindowSize(monitorWidth, monitorHeight);
                System.Numerics.Vector2 origin = Raylib.GetMonitorPosition(monitor);
                Raylib.SetWindowPosition((int)origin.X, (int)origin.Y);
                return;
            }

            // 4. ふつうの枠ありのウィンドウ。モニターに入りきらない大きさなら、入る大きさ（16:9）まで小さくする
            //    （タイトルバーとタスクバーのぶん、上下に少し余白を取る）
            int maxWidth = monitorWidth - 16, maxHeight = monitorHeight - 100;
            if (width > maxWidth || height > maxHeight)
            {
                float fit = Math.Min((float)maxWidth / width, (float)maxHeight / height);
                width = Math.Max(640, (int)(width * fit));
                height = Math.Max(360, (int)(height * fit));
            }
            Raylib.SetWindowSize(width, height);
            CenterWindow();
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

        // 予約されていたら、実解像度のキャンバスを画像で保存する(EndCanvas の後に呼ぶ)
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
            // 透明度(A)は捨てて、色だけで保存する(画面に出しているのと同じ見え方にする)
            Raylib.ImageFormat(ref image, PixelFormat.UncompressedR8G8B8);
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

            // 絵の部分(タイトルバーを除く)の左上を決める。モニターが2枚以上でも、今のモニターの中に置く
            System.Numerics.Vector2 origin = Raylib.GetMonitorPosition(monitor);
            int x = (int)origin.X + Math.Max(0, (monitorWidth - Raylib.GetScreenWidth()) / 2);
            int y = (int)origin.Y + Math.Max(0, (monitorHeight - Raylib.GetScreenHeight()) / 2);
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

