using TakeTaikoSimulator.Configs;

namespace TakeTaikoSimulator.Charts
{
    // 演奏オプションの「あべこべ」「ランダム」：譜面の ドン と カッ を入れかえる
    //
    //   11/12 たけらい「あべこべはノーツが反転する。ランダムはきまぐれ、でたらめとあって…OpenTaiko 参考にして」
    //   OpenTaiko（CTja.cs の tRandomizeTaikoChips）と同じ：
    //     ・あべこべ：全部の ドン⇔カッ を入れかえる（大ドン⇔大カッも）
    //     ・きまぐれ：音符1個ごとに 0〜99 を引いて、0〜20 なら入れかえる（21%）
    //     ・でたらめ：0〜50 なら入れかえる（51%）
    //     ・連打・風船はそのまま
    //     ・入れかえたあと、音符の下の文字（ドン・ド・コ／カッ・カ）を並びから付け直す
    //   OpenTaiko は「あべこべ」と「ランダム」のどちらか1つしか選べないが、TaikøNauts は両方つけられる
    //   （アイコンが別々にある）ので、両方のときは あべこべ → ランダム の順にかける
    public static class NoteOptions
    {
        public const int KimagurePercent = 21;
        public const int DetaramePercent = 51;

        public static void Apply(Course course, bool abekobe, NoteRandom random, Random rng)
        {
            if (!abekobe && random == NoteRandom.Off) return;
            int percent = random switch
            {
                NoteRandom.Kimagure => KimagurePercent,
                NoteRandom.Detarame => DetaramePercent,
                _ => 0,
            };
            foreach (Note note in course.Notes)
            {
                if (note.Type is not (NoteType.Don or NoteType.Ka or NoteType.BigDon or NoteType.BigKa)) continue;
                bool flip = abekobe;
                if (percent > 0 && rng.Next(100) < percent) flip = !flip;
                if (flip) note.Type = Flip(note.Type);
            }
            SeNotes.Assign(course);
        }

        private static NoteType Flip(NoteType type) => type switch
        {
            NoteType.Don => NoteType.Ka,
            NoteType.Ka => NoteType.Don,
            NoteType.BigDon => NoteType.BigKa,
            NoteType.BigKa => NoteType.BigDon,
            _ => type,
        };
    }
}
