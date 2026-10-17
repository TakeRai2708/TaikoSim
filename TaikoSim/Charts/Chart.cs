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
    }

    // 1つの難易度の譜面
    public class Course
    {
        public CourseType Type { get; set; } = CourseType.Oni;
        public int Level { get; set; }
        public List<int> Balloons { get; set; } = new();
        public List<Note> Notes { get; } = new();
        public List<BarLine> BarLines { get; } = new();
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