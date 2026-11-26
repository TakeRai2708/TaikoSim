namespace TakeTaikoSimulator.Configs
{
    // フルスクリーンの方式
    public enum FullscreenType
    {
        Borderless, // 枠なしで画面いっぱい
        Exclusive,  // 本物のフルスクリーン(画面の解像度ごと切り替わる)
    }

    // 打鍵の時刻をどう扱うか（60HzはTaikøNauts風の実験モード）。
    public enum InputTimingMode
    {
        HighPrecision,
        Compatible60Hz,
    }

    public enum RecordGroup { Easy, Normal, Hard, Oni, OniAndUra }

    // 演奏オプションの「ランダム」（Charts/NoteOptions.cs）。しない／きまぐれ（21%）／でたらめ（51%）
    public enum NoteRandom { Off, Kimagure, Detarame }

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

        // UI の色（"Dark" ダーク／"Pink" ピンク）。11/26 たけらい：初回セットアップで選ぶ。選ぶまではダーク
        public string UiTheme { get; set; } = "Dark";

        // ウィンドウの大きさ(Resolutions のどれか)
        public int WindowWidth { get; set; } = 1280;
        public int WindowHeight { get; set; } = 720;

        // フルスクリーンにするか
        public bool Fullscreen { get; set; } = false;

        // フルスクリーンの方式（11/8 から使っていない。前の設定ファイルを読めるように残しているだけ。
        //   フルスクリーンはいつも本物のフルスクリーン。Graphics/Screen.cs の ApplyWindowMode）
        public FullscreenType FullscreenType { get; set; } = FullscreenType.Borderless;

        // 描画方式（OpenGL / DirectX 11 / Vulkan。変えたら再起動。Graphics/GraphicsBackend.cs）
        public Graphics.GraphicsBackendType GraphicsBackend { get; set; } = Graphics.GraphicsBackendType.DirectX11;

        // 垂直同期
        public bool VSync { get; set; } = false;

        // 目標フレームレート(0 は無制限)
        public int TargetFps { get; set; } = 60;

        // 打鍵時刻そのまま / 16.67ms刻みにそろえる（初期値は高精度）
        public InputTimingMode InputTiming { get; set; } = InputTimingMode.HighPrecision;

        // 4キー合計の入力を秒間60打ペースに制限（余った分はキューで待つ）
        public bool LimitDrumHitsTo60PerSecond { get; set; } = false;

        // 選曲画面の演奏オプション。初期値は手動、切り替えた状態は保存する。
        public bool AutoPlay { get; set; } = false;

        // 0は譜面どおり。正の値はTJAのSCROLLに掛ける表示倍率(判定時刻は変えない)。
        public float HiSpeed { get; set; } = 0f;

        // オート演奏の連打の速さ(1秒に何打)。0 ならオートは連打・風船を叩かない
        // 範囲は OpenTaiko と同じ 0〜1000。初期値は今までと同じ 20(50msごと)
        // 風船もこの速さで叩く。この速さで終わりまでに割れないときは、終わるギリギリで割れる叩き方(OpenTaiko と同じ)にする
        public int AutoRollPerSecond { get; set; } = 20;
        public const int AutoRollMin = 0;
        public const int AutoRollMax = 1000;

        // オート演奏の良・可・不可の割合(%)。合計が100でなくても、割合として使う(全部0なら全部良)
        //   初期値は今までと同じ全部良
        public int AutoGoodPercent { get; set; } = 100;
        public int AutoOkPercent { get; set; } = 0;
        public int AutoBadPercent { get; set; } = 0;
        public const int AutoPercentMin = 0;
        public const int AutoPercentMax = 100;

        // オートの良・可・不可の出方を、同じ曲・同じ難易度・同じ割合なら毎回同じにするか(false なら毎回ランダム)
        public bool AutoFixedPattern { get; set; } = false;

        // 音色(ドン・カッの音)。HitSounds フォルダの中のフォルダ名。空ならスキンの音(Audio/HitSounds.cs)
        public string HitSound { get; set; } = "";

        // 演奏画面の左下に、良・可・不可・連打数の板を出すか
        public bool ShowScoreboard { get; set; } = true;

        // ===== 演奏オプション（11/12 たけらい。UI/PlayOptionsPanel.cs） =====
        // ドロン：音符の絵を消す（下の文字「ドン・カッ・連打・ふうせん」だけ流れる）
        public bool Doron { get; set; } = false;
        // あべこべ：ドン⇔カッ を全部入れかえる
        public bool Abekobe { get; set; } = false;
        // ランダム：音符ごとに、決まった確率で ドン⇔カッ を入れかえる
        public NoteRandom Random { get; set; } = NoteRandom.Off;
        // 演奏速度：曲そのものの速さ（1.0 がふつう）。選べる値は PlaySpeeds（A-Style のアイコンがある値だけ）
        //   音の高さも一緒に変わる。段位道場・2人プレイ・特訓モードでは使わない（PlayScene）
        public float PlaySpeed { get; set; } = 1f;
        public static readonly float[] PlaySpeeds = { 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1.0f, 1.1f, 1.2f, 1.3f, 1.4f, 1.5f, 2.0f };
        // ボイス：演奏中のコンボの声（50コンボ…）を鳴らすか
        public bool ComboVoice { get; set; } = true;

        // 王冠・スコアランクの獲得数で表示する難易度。曲ごとの記録はそのまま。
        public RecordGroup RecordGroup { get; set; } = RecordGroup.OniAndUra;

        // デバッグ記録(CSV)を取るか。ON の間、Logs/Debug_日付_時間/ に frames.csv・events.csv・input.csv を書く
        //   (前の「入力診断CSV保存」はこれに入った。入力の記録は input.csv)
        public bool DebugLog { get; set; } = false;

        // 重さの内訳(どこに何 ms かかっているか)を画面の右上に出すか
        public bool ShowPerfOverlay { get; set; } = false;

        // FPSを表示するか
        public bool ShowFps { get; set; } = true;

        // マウスを置いた所の座標(1920×1080)を出すか。素材の位置を自分で直すとき用(11/11)
        public bool ShowCursorPosition { get; set; } = false;

        // 設定の「デバッグ」カテゴリを出すか(11/26)。ふつうは隠しておいて、裏コマンドで出す：
        //   設定を開いているときに キーボードで d・e・b・u・g と打つ か、上の「設定」の文字を7回クリック。もう一度やると隠れる
        public bool DebugMenu { get; set; } = false;

        // 演奏中のデバッグ用のキー(F3 表示・F6 入力判定・F7 60打制限・F8 入力診断・F10 見た目)を使うか(11/26)
        //   段位道場の F3(強制終了)は、これと関係なく使える
        public bool DebugPlayKeys { get; set; } = true;

        // Discord の「プレイ中」に、今なにをしているかを出すか(11/11)
        public bool DiscordPresence { get; set; } = true;

        // Discord に曲名を出さないか(ON なら難易度だけ出す)
        public bool DiscordHideSongTitle { get; set; } = false;

        // おかしな値を直す
        public void Fix()
        {
            if (!Enum.IsDefined(RecordGroup)) RecordGroup = RecordGroup.OniAndUra;
            if (!Enum.IsDefined(GraphicsBackend)) GraphicsBackend = Graphics.GraphicsBackendType.DirectX11;
            // 未知の値なら高精度に戻す（JSONを直接編集した場合など）。
            if (!Enum.IsDefined(InputTiming)) InputTiming = InputTimingMode.HighPrecision;
            if (!float.IsFinite(HiSpeed) || HiSpeed < 0f) HiSpeed = 0f;
            HiSpeed = Math.Min(HiSpeed, 100f);
            if (!Enum.IsDefined(Random)) Random = NoteRandom.Off;
            // 演奏速度は、選べる値のうち一番近いものにそろえる
            PlaySpeed = float.IsFinite(PlaySpeed) ? PlaySpeeds.MinBy(s => Math.Abs(s - PlaySpeed)) : 1f;
            AutoRollPerSecond = Math.Clamp(AutoRollPerSecond, AutoRollMin, AutoRollMax);
            AutoGoodPercent = Math.Clamp(AutoGoodPercent, AutoPercentMin, AutoPercentMax);
            AutoOkPercent = Math.Clamp(AutoOkPercent, AutoPercentMin, AutoPercentMax);
            AutoBadPercent = Math.Clamp(AutoBadPercent, AutoPercentMin, AutoPercentMax);

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
