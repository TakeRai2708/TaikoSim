namespace TaikoSim.Charts
{
    // 段位道場のデータ。形式は TaikøNauts に合わせる(TAIKONAUTS-SPEC.md 参照)。
    //   OpenTaiko は1つの .tja に #NEXTSONG で全曲を詰めるが、TaikøNauts は
    //   課題曲を別々のファイルに置いて dan.json でまとめる方式。

    // 合格条件の種類。
    //   ★「以上」か「未満」かは dan.json に書かれていない。この種類で決まる。
    //     間違えると合否が逆になるので IsLess() を必ず通すこと。
    public enum DanExamType
    {
        Great,     // 良の数        以上
        Good,      // 可の数        未満
        Miss,      // 不可の数      未満
        Roll,      // 連打数        以上
        Hit,       // たたけた数    以上
        Score,     // スコア        以上
        MaxCombo,  // 最大コンボ数  以上
    }

    public static class DanExamTypes
    {
        // 「未満」が条件になるもの(可・不可は少ないほど良い)
        public static bool IsLess(DanExamType type) => type is DanExamType.Good or DanExamType.Miss;

        public static string Label(DanExamType type) => type switch
        {
            DanExamType.Great => "良",
            DanExamType.Good => "可",
            DanExamType.Miss => "不可",
            DanExamType.Roll => "連打",
            DanExamType.Hit => "たたけた数",
            DanExamType.Score => "スコア",
            DanExamType.MaxCombo => "最大コンボ",
            _ => type.ToString(),
        };

        // dan.json の "type" の文字列から。知らない種類は null(その条件は無視する)
        public static DanExamType? Parse(string? text) => (text ?? "").Trim().ToLowerInvariant() switch
        {
            "great" => DanExamType.Great,
            "good" => DanExamType.Good,
            "miss" => DanExamType.Miss,
            "roll" => DanExamType.Roll,
            "hit" => DanExamType.Hit,
            "score" => DanExamType.Score,
            "maxcombo" => DanExamType.MaxCombo,
            _ => null,
        };
    }

    // 赤合格・金合格のボーダー
    public readonly record struct DanThreshold(long Red, long Gold);

    // 合格条件1つ
    public sealed class DanExam
    {
        public DanExamType Type { get; init; }

        /// <summary>
        /// 長さ1 なら「全体条件」(課題曲ぜんぶの合計で見る)。
        /// 曲数ぶんの長さなら「曲ごと条件」(その曲だけで見る)。
        /// </summary>
        public DanThreshold[] Thresholds { get; init; } = Array.Empty<DanThreshold>();

        public bool PerSong => Thresholds.Length > 1;

        public DanThreshold For(int songIndex) =>
            Thresholds.Length == 0 ? default
            : Thresholds[Math.Clamp(songIndex, 0, Thresholds.Length - 1)];

        /// <summary>
        /// その条件に「金のボーダー」があるか。
        /// 「未満」系(可・不可)で gold が 0 だと「0未満」になって達成不可能なので、
        /// conditionGauge の gold:0 と同じく「金の条件なし」とみなす。
        /// たけらいの実ファイルでも 初段の不可が red:36 / gold:0 になっている。
        /// </summary>
        public bool HasGold(int songIndex = 0)
        {
            DanThreshold t = For(songIndex);
            return !IsLess || t.Gold > 0;
        }

        public bool IsLess => DanExamTypes.IsLess(Type);
        public string Label => DanExamTypes.Label(Type);
    }

    // 課題曲1つ
    public sealed class DanSong
    {
        public string RelativePath { get; set; } = "";  // dan.json からの相対パス
        public string FullPath { get; set; } = "";      // 解決したあとの絶対パス
        public CourseType Course { get; set; } = CourseType.Oni;
        public string Genre { get; set; } = "";
        public bool IsHidden { get; set; }              // 段位選択画面で曲名を伏せるか

        // 読んだあとに入る(表示用)
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public int Level { get; set; }
    }

    // 曲1つのクリア種別。上ほど高い(高い記録は低い条件も満たす)
    public enum SongClearType { None = 0, Cleared = 1, FullCombo = 2, AllPerfect = 3 }

    // 段位の合否
    public enum DanResultType { None = 0, Failed = 1, Red = 2, Gold = 3 }

    // 段位のクリア記録。赤/金 と フルコン/全良 の2軸で持つ
    //   ドキュメントいわく「より高いクリア種別の記録は、より低いクリア条件も満たす」。
    //   RedFullCombo と GoldClear のどちらが上かは書かれていないので、
    //   TaikoSim は「金かどうか」と「コンボの種別」の両方を満たしていれば合格、とする。
    public readonly record struct DanClearRecord(bool Gold, SongClearType Combo)
    {
        public bool Satisfies(DanClearRecord need) => (Gold || !need.Gold) && Combo >= need.Combo;
    }

    public static class DanClearTypes
    {
        // requirements.json の songClear 用
        public static SongClearType ParseSong(string? text) => (text ?? "").Trim().ToLowerInvariant() switch
        {
            "cleared" => SongClearType.Cleared,
            "fullcombo" => SongClearType.FullCombo,
            "allperfect" => SongClearType.AllPerfect,
            _ => SongClearType.None,
        };

        // requirements.json の danClear 用。赤/金 と コンボ種別 に分解する
        public static DanClearRecord? ParseDan(string? text) => (text ?? "").Trim().ToLowerInvariant() switch
        {
            "redclear" => new DanClearRecord(false, SongClearType.Cleared),
            "goldclear" => new DanClearRecord(true, SongClearType.Cleared),
            "redfullcombo" => new DanClearRecord(false, SongClearType.FullCombo),
            "goldfullcombo" => new DanClearRecord(true, SongClearType.FullCombo),
            "redallperfect" => new DanClearRecord(false, SongClearType.AllPerfect),
            "goldallperfect" => new DanClearRecord(true, SongClearType.AllPerfect),
            _ => null,
        };
    }

    // 解禁条件(requirements.json)
    public enum DanRequirementType { All, Any, Not, DanClear, SongClear, SongScore, SongRank }

    public sealed class DanRequirement
    {
        public DanRequirementType Type { get; set; }
        public List<DanRequirement> Children { get; } = new();   // all / any / not の中身

        public string Path { get; set; } = "";                   // requirements.json からの相対パス
        public string FullPath { get; set; } = "";
        public CourseType Course { get; set; } = CourseType.Oni;
        public SongClearType SongClear { get; set; }
        public DanClearRecord DanClear { get; set; }
        public long Score { get; set; }
        public int Rank { get; set; }

        /// <summary>読めなかった・解決できなかった条件。
        /// ドキュメントいわく、この場合は「ロックせず、エラー通知を出す」</summary>
        public string Error { get; set; } = "";
    }

    // 曲間演出(ChangeEffect/[名前]/Config.json)
    public sealed class DanChangeEffect
    {
        public string FolderPath { get; set; } = "";
        public int TransitionIndex { get; set; }          // 0 なら1曲目と2曲目の間、1 なら2曲目と3曲目の間
        public bool PlayDefaultScreen { get; set; } = true;

        // playDefaultScreen が true でも使われる3つ
        public int EffectStartMs { get; set; }
        public int ConditionPanelChangeMs { get; set; }
        public int TitleFadeStartMs { get; set; }

        // カスタム演出(playDefaultScreen: false)のときだけ使う4つ
        public int TitleFadeInDurationMs { get; set; }
        public int TitleDisplayDurationMs { get; set; }
        public int PreviousTitleHideMs { get; set; }
        public int NextNoteHitMs { get; set; }
    }

    // 段位1つ(dan.json 1つぶん)
    public sealed class DanCourse
    {
        public string JsonPath { get; set; } = "";       // dan.json の絶対パス
        public string FolderPath { get; set; } = "";     // dan.json の入っているフォルダ

        public string Title { get; set; } = "";
        public int DanIndex { get; set; }                // 並び順

        // 画像(どれも dan.json からの相対パス。無ければ空)
        public string PlatePath { get; set; } = "";
        public string PanelSidePath { get; set; } = "";
        public string TitlePlatePath { get; set; } = "";
        public string MiniPlatePath { get; set; } = "";
        public string MiniPlateText { get; set; } = "";

        // 昇段演出のテキスト画像。赤/金が無ければ共通のものを使う
        public string[] ShodanTextPaths { get; set; } = Array.Empty<string>();
        public string[] ShodanRedTextPaths { get; set; } = Array.Empty<string>();
        public string[] ShodanGoldTextPaths { get; set; } = Array.Empty<string>();
        public string[] ShodanTextFor(bool gold)
        {
            string[] special = gold ? ShodanGoldTextPaths : ShodanRedTextPaths;
            return special.Length > 0 ? special : ShodanTextPaths;
        }

        public List<DanSong> Songs { get; } = new();

        /// <summary>合格に必要な魂ゲージ(%)。
        /// ドキュメントは表の見出しが「合格ゲージ条件」なのに本文が「初期値」で食い違っている。
        /// 実ファイル(初段 red:97/gold:0、十段 red:100/gold:100)から見て、合格ボーダーとして扱う。
        /// gold が 0 のときは「金のゲージ条件なし」とみなす(0%未満は達成不可能なため)。</summary>
        public DanThreshold Gauge { get; set; }
        public bool HasGoldGauge => Gauge.Gold > 0;

        public List<DanExam> Exams { get; } = new();
        public DanRequirement? Requirement { get; set; }          // requirements.json(任意)
        public List<DanChangeEffect> ChangeEffects { get; } = new();

        /// <summary>読み込みに失敗した理由。空でなければ段位選択画面に出す</summary>
        public string Error { get; set; } = "";

        public bool Playable => Error.Length == 0 && Songs.Count > 0;
    }

    // 段位カテゴリ(dan.def の入っているフォルダ)
    public sealed class DanCategory
    {
        public string FolderPath { get; set; } = "";
        public string Title { get; set; } = "";
        /// <summary>dan.def の #DISABLEDCHEAT:true。
        /// true なら、このカテゴリの段位では練習向けの機能(オート演奏など)を使わせない</summary>
        public bool DisabledCheat { get; set; }
        public List<DanCourse> Courses { get; } = new();
    }
}
