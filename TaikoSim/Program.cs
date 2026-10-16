using Raylib_cs;
using TaikoSim.Configs;

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

            // ウィンドウが閉じられるまでずっと繰り返す(Escで閉じる)
            while (!Raylib.WindowShouldClose())
            {
                Raylib.BeginDrawing();               // 描画開始

                Raylib.ClearBackground(Color.Black); // 画面を黒で塗りつぶす
                Raylib.DrawText("TaikoSim Start!", 40, 40, 40, Color.White); // 文字を表示(x, y, 大きさ, 色)
                Raylib.DrawFPS(40, 100);             // FPS(1秒の更新回数)を表示

                // 読み込んだ設定を表示(確認用)
                string skinPath = ConfigManager.Skin.GetCurrentSkinPath() ?? "(none)";

                Raylib.DrawText($"BGM Volume : {ConfigManager.Sound.BgmVolume}", 40, 160, 24, Color.White);
                Raylib.DrawText($"SE Volume  : {ConfigManager.Sound.SeVolume}", 40, 190, 24, Color.White);
                Raylib.DrawText($"Offset     : {ConfigManager.Game.OffsetMs} ms", 40, 220, 24, Color.White);
                Raylib.DrawText($"Don Keys   : {string.Join(", ", ConfigManager.Key.Don)}", 40, 250, 24, Color.White);
                Raylib.DrawText($"Ka Keys    : {string.Join(", ", ConfigManager.Key.Ka)}", 40, 280, 24, Color.White);
                Raylib.DrawText($"Back Key   : {KeyConfig.BackKey} (fixed)", 40, 310, 24, Color.White);

                // システムキーを全部表示(増えても自動で並ぶ)
                int y = 340;
                foreach (var pair in ConfigManager.Key.SystemKeys)
                {
                    Raylib.DrawText($"{pair.Key} : {pair.Value}", 40, y, 24, Color.White);
                    y += 30;
                }

                Raylib.DrawText($"Skin Count : {ConfigManager.Skin.GetSkinList().Count}", 40, y, 24, Color.White);
                Raylib.DrawText($"Skin       : {skinPath}", 40, y + 30, 20, Color.White);
                Raylib.DrawText($"Config     : {ConfigManager.ConfigFolder}", 40, y + 60, 20, Color.Gray);

                Raylib.EndDrawing();                 // 描画終了
            }

            // 後片付け
            Raylib.CloseWindow();

            // 終了時に設定を保存
            ConfigManager.SaveAll();
        }
    }
}