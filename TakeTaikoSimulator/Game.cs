using Raylib_cs;
using TakeTaikoSimulator.Audio;
using TakeTaikoSimulator.Charts;
using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Diagnostics;
using TakeTaikoSimulator.Graphics;
using TakeTaikoSimulator.Input;
using TakeTaikoSimulator.Overlays;
using TakeTaikoSimulator.Scenes;
using TakeTaikoSimulator.UI;

namespace TakeTaikoSimulator
{
    // ゲーム全体のメインループ
    public class Game
    {
        // 設定パネル(オーバーレイ)
        private static readonly SettingsOverlay settings = new();
        private static bool settingsClosedThisFrame;

        // 設定パネルが開いているか(後ろの画面がクリックに反応しないようにするのに使う)
        public static bool IsSettingsOpen => settings.IsOpen || settingsClosedThisFrame;

        // 設定パネルを開く(モード選択の「設定」から)
        public static void OpenSettings() => settings.Open(fullScreen: true);

        // 右上に少しだけ出すお知らせ(スクショ保存したよ、とか)
        private string toastText = "";
        private float toastTimer = 0f;

        // true にするとアプリが終了する(設定パネルの⏻ボタンで使う)
        public static bool ShouldExit { get; set; } = false;

        // true なら、終わったあとにもう一度起動する（描画方式を変えたとき。音や窓を全部片付けてから起動する）
        private static bool restartRequested;
        public static void RequestRestart()
        {
            restartRequested = true;
            ShouldExit = true;
        }

        // 重さの確認用：直近1秒で一番時間がかかったフレーム(ミリ秒)
        // 普段は 1000 ÷ FPS くらい(120FPSなら約8ms)。カクッとすると大きくなる
        private bool firstFrameShown;       // 最初の1枚を出したか（描画方式の「起動できた」印を消すため）
        private float frameMaxMs = 0f;      // 今数えている1秒の中の一番大きい値
        private int fpsShown = 0;           // FPSの表示値も1秒間固定する
        private float frameMaxShownMs = 0f; // 画面に出す値(1秒ごとに入れ替える)
        private float frameWindowTimer = 0f;
        private const float SlowFrameMs = 20f; // これより大きいと赤で出す

        public void Run()
        {
            // 設定を全部読み込む
            ConfigManager.LoadAll();
            PlayerSettings.Migrate();   // 前からあるプレイヤーデータに、今の設定を写す（最初の1回だけ）

            // 描画方式を決める（Raylib を初めて呼ぶ前。ANGLE のときは raylib_angle.dll を読ませる）
            GraphicsBackend.Select(ConfigManager.Game.GraphicsBackend);
            RaylibLog.Start();   // raylib の INFO / WARNING もログファイルに残す（黒い画面がすぐ消えても後で見られる）

            // 設定の解像度でウィンドウを作る
            Raylib.InitWindow(ConfigManager.Game.WindowWidth, ConfigManager.Game.WindowHeight, "TakeTaikoSimulator");
            if (!Raylib.IsWindowReady())
            {
                // 窓を作れなかった（ANGLE の Vulkan など）。ANGLE なら OpenGL にもどして、もう一度起動する
                if (GraphicsBackend.FallBackAfterWindowFailure()) StartAgain();
                return;
            }
            GraphicsBackend.CheckAfterInit();   // ANGLE のとき、ちゃんと描けるか確かめる（ダメなら次は OpenGL）
            RaylibLog.QuietAfterStartup();      // ここからの raylib のログは WARNING 以上だけ
            Platform.AppIcon.Apply();

            // Escで勝手にウィンドウが閉じないようにする(Escは「戻る」に使う)
            Raylib.SetExitKey(KeyboardKey.Null);

            // 画面・フォントの準備(ウィンドウを作った後じゃないとダメ)
            Screen.Init();
            Screen.ApplyWindowSettings(); // 解像度・フルスクリーン・垂直同期・目標FPSを反映
            FontManager.Init();
            AudioManager.Init();          // 音の準備(bass.dll が無くても落ちない)
            WindowsIme.Init();            // 日本語入力の準備(検索の箱の中に変換中の文字を出すため)
            KeyProbe.Start();             // Debug ビルドだけ：キーがどこまで届いているかを Logs/KeyProbe_*.txt に書く(11/8 の調べもの)

            // デバッグ記録(設定で ON のときだけ記録する)に、数を教える係を渡す
            DebugLog.FontPageCounter = () => FontManager.TotalPageCount;
            DebugLog.TextureCounter = () => SkinManager.TextureCount;
            DebugLog.BassCpuReader = () => AudioManager.IsAvailable ? ManagedBass.Bass.CPUUsage : 0;

            // 最初は読み込み画面(スキンと曲を読む)
            // 読み終わったら、読み込み画面がホーム(または初回起動セットアップ)に進める
            SceneManager.ChangeScene(new LoadingScene());

            // ウィンドウの×が押されるか、ShouldExit が true になるまで繰り返す
            while (!Raylib.WindowShouldClose() && !ShouldExit)
            {
                DebugLog.BeginFrame();
                float dt = Raylib.GetFrameTime(); // 前のフレームからの経過時間(秒)
                MeasureFrame(dt);

                KeyProbe.Update();
                Screen.ApplyIfRequested(); // 設定でウィンドウの大きさなどが変わっていたら反映
                SceneManager.Update(dt);   // 画面の切り替えの、暗くする・明るくする
                SceneManager.ApplyChange();
                Update(dt);
                MenuBgm.Update(SceneManager.Current, dt);
                WindowsIme.EndFrame(); // このフレームで文字の入力を頼まれていなければ IME を切る
                Draw();
                DebugLog.EndFrame(Raylib.GetFPS());
            }

            // 後片付け
            WindowsIme.Shutdown(); // ウィンドウを閉じる前に、横取りしていたメッセージを元に戻す
            SceneManager.Current?.Exit(); // 曲もここで片付く
            MenuBgm.Shutdown();
            Platform.DiscordPresence.Shutdown();
            AudioManager.Free();
            CharaBaker.Stop();
            KeyProbe.Stop();
            NamePlate.Clear();
            SkinManager.Unload();
            FontManager.Unload();
            Screen.Unload();
            Raylib.CloseWindow();

            // 終了時に設定を保存
            ConfigManager.SaveAll();

            // デバッグ記録の書き残しを、全部ファイルに書いてから終わる
            DebugLog.Shutdown();

            // 「再起動しますか？」で［はい］：全部片付けてから、同じ exe をもう一度起動する
            if (restartRequested) StartAgain();
        }

        // 同じ exe をもう一度起動する
        private static void StartAgain()
        {
            if (Environment.ProcessPath is string exe)
            {
                try
                {
                    // UseShellExecute = true：新しい黒い画面(コンソール)をもらって起動する。
                    // false だと今の黒い画面を引きつぐので、Visual Studio が今のプロセスの終わりに黒い画面を閉じたとき、
                    // 新しいほうもいっしょに消えていた（11/22：［はい］を押すと終わるだけだった）
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
                    {
                        UseShellExecute = true,
                        WorkingDirectory = AppContext.BaseDirectory,
                    });
                    Console.WriteLine("[Restart] もう一度起動しました");
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
                {
                    Console.WriteLine($"[Restart] もう一度起動できませんでした: {ex.Message}");
                }
            }
        }

        // フレームの時間を記録する(1秒ごとに一番大きい値を画面用に入れ替える)
        private void MeasureFrame(float dt)
        {
            float ms = dt * 1000f;
            frameMaxMs = Math.Max(frameMaxMs, ms);
            frameWindowTimer += dt;

            // カクッとしたら、TakeTaikoSimulator の後ろに出ている黒い画面(コンソール)にも書いておく
            //   (デバッグ記録中は、どこが重かったかまで events.csv の「重いフレーム」に書くので、ここでは書かない)
            if (ms > SlowFrameMs && !DebugLog.Active) Console.WriteLine($"[重さ] {DateTime.Now:HH:mm:ss.fff} 1フレームに {ms:F1} ms かかりました");

            if (frameWindowTimer >= 1f)
            {
                fpsShown = Raylib.GetFPS();
                frameMaxShownMs = frameMaxMs;
                frameMaxMs = 0f;
                frameWindowTimer = 0f;
            }
        }

        // 更新(入力の処理など)
        private void Update(float dt)
        {
            settingsClosedThisFrame = false;
            // 裏で曲を読み終わっていたら、新しい曲の一覧に入れ替える(画面の処理より先にやる)
            long t = DebugLog.Begin();
            SongList.Update();
            DebugLog.End(PerfSection.SongList, t);

            // ファイルやフォルダがウィンドウにドロップされた
            // 設定パネルが開いていればスキンフォルダの変更、そうでなければ今の画面に渡す(.tja のテストなど)
            if (Raylib.IsFileDropped())
            {
                string[] dropped = Raylib.GetDroppedFiles();
                if (settings.IsOpen) settings.OnFilesDropped(dropped);
                else SceneManager.Current?.OnFilesDropped(dropped);
            }

            // スクリーンショットはどこでも反応する(設定パネル中・演奏中も)
            // ただしキー入力待ちのとき(設定パネル・セットアップ画面)は、登録の方を優先するので無視
            bool waitingKey = settings.IsWaitingKey || (SceneManager.Current?.IsWaitingKey ?? false);
            if (!waitingKey && InputManager.IsSystemPressed(SystemAction.Screenshot))
            {
                Screen.RequestScreenshot();
            }

            // お知らせの残り時間を減らす
            if (toastTimer > 0f) toastTimer -= dt;

            // どんちゃんの声のくり返し（設定パネル中・画面の切り替え中も進める）
            MenuVoice.Update(dt);
            Platform.DiscordPresence.Update(SceneManager.Current);

            // 設定パネルが開いているときは、後ろの画面は操作させない
            if (settings.IsOpen)
            {
                t = DebugLog.Begin();
                settings.Update(dt);
                DebugLog.End(PerfSection.SettingsUpdate, t);
                settingsClosedThisFrame = !settings.IsOpen;
                return;
            }

            Scene? scene = SceneManager.Current;
            if (scene == null) return;

            // 画面を切り替えるために暗くしている間は、今の画面を止める(押しても反応しない)
            if (SceneManager.IsBlocking) { scene.UpdateBlocked(dt); return; }

            // 設定キーで設定パネルを開く(演奏中など、開けない画面では無視)
            if (scene.CanOpenSettings && InputManager.IsSystemPressed(SystemAction.Settings))
            {
                settings.Open();
                return;
            }

            // Escは「戻る」
            if (InputManager.IsBackPressed())
            {
                scene.OnBack();
                return; // Esc を同じフレームの選曲・モード操作に使わせない
            }

            t = DebugLog.Begin();
            scene.Update(dt);
            DebugLog.End(PerfSection.SceneUpdate, t);
        }

        // 描画
        private void Draw()
        {
            // ① 1920×1080のキャンバスに描く
            Screen.BeginCanvas();
            Raylib.ClearBackground(Color.Black);

            // モード選択からの全画面設定では、後ろのモード画面を描かない
            Gui.SetMouseInputEnabled(!IsSettingsOpen);
            long t = DebugLog.Begin();
            if (!settings.IsFullScreen)
            {
                SceneManager.Current?.Draw();
                SceneManager.DrawFade(); // 画面の切り替えの黒い幕
            }
            DebugLog.End(PerfSection.SceneDraw, t);
            Gui.SetMouseInputEnabled(true);

            if (settings.IsOpen)
            {
                t = DebugLog.Begin();
                settings.Draw();
                DebugLog.End(PerfSection.SettingsDraw, t);
            }

            t = DebugLog.Begin();
            DebugLog.PauseTextCount(true); // この表示の文字は「文字の数」に入れない
            // FPS表示(左上)
            if (ConfigManager.Game.ShowFps && !settings.IsFullScreen)
            {
                // 設定パネルが開いているときはパネルの右隣に出す(ロゴとかぶらないように)
                float fpsX = settings.IsOpen ? SettingsOverlay.PanelWidth + 10 : 10;
                string fpsText = $"{fpsShown} FPS　最大 {frameMaxShownMs:F1} ms";
                FontManager.Draw(fpsText, fpsX, 8, 28, frameMaxShownMs > SlowFrameMs ? Color.Red : Color.Lime);
            }
            if (ConfigManager.Game.ShowPerfOverlay) DrawPerfOverlay();
            if (ConfigManager.Game.ShowCursorPosition) DrawCursorPosition();
            DebugLog.PauseTextCount(false);
            DebugLog.End(PerfSection.OverlayDraw, t);

            // ここまでにたまった描画を、まとめて GPU に送る
            t = DebugLog.Begin();
            Screen.EndCanvas();
            DebugLog.End(PerfSection.CanvasFlush, t);

            // スクリーンショットが予約されていたら保存(キャンバスを描き終わった直後)
            t = DebugLog.Begin();
            string? screenshotResult = Screen.SaveScreenshotIfRequested();
            DebugLog.End(PerfSection.Screenshot, t);
            if (screenshotResult != null)
            {
                toastText = screenshotResult;
                toastTimer = 2f; // 2秒表示
                DebugLog.Event("スクショ", screenshotResult);
            }

            // ② キャンバスをウィンドウの大きさに縮小して表示(画面合成)
            t = DebugLog.Begin();
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);
            Screen.DrawToWindow();

            // お知らせはウィンドウに直接描く(スクショに写らないように)
            if (toastTimer > 0f)
            {
                DrawToast();
            }
            DebugLog.End(PerfSection.Composite, t);

            // 画面を入れ替える(垂直同期・FPS の上限の待ちもここに入る)
            t = DebugLog.Begin();
            Raylib.EndDrawing();
            if (!firstFrameShown) { firstFrameShown = true; GraphicsBackend.FirstFrameShown(); }
            DebugLog.End(PerfSection.Present, t);
        }

        // 重さの内訳(右上)。1秒ごとの「平均 / 一番重かったフレーム」を ms で出す
        //   作業 = CPU が働いた時間(表示・待ちを引いたもの)。1000 ÷ 作業 が、出せる FPS のめやす
        // マウスを置いた所の座標(設定の「マウスの位置の座標を出す」)
        //   11/11 たけらい：素材の位置がずれていたら自分で直したい。スクショから換算しなくても、座標がそのまま読めるように。
        //   座標はコードと同じ 1920×1080 のキャンバスの座標(左上が 0, 0)。ウィンドウの大きさは関係ない。
        //   細い十字の線と、マウスの右下に「x, y」を出す(画面の端では左・上に回る)
        private static void DrawCursorPosition()
        {
            System.Numerics.Vector2 m = Screen.GetMousePosition();
            if (m.X < 0 || m.Y < 0 || m.X >= Screen.Width || m.Y >= Screen.Height) return;
            int x = (int)MathF.Floor(m.X), y = (int)MathF.Floor(m.Y);

            Color line = new Color(0, 255, 255, 110);
            Raylib.DrawLine(0, y, Screen.Width, y, line);
            Raylib.DrawLine(x, 0, x, Screen.Height, line);

            string text = $"{x}, {y}";
            const float size = 28;
            System.Numerics.Vector2 t = FontManager.Measure(text, size);
            float bx = x + 18, by = y + 18;
            if (bx + t.X + 16 > Screen.Width) bx = x - 18 - t.X - 16;
            if (by + t.Y + 8 > Screen.Height) by = y - 18 - t.Y - 8;
            Raylib.DrawRectangleRec(new Rectangle(bx, by, t.X + 16, t.Y + 8), new Color(0, 0, 0, 200));
            FontManager.Draw(text, bx + 8, by + 4, size, Color.SkyBlue);
        }

        private static void DrawPerfOverlay()
        {
            const float size = 20f, line = 24f, width = 330f;
            int rows = 0;
            for (int i = 0; i < DebugLog.SectionTotal; i++) if (DebugLog.IsShown(i)) rows++;
            float x = Screen.Width - width - 10, y = 60;
            float height = line * (rows + 4) + 12;
            Raylib.DrawRectangleRec(new Rectangle(x - 8, y - 6, width + 16, height), new Color(0, 0, 0, 190));

            FontManager.Draw($"作業　{DebugLog.ShownWorkAvg:F2} / {DebugLog.ShownWorkMax:F1} ms", x, y, size,
                DebugLog.ShownWorkMax > DebugLog.SlowFrameMs ? Color.Red : Color.Lime);
            y += line;
            for (int i = 0; i < DebugLog.SectionTotal; i++)
            {
                if (!DebugLog.IsShown(i)) continue;
                double avg = DebugLog.ShownAvg[i], max = DebugLog.ShownMax[i];
                FontManager.Draw(DebugLog.SectionShortName(i), x, y, size, Color.White);
                FontManager.Draw($"{avg:F2} / {max:F1}", x + 150, y, size, max > 8 ? Color.Orange : Color.LightGray);
                y += line;
            }
            FontManager.Draw($"文字　{DebugLog.ShownGlyphs} 文字/フレーム", x, y, size, Color.SkyBlue);
            y += line;
            FontManager.Draw($"確保　{DebugLog.ShownAllocKb:F1} KB/フレーム", x, y, size, Color.SkyBlue);
            y += line;
            FontManager.Draw(DebugLog.Active ? "● CSV 記録中" : "CSV 記録：OFF", x, y, size, DebugLog.Active ? Color.Red : Color.Gray);
        }

        // 右上にお知らせを出す
        private void DrawToast()
        {
            const float size = 20f;
            System.Numerics.Vector2 measured = FontManager.Measure(toastText, size);

            float x = Raylib.GetScreenWidth() - measured.X - 24;
            float y = 12;

            Raylib.DrawRectangleRec(new Rectangle(x - 12, y - 6, measured.X + 24, measured.Y + 12), new Color(0, 0, 0, 200));
            FontManager.Draw(toastText, x, y, size, Color.White);
        }
    }
}

