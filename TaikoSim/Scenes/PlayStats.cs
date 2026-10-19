using TaikoSim.Charts;

namespace TaikoSim.Scenes
{
    public enum PlayJudgement { Good, Ok, Bad }

    // 得点と通常の魂ゲージを計算する係。画像や入力処理には触れない。
    public sealed class PlayStats
    {
        public long Score { get; private set; }
        public double Gauge { get; private set; }
        public int Norma { get; private set; } = 80;
        public bool IsClear => Gauge >= Norma;
        public bool IsMax => Gauge >= 100;

        private int scoreMode = 2;
        private long scoreInit = 300, scoreDiff = 120;
        private double goodGain, okGain, badGain;
        private long pendingBonus;
        private float bonusSeconds;

        public void Reset(Course course)
        {
            Score = 0;
            Gauge = 0;
            pendingBonus = 0;
            bonusSeconds = 0;
            scoreMode = course.ScoreMode is >= 0 and <= 2 ? course.ScoreMode : 2;
            scoreInit = Math.Max(0, course.ScoreInit);
            scoreDiff = Math.Max(0, course.ScoreDiff);
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

            // 連打・風船はゲージ計算の音符数に含めない。
            int notes = course.Notes.Count(n => n.Type is NoteType.Don or NoteType.Ka or NoteType.BigDon or NoteType.BigKa);
            goodGain = okGain = badGain = 0;
            if (notes == 0) return;

            // OpenTaiko の通常ゲージ。★1〜7 / ★8 / ★9以上で係数が変わる。
            float rate = course.Level <= 7 ? 70.7f : course.Level == 8 ? 70f : 75f;
            float damage = course.Level <= 8 ? 0.625f : 2f;
            float gain = (10000f / (notes * (rate / 100f))) / 100f;
            goodGain = TruncateGain(gain);
            okGain = TruncateGain(gain * 0.5f);
            badGain = TruncateGain(-gain * damage);
        }

        private static float TruncateGain(float value) => MathF.Truncate(value * 10000f) / 10000f;

        public void Update(float dt)
        {
            if (pendingBonus == 0) return;
            bonusSeconds -= dt;
            if (bonusSeconds <= 0) AddPendingBonus();
        }

        // combo は、この音符の判定を反映した後のコンボ数。
        public void ApplyJudge(Note note, int combo, PlayJudgement judge)
        {
            if (note.Type is not (NoteType.Don or NoteType.Ka or NoteType.BigDon or NoteType.BigKa)) return;

            double gain = judge switch
            {
                PlayJudgement.Good => goodGain,
                PlayJudgement.Ok => okGain,
                _ => badGain,
            };
            Gauge = Math.Clamp(Math.Round(Gauge + gain, 5, MidpointRounding.ToEven), 0, 100);
            if (judge == PlayJudgement.Bad) return;

            long points;
            if (scoreMode == 0)
            {
                points = combo < 200 ? 1000 : 2000;
                if (judge == PlayJudgement.Ok) points /= 2;
                if (note.IsGoGo) points = points * 6 / 5;
            }
            else
            {
                int multiplier = scoreMode == 1 ? Math.Clamp(combo / 10, 0, 10)
                    : combo < 10 ? 0 : combo < 30 ? 1 : combo < 50 ? 2 : combo < 100 ? 4 : 8;
                points = scoreInit + scoreDiff * multiplier;
                if (scoreMode == 2) points = points / 10 * 10;
                if (judge == PlayJudgement.Ok) points /= 2;
                if (note.IsGoGo) points = points * 6 / 5;
                points = points / 10 * 10;

                // SCOREMODE:2 は100コンボごとに1万点。OpenTaiko と同じく2秒後に加算する。
                if (scoreMode == 2 && combo > 0 && combo % 100 == 0)
                {
                    AddPendingBonus();
                    pendingBonus = 10000;
                    bonusSeconds = 2f;
                }
            }

            // 大音符は片手1打で成立。両手待ちや特良判定は設けない。
            if (note.Type is NoteType.BigDon or NoteType.BigKa) points *= 2;
            Score += points;
        }

        public void ApplyRollHit(Note note, bool popped)
        {
            long points;
            if (note.IsBalloon)
            {
                // 割れた最後の1打は、通常の300点に代えて5000点。
                points = popped ? 5000 : 300;
            }
            else if (note.IsRoll)
            {
                points = scoreMode == 2
                    ? note.Type == NoteType.BigRoll ? 200 : 100
                    : note.Type == NoteType.BigRoll ? 360 : 300;
            }
            else return;

            if (note.IsGoGo) points = points * 6 / 5;
            Score += points;
        }

        private void AddPendingBonus()
        {
            Score += pendingBonus;
            pendingBonus = 0;
            bonusSeconds = 0;
        }
    }
}
