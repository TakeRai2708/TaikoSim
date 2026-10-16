namespace TaikoSim.Configs
{
    // ゲーム全体の設定
    public class GameConfig
    {
        public const string FileName = "GameConfig.json";

        // 譜面と音のズレ補正(ミリ秒)。プラスにすると音符が遅れて来る
        public int OffsetMs { get; set; } = 0;

        // おかしな値を直す(±2秒まで)
        public void Fix()
        {
            OffsetMs = Math.Clamp(OffsetMs, -2000, 2000);
        }
    }
}