using Raylib_cs;
using TaikoSim.Audio;
using TaikoSim.Charts;
using TaikoSim.Configs;
using TaikoSim.Graphics;
using TaikoSim.Input;
using TaikoSim.Overlays;
using TaikoSim.Scenes;
using TaikoSim.UI;

namespace TaikoSim
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

        // 重さの確認用：直近1秒で一番時間がかかったフレーム(ミリ秒)
        // 普段は 1000 ÷ FPS くらい(120FPSなら約8ms)。カクッとすると大きくなる
        private float frameMaxMs = 0f;      // 今数えている1秒の中の一番大きい値
        private float frameMaxShownMs = 0f; // 画面に出す値(1秒ごとに入れ替える)
        private float frameWindowTimer = 0f;
        private const float SlowFrameMs = 20f; // これより大きいと赤で出す

        public void Run()
        {
            // 設定を全部読み込む
            ConfigManager.LoadAll();

            // 設定の解像度でウィンドウを作る
            Raylib.InitWindow(ConfigManager.Game.WindowWidth, ConfigManager.Game.WindowHeight, "TaikoSim");

            // Escで勝手にウィンドウが閉じないようにする(Escは「戻る」に使う)
            Raylib.SetExitKey(KeyboardKey.Null);

            // 画面・フォントの準備(ウィンドウを作った後じゃないとダメ)
            Screen.Init();
            Screen.ApplyWindowSettings(); // 解像度・フルスクリーン・垂直同期・目標FPSを反映
            FontManager.Init();
            AudioManager.Init();          // 音の準備(bass.dll が無くても落ちない)
            WindowsIme.Init();            // 日本語入力の準備(検索の箱の中に変換中の文字を出すため)

            // 最初は読み込み画面(スキンと曲を読む)
            // 読み終わったら、読み込み画面がホーム(または初回起動セットアップ)に進める
            SceneManager.ChangeScene(new LoadingScene());

            // ウィンドウの×が押されるか、ShouldExit が true になるまで繰り返す
            while (!Raylib.WindowShouldClose() && !ShouldExit)
            {
                float dt = Raylib.GetFrameTime(); // 前のフレームからの経過時間(秒)
                MeasureFrame(dt);

                Screen.ApplyIfRequested(); // 設定でウィンドウの大きさなどが変わっていたら反映
                SceneManager.ApplyChange();
                Update(dt);
                WindowsIme.EndFrame(); // このフレームで文字の入力を頼まれていなければ IME を切る
                Draw();
            }

            // 後片付け
            WindowsIme.Shutdown(); // ウィンドウを閉じる前に、横取りしていたメッセージを元に戻す
            SceneManager.Current?.Exit(); // 曲もここで片付く
            AudioManager.Free();
            SkinManager.Unload();
            FontManager.Unload();
            Screen.Unload();
            Raylib.CloseWindow();

            // 終了時に設定を保存
            ConfigManager.SaveAll();
        }

        // フレームの時間を記録する(1秒ごとに一番大きい値を画面用に入れ替える)
        private void MeasureFrame(float dt)
        {
            float ms = dt * 1000f;
            frameMaxMs = Math.Max(frameMaxMs, ms);
            frameWindowTimer += dt;

            // カクッとしたら、TaikoSim の後ろに出ている黒い画面(コンソール)にも書いておく
            if (ms > SlowFrameMs) Console.WriteLine($"[重さ] {DateTime.Now:HH:mm:ss.fff} 1フレームに {ms:F1} ms かかりました");

            if (frameWindowTimer >= 1f)
            {
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
            SongList.Update();

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

            // 設定パネルが開いているときは、後ろの画面は操作させない
            if (settings.IsOpen)
            {
                settings.Update(dt);
                settingsClosedThisFrame = !settings.IsOpen;
                return;
            }

            Scene? scene = SceneManager.Current;
            if (scene == null) return;

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

            scene.Update(dt);
        }

        // 描画
        private void Draw()
        {
            // ① 1920×1080のキャンバスに描く
            Screen.BeginCanvas();
            Raylib.ClearBackground(Color.Black);

            // モード選択からの全画面設定では、後ろのモード画面を描かない
            Gui.SetMouseInputEnabled(!IsSettingsOpen);
            if (!settings.IsFullScreen) SceneManager.Current?.Draw();
            Gui.SetMouseInputEnabled(true);

            if (settings.IsOpen)
            {
                settings.Draw();
            }

            // FPS表示(左上)
            if (ConfigManager.Game.ShowFps && !settings.IsFullScreen)
            {
                // 設定パネルが開いているときはパネルの右隣に出す(ロゴとかぶらないように)
                float fpsX = settings.IsOpen ? SettingsOverlay.PanelWidth + 10 : 10;
                string fpsText = $"{Raylib.GetFPS()} FPS　最大 {frameMaxShownMs:F1} ms";
                FontManager.Draw(fpsText, fpsX, 8, 28, frameMaxShownMs > SlowFrameMs ? Color.Red : Color.Lime);
            }

            Screen.EndCanvas();

            // スクリーンショットが予約されていたら保存(キャンバスを描き終わった直後)
            string? screenshotResult = Screen.SaveScreenshotIfRequested();
            if (screenshotResult != null)
            {
                toastText = screenshotResult;
                toastTimer = 2f; // 2秒表示
            }

            // ② キャンバスをウィンドウの大きさに縮小して表示
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);
            Screen.DrawToWindow();

            // お知らせはウィンドウに直接描く(スクショに写らないように)
            if (toastTimer > 0f)
            {
                DrawToast();
            }

            Raylib.EndDrawing();
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
