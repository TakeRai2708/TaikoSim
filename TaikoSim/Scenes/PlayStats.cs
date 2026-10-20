using TaikoSim.Charts;

namespace TaikoSim.Scenes
{
    public enum PlayJudgement { Good, Ok, Bad }

    // ニジイロ配点と通常の魂ゲージ。画像や入力処理には触れない。
    public sealed class PlayStats
    {
        public long Score { get; private set; }
        public long PointsPerGood { get; private set; }
        public double Gauge { get; private set; }
        public int Norma { get; private set; } = 80;
        public bool IsClear => Gauge >= Norma;
        public bool IsMax => Gauge >= 100;

        // 1打ごとの加算額と合計。1フレームに複数打あっても、それぞれHUDへ知らせる。
        public event Action<long, long>? ScoreAdded;
        private double goodGain, okGain, badGain;

        public void Reset(Course course)
        {
            Score = 0;
            Gauge = 0;
            PointsPerGood = CalculatePointsPerGood(course);
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

            int notes = course.Notes.Count(IsRegularNote);
            goodGain = okGain = badGain = 0;
            if (notes == 0) return;

            // ゲージ増加量はOpenTaikoの通常ゲージと同じ。配点方式には連動させない。
            float rate = course.Level <= 7 ? 70.7f : course.Level == 8 ? 70f : 75f;
            float damage = course.Level <= 8 ? 0.625f : 2f;
            float gain = (10000f / (notes * (rate / 100f))) / 100f;
            goodGain = TruncateGain(gain);
            okGain = TruncateGain(gain * 0.5f);
            badGain = TruncateGain(-gain * damage);
        }

        // OpenTaikoのGen4真打方式を参考に、100万点から連打・風船の見込み点を引き、
        // 通常音符数で割って10点単位に切り上げる。曲・難易度ごとに計算する。
        // SCOREINIT/SCOREDIFF/SCOREMODEは旧通常配点用なので、ニジイロ配点には使用しない。
        public static long CalculatePointsPerGood(Course course)
        {
            int normalNotes = 0;
            long expectedBalloonHits = 0;
            double rollMilliseconds = 0;
            foreach (Note note in course.Notes)
            {
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
            return Math.Max(10L, (long)(Math.Ceiling((decimal)remaining / normalNotes / 10m) * 10m));
        }

        private static bool IsRegularNote(Note note) =>
            note.Type is NoteType.Don or NoteType.Ka or NoteType.BigDon or NoteType.BigKa;

        private static float TruncateGain(float value) => MathF.Truncate(value * 10000f) / 10000f;

        public void ApplyJudge(Note note, PlayJudgement judge)
        {
            if (!IsRegularNote(note)) return;
            double gain = judge switch
            {
                PlayJudgement.Good => goodGain,
                PlayJudgement.Ok => okGain,
                _ => badGain,
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
