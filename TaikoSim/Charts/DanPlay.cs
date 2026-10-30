namespace TaikoSim.Charts
{
    // 段位に挑戦している間の状態と、合否の判定。
    //   判定のしかたは OpenTaiko に合わせる（赤合格／金合格、以上／未満の扱い）。
    //   条件の種類ごとの「以上／未満」は DanExamTypes.IsLess() が持っている。

    /// <summary>課題曲1曲ぶんの成績</summary>
    public sealed class DanSongTally
    {
        public int Great;     // 良
        public int Good;      // 可
        public int Miss;      // 不可
        public int Roll;      // 連打
        public int MaxCombo;
        public long Score;

        /// <summary>たたけた数（良＋可。不可は当たっていないので数えない）</summary>
        public int Hit => Great + Good;

        public long Value(DanExamType type) => type switch
        {
            DanExamType.Great => Great,
            DanExamType.Good => Good,
            DanExamType.Miss => Miss,
            DanExamType.Roll => Roll,
            DanExamType.Hit => Hit,
            DanExamType.Score => Score,
            DanExamType.MaxCombo => MaxCombo,
            _ => 0,
        };

        public void Reset()
        {
            Great = Good = Miss = Roll = MaxCombo = 0;
            Score = 0;
        }
    }

    // 段位1つぶんの挑戦
    public sealed class DanPlay
    {
        public DanCourse Course { get; }

        /// <summary>今やっている課題曲（0 から）</summary>
        public int SongIndex { get; set; }

        /// <summary>段位の魂ゲージ（0〜100）。曲をまたいで持ち越す</summary>
        public double Gauge { get; set; }

        /// <summary>課題曲ごとの成績</summary>
        public DanSongTally[] Tallies { get; }

        /// <summary>途中でやめたか（F3 の強制終了・Esc で戻った）</summary>
        public bool Quit { get; set; }

        public DanPlay(DanCourse course)
        {
            Course = course;
            Tallies = new DanSongTally[Math.Max(1, course.Songs.Count)];
            for (int i = 0; i < Tallies.Length; i++) Tallies[i] = new DanSongTally();
        }

        public DanSongTally Current => Tallies[Math.Clamp(SongIndex, 0, Tallies.Length - 1)];
        public int SongCount => Course.Songs.Count;
        public bool IsLastSong => SongIndex >= SongCount - 1;

        /// <summary>全部の課題曲を合計した値</summary>
        public long Total(DanExamType type)
        {
            // 最大コンボだけは「合計」ではなく「一番大きいもの」
            if (type == DanExamType.MaxCombo)
            {
                long best = 0;
                foreach (DanSongTally t in Tallies) best = Math.Max(best, t.MaxCombo);
                return best;
            }

            long sum = 0;
            foreach (DanSongTally t in Tallies) sum += t.Value(type);
            return sum;
        }

        // ===== 条件の判定 =====

        /// <summary>値がボーダーを満たしているか。「未満」系は value &lt; border、それ以外は value &gt;= border</summary>
        public static bool Satisfies(long value, long border, bool isLess) =>
            isLess ? value < border : value >= border;

        /// <summary>条件1つの今の状態（不合格／赤／金）</summary>
        public DanResultType Status(DanExam exam)
        {
            if (exam.Thresholds.Length == 0) return DanResultType.Gold;   // 条件が空＝何もしなくても満たす

            bool red = true, gold = true;
            if (exam.PerSong)
            {
                // 曲ごと条件：どの曲もそれぞれのボーダーを満たす必要がある
                for (int i = 0; i < SongCount && i < exam.Thresholds.Length; i++)
                {
                    long value = Tallies[i].Value(exam.Type);
                    DanThreshold t = exam.Thresholds[i];
                    if (!Satisfies(value, t.Red, exam.IsLess)) red = false;
                    if (exam.HasGold(i) && !Satisfies(value, t.Gold, exam.IsLess)) gold = false;
                }
            }
            else
            {
                long value = Total(exam.Type);
                DanThreshold t = exam.Thresholds[0];
                if (!Satisfies(value, t.Red, exam.IsLess)) red = false;
                if (exam.HasGold(0) && !Satisfies(value, t.Gold, exam.IsLess)) gold = false;
            }

            return !red ? DanResultType.Failed : gold ? DanResultType.Gold : DanResultType.Red;
        }

        /// <summary>魂ゲージの条件の今の状態</summary>
        public DanResultType GaugeStatus()
        {
            DanThreshold g = Course.Gauge;
            if (g.Red <= 0) return DanResultType.Gold;                     // ゲージ条件なし
            if (Gauge + 0.0001 < g.Red) return DanResultType.Failed;
            if (!Course.HasGoldGauge) return DanResultType.Red;            // 金のゲージ条件なし → 赤どまりにはしない
            return Gauge + 0.0001 >= g.Gold ? DanResultType.Gold : DanResultType.Red;
        }

        /// <summary>
        /// 段位ぜんぶの合否。
        /// 全部の条件が赤以上なら赤合格、さらに全部が金なら金合格、1つでも割ったら不合格。
        /// 「金の条件なし」の項目は金の足を引っぱらない。
        /// </summary>
        public DanResultType Overall()
        {
            if (Quit) return DanResultType.Failed;

            bool allGold = true;

            DanResultType gauge = GaugeStatus();
            if (gauge == DanResultType.Failed) return DanResultType.Failed;
            // ゲージの金条件が無いときは、金の判定に加えない
            if (gauge == DanResultType.Red && Course.HasGoldGauge) allGold = false;

            foreach (DanExam exam in Course.Exams)
            {
                DanResultType status = Status(exam);
                if (status == DanResultType.Failed) return DanResultType.Failed;
                if (status == DanResultType.Red) allGold = false;
            }

            return allGold ? DanResultType.Gold : DanResultType.Red;
        }

        /// <summary>
        /// もう赤合格に届かないことが確定したか（「未満」系の条件を使い切ったとき）。
        /// 途中で分かるのは「未満」系だけ。「以上」系はこれから増えるので、最後まで分からない。
        /// </summary>
        public bool AlreadyFailed()
        {
            foreach (DanExam exam in Course.Exams)
            {
                if (!exam.IsLess) continue;
                if (exam.PerSong)
                {
                    // 終わった曲（今の曲も含む）だけ見る
                    for (int i = 0; i <= SongIndex && i < SongCount && i < exam.Thresholds.Length; i++)
                        if (!Satisfies(Tallies[i].Value(exam.Type), exam.Thresholds[i].Red, true)) return true;
                }
                else if (!Satisfies(Total(exam.Type), exam.Thresholds[0].Red, true))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>結果をクリア記録に残す（次の段位の解禁条件に効く）</summary>
        public void SaveRecord()
        {
            DanResultType result = Overall();
            if (result is DanResultType.None or DanResultType.Failed) return;

            // コンボの種別：不可0なら全良、可も0ならさらに上
            long miss = Total(DanExamType.Miss);
            long good = Total(DanExamType.Good);
            SongClearType combo = miss > 0 ? SongClearType.Cleared
                : good > 0 ? SongClearType.FullCombo
                : SongClearType.AllPerfect;

            SongHistory.SaveDanRecord(SongHistory.KeyFor(Course.JsonPath),
                new DanClearRecord(result == DanResultType.Gold, combo));
        }
    }
}
