using TaikoSim.Configs;
using TaikoSim.Graphics;

namespace TaikoSim.Audio
{
    // メニュー(モード選択・選曲画面)で鳴らす音。スキンのドン・カッをそのまま使う
    //   ドン … 決定、カッ … カーソルを動かす
    // スキンを切り替えたとき・音を準備し直したときは読み直す
    public static class MenuSounds
    {
        private const string DonFile = "Sound/Taiko/0.Taiko/don.ogg";
        private const string KaFile = "Sound/Taiko/0.Taiko/ka.ogg";

        private static SoundEffect? don, ka;
        private static int loadedSkin = -1;
        private static int loadedAudio = -1;

        public static void Don()
        {
            Ensure();
            don?.Play(Volume);
        }

        public static void Ka()
        {
            Ensure();
            ka?.Play(Volume);
        }

        private static float Volume => (float)ConfigManager.Sound.GetFinalVolume(ConfigManager.Sound.SeVolume);

        private static void Ensure()
        {
            if (loadedSkin == SkinManager.Generation && loadedAudio == AudioManager.Generation) return;

            // 音を準備し直したあとの古い音は、もう使えない(片付けもしない)
            if (loadedAudio == AudioManager.Generation)
            {
                don?.Dispose();
                ka?.Dispose();
            }
            don = ka = null;
            loadedSkin = SkinManager.Generation;
            loadedAudio = AudioManager.Generation;

            string? skin = SkinManager.CurrentPath;
            if (skin == null || !AudioManager.IsAvailable) return;
            don = SoundEffect.Load(Path.Combine(skin, DonFile), out _);
            ka = SoundEffect.Load(Path.Combine(skin, KaFile), out _);
        }
    }
}
