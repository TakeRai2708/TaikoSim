namespace TaikoSim.Configs
{
    // 音量の設定
    public class SoundConfig
    {
        public const string FileName = "SoundConfig.json";

        // すべて 0〜100
        public int MasterVolume { get; set; } = 50; // 全体(他の全部にかかる)
        public int SeVolume { get; set; } = 50;     // 効果音(ドン・カッなど)
        public int MusicVolume { get; set; } = 50;  // 演奏中の曲
        public int BgmVolume { get; set; } = 50;    // 選曲画面などのBGM
        public int VoiceVolume { get; set; } = 50;  // ボイス

        // Raylibに渡す実際の音量(0.0〜1.0) = Master × 各音量
        // 例: Master 50, SE 80 → 0.5 × 0.8 = 0.4
        public float GetFinalVolume(int volume)
        {
            return (MasterVolume / 100f) * (volume / 100f);
        }

        // おかしな値を直す
        public void Fix()
        {
            MasterVolume = Math.Clamp(MasterVolume, 0, 100);
            SeVolume = Math.Clamp(SeVolume, 0, 100);
            MusicVolume = Math.Clamp(MusicVolume, 0, 100);
            BgmVolume = Math.Clamp(BgmVolume, 0, 100);
            VoiceVolume = Math.Clamp(VoiceVolume, 0, 100);
        }
    }
}