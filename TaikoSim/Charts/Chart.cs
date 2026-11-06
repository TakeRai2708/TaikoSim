namespace TaikoSim.Charts
{
    // 音符の種類
    public enum NoteType
    {
        Don,       // ドン
        Ka,        // カッ
        BigDon,    // ドン(大)
        BigKa,     // カッ(大)
        Roll,      // 連打
        BigRoll,   // 連打(大)
        Balloon,   // 風船
        Kusudama,  // くすだま
    }

    // 音符の下の文字（口唱歌）。ドン・カッの音符だけ、まわりの音符との間隔で変わる（SeNotes が決める）
    public enum SeKind
    {
        Don,  // ドン
        Do,   // ド
        Ko,   // コ
        Kat,  // カッ
        Ka,   // カ
    }

    // 譜面分岐のコース(数字は OpenTaiko と同じ。小さいほどやさしい)
    //   音符などの Branch が null のときは「共通」(分岐していないところ)
    public enum BranchType
    {
        Normal = 0, // 普通譜面
        Expert = 1, // 玄人譜面
        Master = 2, // 達人譜面
    }

    // 分岐の条件の種類(#BRANCHSTART の1つ目。OpenTaiko の Exam.Type と同じもの)
    public enum BranchConditionType
    {
        None,           // 条件なし(今の分岐のまま)
        Accuracy,       // p  … 精度 (良 + 可×0.5) ÷ (良 + 可 + 不可) × 100
        PercentPerfect, // pp … 良の割合 良 ÷ (良 + 可 + 不可) × 100
        Roll,           // r  … 連打の数(風船を叩いた数も入る)
        BalloonHits,    // rb … 風船を叩いた数
        Score,          // s  … スコア
        JudgeGood,      // jp・d … 良の数
        JudgeOk,        // jg … 可の数
        JudgeBad,       // jb … 不可の数
    }

    // 分岐の条件(例：#BRANCHSTART p,80,90 → 精度が90以上なら達人、80以上なら玄人、それ以外は普通)
    public class BranchCondition
    {
        public BranchConditionType Type { get; set; } = BranchConditionType.None;

        // 大音符(大連打)だけを数えるか(条件の1文字目が大文字。例：P、R。d は最初から大音符だけ)
        public bool BigOnly { get; set; }

        // true なら「未満」で比べる(4つ目に l が書いてあるとき)。false なら「以上」
        public bool Less { get; set; }

        public double Expert { get; set; } // 玄人になる数
        public double Master { get; set; } // 達人になる数
    }

    // #BRANCHSTART 1回分
    public class BranchPoint
    {
        // 分岐を決める時刻(分岐する場所の1小節前の小節線。連打の条件なら連打の終わりのこともある)
        public double JudgeTimeMs { get; set; }

        // 分岐する時刻(#BRANCHSTART が書いてある場所)
        public double BranchTimeMs { get; set; }

        public BranchCondition Condition { get; set; } = new();

        // その分岐(#N・#E・#M の中)に #LEVELHOLD があるか。そこに決まったら、そのあとは分岐が固定される
        public bool[] LevelHold { get; } = new bool[3];
    }

    // 分岐の外に書いてある #SECTION・#LEVELHOLD(書いてある時刻に起きる)
    public enum BranchEventType
    {
        Section,   // #SECTION … 分岐の判定に使う数(良・可・不可・連打)を0に戻す
        LevelHold, // #LEVELHOLD … ここから先は分岐を固定する
    }

    public class BranchEvent
    {
        public double TimeMs { get; set; }
        public BranchEventType Type { get; set; }
    }

    // 難易度(数字は .tja の COURSE: の番号と同じ)
    public enum CourseType
    {
        Easy = 0,    // かんたん
        Normal = 1,  // ふつう
        Hard = 2,    // むずかしい
        Oni = 3,     // おに
        Edit = 4,    // おに(裏)
        Tower = 5,   // タワー
        Dan = 6,     // 段位道場
    }

    // 音符1つ分
    public class Note
    {
        public NoteType Type { get; set; }

        // 音符の下の文字（ドン・カッの音符だけ使う。読み込みのときに SeNotes.Assign が決める）
        public SeKind SeKind { get; set; }

        // 叩くタイミング(曲の頭からのミリ秒)
        public double TimeMs { get; set; }

        // 連打・風船の終わり(それ以外の音符は TimeMs と同じ)
        public double EndTimeMs { get; set; }

        // この音符が来たときの BPM と SCROLL(流れる速さの計算に使う)
        public double Bpm { get; set; }
        public double Scroll { get; set; }

        // 風船を割るのに必要な回数
        public int BalloonCount { get; set; }

        // ゴーゴータイム中か
        public bool IsGoGo { get; set; }

        // どの分岐の音符か(null なら共通)と、何番目の #BRANCHSTART の中か(-1 なら分岐の外)
        public BranchType? Branch { get; set; }
        public int BranchIndex { get; set; } = -1;

        public bool IsRoll => Type == NoteType.Roll || Type == NoteType.BigRoll;
        public bool IsBalloon => Type == NoteType.Balloon || Type == NoteType.Kusudama;
        public bool IsBig => Type == NoteType.BigDon || Type == NoteType.BigKa || Type == NoteType.BigRoll;
    }

    // 小節線1本分
    public class BarLine
    {
        public double TimeMs { get; set; }
        public double Bpm { get; set; }
        public double Scroll { get; set; }
        public bool Visible { get; set; } = true; // #BARLINEOFF の間は false

        // どの分岐の小節線か(null なら共通)と、何番目の #BRANCHSTART の中か(-1 なら分岐の外)
        public BranchType? Branch { get; set; }
        public int BranchIndex { get; set; } = -1;

        // #BRANCHSTART のあとの最初の小節線(本家では黄色い線になる。見た目は2回目で作る)
        public bool IsBranchStart { get; set; }
    }

    // ゴーゴータイム1回分(#GOGOSTART 〜 #GOGOEND)
    public class GoGoRange
    {
        public double StartMs { get; set; }
        public double EndMs { get; set; }

        // どの分岐のゴーゴーか(null なら共通)と、何番目の #BRANCHSTART の中か(-1 なら分岐の外)
        public BranchType? Branch { get; set; }
        public int BranchIndex { get; set; } = -1;
    }

    // 1つの難易度の譜面
    public class Course
    {
        public CourseType Type { get; set; } = CourseType.Oni;
        public int Level { get; set; }
        // 通常配点。指定がないときは OpenTaiko の初期値を使う。
        public int ScoreMode { get; set; } = 2;
        public int ScoreInit { get; set; } = 300;
        public int ScoreDiff { get; set; } = 120;
        // 風船を割る回数(BALLOON: または BALLOONNOR: が普通譜面と共通、BALLOONEXP: が玄人、BALLOONMAS: が達人)
        public List<int> Balloons { get; set; } = new();
        public List<int> BalloonsExpert { get; set; } = new();
        public List<int> BalloonsMaster { get; set; } = new();
        public List<Note> Notes { get; } = new();
        public List<BarLine> BarLines { get; } = new();

        // ゴーゴータイムの時間(時間の順)
        public List<GoGoRange> GoGoRanges { get; } = new();

        // 譜面分岐(#BRANCHSTART の順番)。分岐の無い譜面なら空
        public List<BranchPoint> BranchPoints { get; } = new();

        // 分岐の外の #SECTION・#LEVELHOLD(書いてある順)
        public List<BranchEvent> BranchEvents { get; } = new();

        public bool HasBranch => BranchPoints.Count > 0;

        // #END の時間(最後の小節の終わり。分岐の途中で終わっていたら一番長い分岐の終わり)
        public double EndTimeMs { get; set; }

        // その時間がゴーゴータイム中か
        //   isActive … 今の分岐で使うゴーゴーかを調べる関数(null なら全部使う。分岐の無い譜面はこれで同じ)
        public bool IsGoGoAt(double timeMs, Func<GoGoRange, bool>? isActive = null)
        {
            foreach (GoGoRange range in GoGoRanges)
            {
                if (isActive != null && !isActive(range)) continue;
                if (timeMs >= range.StartMs && timeMs < range.EndMs) return true;
            }
            return false;
        }
    }

    // .tja 1ファイル分
    public class Chart
    {
        public string FilePath { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Wave { get; set; } = "";     // 曲のファイル名
        public double Bpm { get; set; } = 120;     // 最初のBPM
        public double OffsetSec { get; set; } = 0; // 譜面と曲のズレ(秒)
        public List<Course> Courses { get; } = new();

        // 遊ぶ難易度を選ぶ(おに → 裏 → むずかしい → ふつう → かんたん → タワー → 段位 の順で探す)
        public Course? GetPreferredCourse()
        {
            CourseType[] order =
            {
                CourseType.Oni, CourseType.Edit, CourseType.Hard, CourseType.Normal,
                CourseType.Easy, CourseType.Tower, CourseType.Dan,
            };

            foreach (CourseType type in order)
            {
                Course? course = Courses.FirstOrDefault(c => c.Type == type);
                if (course != null) return course;
            }
            return null;
        }

        // 難易度の表示名
        public static string GetCourseName(CourseType type) => type switch
        {
            CourseType.Easy => "かんたん",
            CourseType.Normal => "ふつう",
            CourseType.Hard => "むずかしい",
            CourseType.Oni => "おに",
            CourseType.Edit => "おに（裏）",
            CourseType.Tower => "タワー",
            CourseType.Dan => "段位道場",
            _ => type.ToString(),
        };
    }
}