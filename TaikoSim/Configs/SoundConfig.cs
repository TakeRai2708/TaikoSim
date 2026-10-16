namespace TaikoSim.Configs
{
    // 音量の設定
    public class SoundConfig
    {
        public const string FileName = "SoundConfig.json";

        public int BgmVolume { get; set; } = 100; // BGM音量(0〜100)
        public int SeVolume { get; set; } = 100;  // 効果音音量(0〜100)

        // おかしな値を直す
        public void Fix()
        {
            BgmVolume = Math.Clamp(BgmVolume, 0, 100);
            SeVolume = Math.Clamp(SeVolume, 0, 100);
        }
    }
}