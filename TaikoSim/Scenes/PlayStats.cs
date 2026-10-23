using TaikoSim.Charts;

namespace TaikoSim.Scenes
{
    public enum PlayJudgement { Good, Ok, Bad }

    // ニジイロ配点と通常の魂ゲージ。画像や入力処理には触れない。
    public sealed class PlayStats
    {
        public long Score { get; private set; }
        public long PointsPerGood { get; private set; }

        // スコアランクの境目(OpenTaiko と同じ)。上から 粋(白・銅・銀)、雅(金・桃・紫)、極
        //   極は「全部良で叩いて、連打・風船も見込みどおり叩いたときの点」(100万点より低ければ100万点)
        public long[] ScoreRankBorders { get; } = new long[ScoreRankCount];
        public const int ScoreRankCount = 7;

        // 今のスコアで取れているランク(-1 ならまだ無し。0 が粋の白 … 6 が極)
        public int ScoreRank
        {
            get
            {
                int rank = -1;
                for (int i = 0; i < ScoreRankCount; i++)
                    if (Score >= ScoreRankBorders[i]) rank = i;
                return rank;
            }
        }
        public double Gauge { get; private set; }
        public int Norma { get; private set; } = 80;
        public bool IsClear => Gauge >= Norma;
        public bool IsMax => Gauge >= 100;

        // 1打ごとの加算額と合計。1フレームに複数打あっても、それぞれHUDへ知らせる。
        public event Action<long, long>? ScoreAdded;

        // ゲージの増える量(普通・玄人・達人の分岐ごと。分岐の無い譜面は3つとも同じ)
        //   OpenTaiko と同じく、分岐ごとの音符の数(共通 + その分岐)で計算する
        private readonly double[] goodGain = new double[3], okGain = new double[3], badGain = new double[3];

        public void Reset(Course course)
        {
            Score = 0;
            Gauge = 0;
            PointsPerGood = CalculatePointsPerGood(course);
            long[] borders = { 500_000, 600_000, 700_000, 800_000, 900_000, 950_000, CalculateCeilingScore(course, PointsPerGood) };
            Array.Copy(borders, ScoreRankBorders, ScoreRankCount);
            Norma = course.Level switch
            {
                11 => 88,
                12 => 92,
                >= 13 => 96,
                _ => course.Type switch
                {
                    CourseType.Easy => 60,
                    CourseType.Normal or CourseType.Hard => 70,
                    _ => 80,
                },
            };

            Array.Clear(goodGain);
            Array.Clear(okGain);
            Array.Clear(badGain);

            // ゲージ増加量はOpenTaikoの通常ゲージと同じ。配点方式には連動させない。
            float rate = course.Level <= 7 ? 70.7f : course.Level == 8 ? 70f : 75f;
            float damage = course.Level <= 8 ? 0.625f : 2f;
            for (int b = 0; b < 3; b++)
            {
                BranchType branch = (BranchType)b;
                int notes = course.Notes.Count(note => IsRegularNote(note) && IsOnPath(note, branch));
                if (notes == 0) continue;

                float gain = (10000f / (notes * (rate / 100f))) / 100f;
                goodGain[b] = TruncateGain(gain);
                okGain[b] = TruncateGain(gain * 0.5f);
                badGain[b] = TruncateGain(-gain * damage);
            }
        }

        // その音符が、branch の分岐をずっと通ったときに出てくる音符か(共通の音符はどの分岐でも出てくる)
        private static bool IsOnPath(Note note, BranchType branch) => note.Branch == null || note.Branch == branch;

        // OpenTaikoのGen4真打方式を参考に、100万点から連打・風船の見込み点を引き、
        // 通常音符数で割って10点単位に切り捨てる(TaikøNauts に合わせた)。曲・難易度ごとに計算する。
        // SCOREINIT/SCOREDIFF/SCOREMODEは旧通常配点用なので、ニジイロ配点には使用しない。
        // 分岐のある譜面は、OpenTaiko と同じく達人譜面を通ったときの音符で計算する。
        public static long CalculatePointsPerGood(Course course)
        {
            int normalNotes = 0;
            long expectedBalloonHits = 0;
            double rollMilliseconds = 0;
            foreach (Note note in course.Notes)
            {
                if (!IsOnPath(note, BranchType.Master)) continue;
                if (IsRegularNote(note)) normalNotes++;
                else if (note.IsRoll) rollMilliseconds += Math.Max(0, note.EndTimeMs - note.TimeMs);
                else if (note.IsBalloon)
                {
                    double duration = Math.Max(0, note.EndTimeMs - note.TimeMs);
                    expectedBalloonHits += (long)Math.Min(Math.Max(0, note.BalloonCount), Math.Floor(16.6 * duration / 1000.0));
                }
            }
            if (normalNotes == 0) return 0;

            double remaining = 1_000_000.0 - expectedBalloonHits * 100.0 - 16.6 * rollMilliseconds / 1000.0 * 100.0;
            // OpenTaiko は10点単位に切り上げだけど、TaikøNauts は切り捨てだった
            // (テスト譜面：ちょうどの値 6120.64点 → TaikøNauts 6120点、OpenTaiko の計算だと 6130点)
            return Math.Max(10L, (long)(Math.Floor((decimal)remaining / normalNotes / 10m) * 10m));
        }

        // 極の境目。OpenTaiko の GetCeilingGen4ShinUchiScore と同じ考え方
        //   1打の点 × 音符の数 + 風船の見込みの打数 × 100 + 連打の見込み(1秒16.6打。10打単位に切り上げ) × 100
        //   100万点より低ければ100万点(1打の点を切り捨てているので、ふつうは100万点になる)
        public static long CalculateCeilingScore(Course course, long pointsPerGood)
        {
            int normalNotes = 0;
            long balloonHits = 0;
            double rollMilliseconds = 0;
            foreach (Note note in course.Notes)
            {
                if (!IsOnPath(note, BranchType.Master)) continue;
                if (IsRegularNote(note)) normalNotes++;
                else if (note.IsRoll) rollMilliseconds += Math.Max(0, note.EndTimeMs - note.TimeMs);
                else if (note.IsBalloon)
                {
                    double duration = Math.Max(0, note.EndTimeMs - note.TimeMs);
                    balloonHits += (long)Math.Min(Math.Max(0, note.BalloonCount), Math.Floor(16.6 * duration / 1000.0));
                }
            }
            long ceiling = pointsPerGood * normalNotes + balloonHits * 100
                + (long)Math.Ceiling(16.6 * rollMilliseconds / 1000.0 / 10.0) * 1000;
            return Math.Max(1_000_000L, ceiling);
        }

        private static bool IsRegularNote(Note note) =>
            note.Type is NoteType.Don or NoteType.Ka or NoteType.BigDon or NoteType.BigKa;

        private static float TruncateGain(float value) => MathF.Truncate(value * 10000f) / 10000f;

        // gaugeBranch … どの分岐の増え方を使うか(分岐の中の音符はその分岐、共通の音符は今の分岐。OpenTaiko と同じ)
        public void ApplyJudge(Note note, PlayJudgement judge, BranchType gaugeBranch = BranchType.Normal)
        {
            if (!IsRegularNote(note)) return;
            int b = (int)gaugeBranch;
            double gain = judge switch
            {
                PlayJudgement.Good => goodGain[b],
                PlayJudgement.Ok => okGain[b],
                _ => badGain[b],
            };
            Gauge = Math.Clamp(Math.Round(Gauge + gain, 5, MidpointRounding.ToEven), 0, 100);
            if (judge == PlayJudgement.Bad) return;

            // 可は半分を10点単位に切り捨て。大音符も片手1打で成立し、配点の倍率は付けない。
            long points = judge == PlayJudgement.Ok ? PointsPerGood / 20 * 10 : PointsPerGood;
            AddScore(points);
        }

        public void ApplyRollHit(Note note)
        {
            // 小・大連打、風船の途中・割れた最後の1打も100点。ゴーゴー倍率なし。
            if (note.IsRoll || note.IsBalloon) AddScore(100);
        }

        private void AddScore(long points)
        {
            if (points <= 0) return;
            Score += points;
            ScoreAdded?.Invoke(points, Score);
        }
    }
}