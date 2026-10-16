using Raylib_cs;
using TaikoSim.Configs;
using TaikoSim.Graphics;

namespace TaikoSim.Scenes
{
    // 仮のホーム画面(今は設定の確認表示だけ)
    public class HomeScene : Scene
    {
        // ホームでは Esc を押しても何もしない
        public override void OnBack() { }

        public override void Draw()
        {
            FontManager.Draw("TaikoSim", 80, 60, 72, Color.White);
            FontManager.Draw("F1：設定を開く", 80, 150, 32, Color.SkyBlue);

            SoundConfig sound = ConfigManager.Sound;
            GameConfig game = ConfigManager.Game;
            int y = 230;

            // 音量・オフセット・解像度
            y = Line($"音量　Master:{sound.MasterVolume}　SE:{sound.SeVolume}　Music:{sound.MusicVolume}　BGM:{sound.BgmVolume}　Voice:{sound.VoiceVolume}", y);
            y = Line($"オフセット：{game.OffsetMs} ms", y);
            y = Line($"解像度：{game.WindowWidth}×{game.WindowHeight}", y);

            // 叩くキー
            foreach (var pair in ConfigManager.Key.DrumKeys)
            {
                y = Line($"{KeyConfig.GetDrumPartName(pair.Key)}：{string.Join(", ", pair.Value)}", y);
            }

            // 戻るキーとシステムキー
            y = Line($"戻る：{KeyConfig.BackKey}（固定）", y);
            foreach (var pair in ConfigManager.Key.SystemKeys)
            {
                string keyName = pair.Value == KeyboardKey.Null ? "未割り当て" : pair.Value.ToString();
                y = Line($"{KeyConfig.GetSystemActionName(pair.Key)}：{keyName}", y);
            }

            // スキン・フォント・設定の場所
            string skinPath = ConfigManager.Skin.GetCurrentSkinPath() ?? "なし";
            y = Line($"スキン数：{ConfigManager.Skin.GetSkinList().Count}　使用中：{skinPath}", y);
            y = Line($"フォント：{FontManager.FontName}", y);
            Line($"設定の場所：{ConfigManager.ConfigFolder}", y, 24, Color.Gray);
        }

        // 1行表示して、次の行のy座標を返す(白)
        private static int Line(string text, int y, int size = 32)
        {
            return Line(text, y, size, Color.White);
        }

        // 1行表示して、次の行のy座標を返す(色指定)
        private static int Line(string text, int y, int size, Color color)
        {
            FontManager.Draw(text, 80, y, size, color);
            return y + size + 10;
        }
    }
}