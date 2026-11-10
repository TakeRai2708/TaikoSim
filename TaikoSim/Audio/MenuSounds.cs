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
        private const string SkipFile = "Sound/SongSelect/Skip.ogg";     // 選曲画面で7曲まとめて飛ぶとき(TaikøNauts の A-Style と同じ場所)
        private const string OptionFile = "Sound/SongSelect/Option.ogg"; // 演奏オプションの板を開いたとき
        private const string CancelFile = "Sound/Cancel.ogg";          // 曲・難易度の画面から戻る音
        private const string UraFlipFile = "Sound/SongSelect/UraFlip.ogg"; // おに ⇔ 裏おにを切り替えたとき

        private static SoundEffect? don, ka, skip, option, uraFlip, cancel;
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

        // 戻る音。素材の無いスキンはカッで知らせる。
        public static void Cancel()
        {
            Ensure();
            (cancel ?? ka)?.Play(Volume);
        }

        // 7曲まとめて飛ぶ音(スキンに無ければカッ)
        public static void Skip()
        {
            Ensure();
            (skip ?? ka)?.Play(Volume);
        }

        // オプションの板を開く音(スキンに無ければドン)
        public static void Option()
        {
            Ensure();
            (option ?? don)?.Play(Volume);
        }

        // おに ⇔ 裏おにの切り替えの音(スキンに無ければドン)
        public static void UraFlip()
        {
            Ensure();
            (uraFlip ?? don)?.Play(Volume);
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
                skip?.Dispose();
                option?.Dispose();
                uraFlip?.Dispose();
                cancel?.Dispose();
            }
            don = ka = skip = option = uraFlip = cancel = null;
            loadedSkin = SkinManager.Generation;
            loadedAudio = AudioManager.Generation;

            string? skin = SkinManager.CurrentPath;
            if (skin == null || !AudioManager.IsAvailable) return;
            don = SoundEffect.Load(Path.Combine(skin, DonFile), out _);
            ka = SoundEffect.Load(Path.Combine(skin, KaFile), out _);
            skip = SoundEffect.Load(Path.Combine(skin, SkipFile), out _);
            option = SoundEffect.Load(Path.Combine(skin, OptionFile), out _);
            uraFlip = SoundEffect.Load(Path.Combine(skin, UraFlipFile), out _);
            cancel = SoundEffect.Load(Path.Combine(skin, CancelFile), out _);
        }
    }
}

