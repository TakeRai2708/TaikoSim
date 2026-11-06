namespace TaikoSim.Charts
{
    // 音符の下の文字（口唱歌）を決める係：ドン・ド・コ／カッ・カ
    //
    //   11/6 たけらい「今は全部ドン・カッだけど、ドコドンとかドドドになるときがある」。
    //   OpenTaiko の CTja.tSenotes_Core_V2（TJAPlayer3 から持ってきたもの）をそのまま移した。
    //   ゲーセンの六段の動画でも「ド カッ カ ド ドン ドン カッ」と、間隔で変わっていた。
    //
    //   決め方：音符ごとに、前後3つずつ（計7個）との間隔を「16分音符＝1」で数えて見る
    //     ドン：次が8分(2)より遠い → ドン ／ 16分で続く → ド・コを交互（ドコドン、ドコドコドン）
    //           8分で続く「ドドド」→ BPM 120 以上は「ド ド ドン」、120 より遅いと「ドン ドン ドン」
    //     カッ：次が離れている → カッ ／ 間が短い → カ
    //   大音符・連打・ふうせんは、いつも同じ文字（ここでは決めない）
    //
    //   ・間隔は OpenTaiko だと「何拍目か」から出すけど、TaikoSim の音符は時間(ms)と BPM しか持っていないので、
    //     時間 × BPM ÷ 15000 で出す。曲の途中で BPM が変わるところのすぐ近くだけ、少しずれることがある
    //   ・OpenTaiko は「== 2」「> 2」のようにぴったりで比べているので、ここは小さいずれ(0.02)を同じとみなす
    //   ・譜面分岐があるときは、普通・玄人・達人のそれぞれで決める（OpenTaiko の tSetSenotes_branch と同じ）
    public static class SeNotes
    {
        // OpenTaiko の nSenote の番号（SENotes.png の段。OpenTaiko の画像は ドン・ド・コ・カッ・カ の順）
        private const int OtDon = 0, OtDo = 1, OtKo = 2, OtKat = 3, OtKa = 4;
        private const int Data = 3;
        private const double Same = 0.02;

        /// <summary>コースの全部の ドン・カッ に、文字（Note.SeKind）を決めて入れる</summary>
        public static void Assign(Course course)
        {
            if (course.HasBranch)
            {
                // 分岐ごとに：その分岐をずっと通ったときに出てくる音符（共通の音符もふくむ）で決める。
                // 共通の音符は3回決めなおすことになるけど、最後（達人）の結果が残る。
                // （OpenTaiko は分岐ごとに別の音符を持っているので、共通の部分は同じになるはず）
                foreach (BranchType branch in new[] { BranchType.Normal, BranchType.Expert, BranchType.Master })
                    AssignList(course.Notes.Where(n => n.Branch == null || n.Branch == branch).ToList());
            }
            else
            {
                AssignList(course.Notes.ToList());
            }
        }

        private static bool IsDon(Note n) => n.Type == NoteType.Don;
        private static bool IsKa(Note n) => n.Type == NoteType.Ka;
        private static bool IsBig(Note n) => n.Type is NoteType.BigDon or NoteType.BigKa;

        private static void AssignList(List<Note> all)
        {
            // OpenTaiko と同じく、叩ける音符（ふつう・大音符・連打・ふうせんの頭）だけを時間の順に並べる
            List<Note> list = all.OrderBy(n => n.TimeMs).ToList();
            if (list.Count == 0) return;

            // 1つ目からの位置を「16分音符＝1」で出す（前の音符の BPM で、間の時間を数に直していく）
            double[] pos = new double[list.Count];
            for (int i = 1; i < list.Count; i++)
            {
                double bpm = list[i - 1].Bpm > 0 ? list[i - 1].Bpm : 120;
                pos[i] = pos[i - 1] + (list[i].TimeMs - list[i - 1].TimeMs) * bpm / 15000.0;
            }

            int[] senote = new int[list.Count];
            for (int i = 0; i < list.Count; i++) senote[i] = IsKa(list[i]) ? OtKat : OtDon;

            int docoCount = 0;
            Note?[] sort = new Note?[7];
            double[] time = new double[7];
            for (int i = 0; i < list.Count; i++)
            {
                for (int j = 0; j < 7; j++)
                {
                    int k = i + (j - 3);
                    if (k < 0) { sort[j] = null; time[j] = -1e9; }
                    else if (k >= list.Count) { sort[j] = null; time[j] = 1e9; }
                    else
                    {
                        sort[j] = list[k];
                        time[j] = (pos[k] - pos[i]) * (list[k].Scroll == 0 ? 1 : list[k].Scroll);
                    }
                    if (time[j] < 0) time[j] = -time[j];
                }

                Note note = list[i];
                if (IsDon(note))
                {
                    // （左が2より離れている）＿右2＿右ドン＿右右4＿右右ドン… → ドコドコ… の始まり
                    if (Gt(time[Data - 1], 2) && Eq(time[Data + 1], 2) && IsDonAt(sort, Data + 1)
                        && Eq(time[Data + 2], 4) && IsDonAt(sort, Data + 2) && Eq(time[Data + 3], 6) && IsDonAt(sort, Data + 3))
                    {
                        senote[i] = OtDo;
                        docoCount = 1;
                        continue;
                    }
                    // ドコドコの途中：左2＿右2＿右ドン
                    if (docoCount != 0 && Eq(time[Data - 1], 2) && Eq(time[Data + 1], 2) && IsDonAt(sort, Data + 1))
                    {
                        senote[i] = docoCount % 2 == 0 ? OtDo : OtKo;
                        docoCount++;
                        continue;
                    }
                    docoCount = 0;

                    // 8分のドコドン
                    if (Ge(time[Data - 2], 4.1) && Eq(time[Data - 1], 2) && Eq(time[Data + 1], 2) && Ge(time[Data + 2], 4.1)
                        && IsDonAt(sort, Data - 1) && IsDonAt(sort, Data + 1))
                    {
                        if (note.Bpm >= 120.0) { senote[i - 1] = OtDo; senote[i] = OtKo; senote[i + 1] = OtDon; }
                        else { senote[i - 1] = OtDon; senote[i] = OtDon; senote[i + 1] = OtDon; }
                        continue;
                    }

                    // BPM 120 より遅いときだけ：8分の「ドドド」→「ドンドンドン」
                    if (Ge(time[Data - 1], 2) && Ge(time[Data + 1], 2) && note.Bpm < 120.0)
                    {
                        senote[i] = OtDon;
                        continue;
                    }

                    // ドコドコドン
                    if (Ge(time[Data - 3], 3.4) && Eq(time[Data - 2], 2) && Eq(time[Data - 1], 1) && Eq(time[Data + 1], 1)
                        && Eq(time[Data + 2], 2) && Ge(time[Data + 3], 3.4)
                        && IsDonAt(sort, Data - 2) && IsDonAt(sort, Data - 1) && IsDonAt(sort, Data + 1) && IsDonAt(sort, Data + 2))
                    {
                        senote[i - 2] = OtDo; senote[i - 1] = OtKo; senote[i] = OtDo; senote[i + 1] = OtKo; senote[i + 2] = OtDon;
                        i += 2;
                    }
                    // ドコドン
                    else if (Ge(time[Data - 2], 2.4) && Eq(time[Data - 1], 1) && Eq(time[Data + 1], 1) && Ge(time[Data + 2], 2.4)
                        && IsDonAt(sort, Data - 1) && IsDonAt(sort, Data + 1))
                        senote[i] = OtKo;
                    else
                        senote[i] = ShortOrLong(time, sort, OtDon, OtDo);
                }
                else if (IsKa(note))
                {
                    docoCount = 0;
                    // BPM 120 より遅いときだけ：8分の「カカカ」→「カッカッカッ」
                    if (Eq(time[Data - 1], 2) && Eq(time[Data + 1], 2)
                        && list[i - 1].Bpm < 120.0 && note.Bpm < 120.0 && list[i + 1].Bpm < 120.0)
                    {
                        senote[i] = OtKat;
                        continue;
                    }
                    senote[i] = ShortOrLong(time, sort, OtKat, OtKa);
                }
                else
                {
                    docoCount = 0;
                }
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (!IsDon(list[i]) && !IsKa(list[i])) continue;
                list[i].SeKind = senote[i] switch
                {
                    OtDo => SeKind.Do,
                    OtKo => SeKind.Ko,
                    OtKa => SeKind.Ka,
                    OtKat => SeKind.Kat,
                    _ => SeKind.Don,
                };
            }
        }

        // ドン・カッ共通：離れているなら長い方（ドン・カッ）、続いているなら短い方（ド・カ）
        private static int ShortOrLong(double[] time, Note?[] sort, int longKind, int shortKind)
        {
            if (Gt(time[Data + 1], 2)) return longKind;                                     // 右の音符が2より離れている
            if (Ge(time[Data + 1], 1.4) && Le(time[Data - 1], 1.4)) return longKind;          // 右が1.4以上、左が1.4以内
            if (Ge(time[Data + 1], 2) && Le(time[Data + 2], 3)) return longKind;              // 右が2以上、右右が3以内
            if (Ge(time[Data + 1], 2) && sort[Data + 1] is Note n && IsBig(n)) return longKind;   // 右が2以上の大音符
            return shortKind;
        }

        private static bool IsDonAt(Note?[] sort, int j) => sort[j] is Note n && IsDon(n);

        // 時間から出した間隔には小さいずれがあるので、Same(0.02) までは同じとみなして比べる
        //   （例：8分ぴったりが 2.0001 になって「2より遠い」と判定されると、カ が カッ になってしまう。
        //    ゲーセンの六段の動画の 190秒で「カ ドン」のところがそうだった）
        private static bool Eq(double a, double b) => Math.Abs(a - b) < Same;
        private static bool Gt(double a, double b) => a > b + Same;    // a > b
        private static bool Ge(double a, double b) => a > b - Same;    // a >= b
        private static bool Le(double a, double b) => a < b + Same;    // a <= b
    }
}
