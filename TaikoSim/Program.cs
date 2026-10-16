using Raylib_cs;
using TaikoSim.Configs;
using TaikoSim.Graphics;

namespace TaikoSim
{
    internal class Program
    {
        static void Main(string[] args)
        {
            // 設定を全部読み込む(無ければ自動で作られる)
            ConfigManager.LoadAll();

            // ウィンドウを作る(横1280, 縦720, タイトル)
            Raylib.InitWindow(1280, 720, "TaikoSim");

            // 1秒間に60回画面を更新する
            Raylib.SetTargetFPS(60);

            // フォントを読み込む(ウィンドウを作った後じゃないとダメ)
            FontManager.Init();

            // テスト用: わざと使用中のキー(F)を右縁に登録しようとして、エラーメッセージを作る
            // Fは左面で使われているので失敗する(設定は変わらない)
            ConfigManager.Key.TryAddDrumKey(DrumPart.RightKa, KeyboardKey.F, out string testMessage);

            // ウィンドウが閉じられるまでずっと繰り返す(Escで閉じる)
            while (!Raylib.WindowShouldClose())
            {
                Raylib.BeginDrawing();               // 描画開始

                Raylib.ClearBackground(Color.Black); // 画面を黒で塗りつぶす
                FontManager.Draw("TaikoSim Start!", 40, 40, 40, Color.White); // 文字を表示(文字, x, y, 大きさ, 色)
                Raylib.DrawFPS(40, 100);             // FPS(1秒の更新回数)を表示

                // 読み込んだ設定を表示(確認用)
                SoundConfig sound = ConfigManager.Sound;
                int y = 150;

                // 音量とオフセット
                y = DrawLine($"Volume  Master:{sound.MasterVolume}  SE:{sound.SeVolume}  Music:{sound.MusicVolume}  BGM:{sound.BgmVolume}  Voice:{sound.VoiceVolume}", y);
                y = DrawLine($"Offset : {ConfigManager.Game.OffsetMs} ms", y);

                // 叩くキー(4か所)
                foreach (var pair in ConfigManager.Key.DrumKeys)
                {
                    y = DrawLine($"{pair.Key} : {string.Join(", ", pair.Value)}", y);
                }

                // 戻るキーとシステムキー
                y = DrawLine($"Back : {KeyConfig.BackKey} (fixed)", y);
                foreach (var pair in ConfigManager.Key.SystemKeys)
                {
                    y = DrawLine($"{pair.Key} : {pair.Value}", y);
                }

                // スキン
                string skinPath = ConfigManager.Skin.GetCurrentSkinPath() ?? "(none)";
                y = DrawLine($"Skin Count : {ConfigManager.Skin.GetSkinList().Count}", y);
                y = DrawLine($"Skin : {skinPath}", y, 20);
                y = DrawLine($"Config : {ConfigManager.ConfigFolder}", y, 20, Color.Gray);

                // 日本語テスト
                y += 10;
                y = DrawLine($"フォント：{FontManager.FontName}", y);
                y = DrawLine("日本語テスト：ドン・カッ／左面・右縁　設定を保存しました。", y);
                DrawLine(testMessage, y, 24, Color.Orange);

                Raylib.EndDrawing();                 // 描画終了
            }

            // フォントの後片付け
            FontManager.Unload();

            // 後片付け
            Raylib.CloseWindow();

            // 終了時に設定を保存
            ConfigManager.SaveAll();
        }

        // 1行表示して、次の行のy座標を返す(白)
        static int DrawLine(string text, int y, int fontSize = 24)
        {
            return DrawLine(text, y, fontSize, Color.White);
        }

        // 1行表示して、次の行のy座標を返す(色指定)
        static int DrawLine(string text, int y, int fontSize, Color color)
        {
            FontManager.Draw(text, 40, y, fontSize, color);
            return y + fontSize + 6;
        }
    }
}