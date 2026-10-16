namespace TaikoSim.Configs
{
    // フルスクリーンの方式
    public enum FullscreenType
    {
        Borderless, // 枠なしで画面いっぱい
        Exclusive,  // 本物のフルスクリーン(画面の解像度ごと切り替わる)
    }

    // ゲーム全体の設定
    public class GameConfig
    {
        public const string FileName = "GameConfig.json";

        // 選べる解像度(TaikoNautsと同じ)
        public static readonly (int Width, int Height)[] Resolutions =
        {
            (1920, 1080),
            (1600, 900),
            (1366, 768),
            (1280, 720),
            (1024, 600),
        };

        // オフセットの範囲(ミリ秒)
        public const int OffsetMin = -150;
        public const int OffsetMax = 150;

        // 音オフセット(ミリ秒)：判定のタイミングごとずらす
        // マイナスで早く、プラスで遅く
        public int AudioOffsetMs { get; set; } = 0;

        // 譜面オフセット(ミリ秒)：音符の見た目(表示位置)だけずらす。判定は変わらない
        // マイナスで早く、プラスで遅く
        public int ChartOffsetMs { get; set; } = 0;

        // ウィンドウの大きさ(Resolutions のどれか)
        public int WindowWidth { get; set; } = 1280;
        public int WindowHeight { get; set; } = 720;

        // フルスクリーンにするか
        public bool Fullscreen { get; set; } = false;

        // フルスクリーンの方式
        public FullscreenType FullscreenType { get; set; } = FullscreenType.Borderless;

        // 垂直同期
        public bool VSync { get; set; } = false;

        // 目標フレームレート(0 は無制限)
        public int TargetFps { get; set; } = 60;

        // FPSを表示するか
        public bool ShowFps { get; set; } = true;

        // おかしな値を直す
        public void Fix()
        {
            // オフセットは -150〜150ms
            AudioOffsetMs = Math.Clamp(AudioOffsetMs, OffsetMin, OffsetMax);
            ChartOffsetMs = Math.Clamp(ChartOffsetMs, OffsetMin, OffsetMax);

            // 一覧に無い解像度なら1280×720に戻す
            if (!Resolutions.Contains((WindowWidth, WindowHeight)))
            {
                WindowWidth = 1280;
                WindowHeight = 720;
            }

            // フレームレートは 0(無制限) か 30〜360
            if (TargetFps < 0) TargetFps = 0;
            if (TargetFps > 0 && TargetFps < 30) TargetFps = 30;
            if (TargetFps > 360) TargetFps = 360;
        }
    }
}