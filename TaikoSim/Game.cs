using Raylib_cs;
using TaikoSim.Configs;
using TaikoSim.Graphics;
using TaikoSim.Input;
using TaikoSim.Overlays;
using TaikoSim.Scenes;

namespace TaikoSim
{
    // ゲーム全体のメインループ
    public class Game
    {
        // 設定パネル(オーバーレイ)
        private readonly SettingsOverlay settings = new();

        // 右上に少しだけ出すお知らせ(スクショ保存したよ、とか)
        private string toastText = "";
        private float toastTimer = 0f;

        // true にするとアプリが終了する(設定パネルの⏻ボタンで使う)
        public static bool ShouldExit { get; set; } = false;

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
            SkinManager.Init();           // スキンの画像を読む準備

            // 最初の画面(叩くキーが決まっていなければ初回起動セットアップ)
            if (ConfigManager.Key.NeedsSetup())
            {
                SceneManager.ChangeScene(new SetupScene());
            }
            else
            {
                SceneManager.ChangeScene(new HomeScene());
            }

            // ウィンドウの×が押されるか、ShouldExit が true になるまで繰り返す
            while (!Raylib.WindowShouldClose() && !ShouldExit)
            {
                float dt = Raylib.GetFrameTime(); // 前のフレームからの経過時間(秒)

                Screen.ApplyIfRequested(); // 設定でウィンドウの大きさなどが変わっていたら反映
                SceneManager.ApplyChange();
                Update(dt);
                Draw();
            }

            // 後片付け
            SceneManager.Current?.Exit();
            SkinManager.Unload();
            FontManager.Unload();
            Screen.Unload();
            Raylib.CloseWindow();

            // 終了時に設定を保存
            ConfigManager.SaveAll();
        }

        // 更新(入力の処理など)
        private void Update(float dt)
        {
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
            }

            scene.Update(dt);
        }

        // 描画
        private void Draw()
        {
            // ① 1920×1080のキャンバスに描く
            Screen.BeginCanvas();
            Raylib.ClearBackground(Color.Black);

            SceneManager.Current?.Draw();

            if (settings.IsOpen)
            {
                settings.Draw();
            }

            // FPS表示(左上)
            if (ConfigManager.Game.ShowFps)
            {
                // 設定パネルが開いているときはパネルの右隣に出す(ロゴとかぶらないように)
                float fpsX = settings.IsOpen ? SettingsOverlay.PanelWidth + 10 : 10;
                FontManager.Draw($"{Raylib.GetFPS()} FPS", fpsX, 8, 28, Color.Lime);
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