using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TaikoSim.Charts
{
    // .tja ファイルを読んで Chart にする係
    public static class TjaParser
    {
        // 音符として数える文字(C〜G は今は休符あつかい)
        private const string NoteChars = "0123456789ABCDEFG";

        // 文字の先頭にある数字を取り出す用("1+2i" → 1 など)
        private static readonly Regex NumberPattern = new(@"^[-+]?(\d+(\.\d*)?|\.\d+)");

        public static Chart Load(string path)
        {
            string text = ReadText(path);
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            Chart chart = new Chart
            {
                FilePath = path,
                Title = Path.GetFileNameWithoutExtension(path), // TITLE: が無いときはファイル名
            };

            // 難易度ごとのヘッダー(#START の前に書かれる)
            CourseType courseType = CourseType.Oni; // COURSE: が無ければおに
            int level = 0;
            // 風船の回数(普通・共通 / 玄人 / 達人)
            List<int> balloons = new(), balloonsExpert = new(), balloonsMaster = new();
            // COURSE: より前に書いてあった値(どの難易度にも使う初期値)
            int commonLevel = 0;
            List<int> commonBalloons = new(), commonBalloonsExpert = new(), commonBalloonsMaster = new();
            int scoreMode = 2, scoreInit = 300, scoreDiff = 120;
            int commonScoreMode = 2, commonScoreInit = 300, commonScoreDiff = 120;
            bool courseHeaderSeen = false;

            // 日本語の曲名(TITLEJA: / SUBTITLEJA:)。書いてあれば TITLE: より優先する(OpenTaiko と同じ)
            string titleJa = "";
            string subtitleJa = "";

            for (int i = 0; i < lines.Length; i++)
            {
                string line = StripComment(lines[i]).Trim();
                if (line.Length == 0) continue;

                // ----- 譜面本体(#START 〜 #END) -----
                if (line.StartsWith("#START", StringComparison.OrdinalIgnoreCase))
                {
                    List<string> body = new();
                    for (i++; i < lines.Length; i++)
                    {
                        string bodyLine = StripComment(lines[i]).Trim();
                        if (bodyLine.StartsWith("#END", StringComparison.OrdinalIgnoreCase)) break;
                        body.Add(bodyLine);
                    }

                    Course course = new Course
                    {
                        Type = courseType,
                        Level = level,
                        ScoreMode = scoreMode,
                        ScoreInit = scoreInit,
                        ScoreDiff = scoreDiff,
                        Balloons = new List<int>(balloons),
                        BalloonsExpert = new List<int>(balloonsExpert),
                        BalloonsMaster = new List<int>(balloonsMaster),
                    };
                    ParseBody(chart, course, body);

                    // 同じ難易度がもう1つある(2P用など)ときは、最初の方を使う
                    if (!chart.Courses.Any(c => c.Type == course.Type))
                    {
                        chart.Courses.Add(course);
                    }
                    continue;
                }

                // ----- ヘッダー(「名前:値」の形) -----
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;

                string key = line[..colon].Trim().ToUpperInvariant();
                string value = line[(colon + 1)..].Trim();

                switch (key)
                {
                    case "TITLE":
                        if (value.Length > 0) chart.Title = value;
                        break;
                    case "SUBTITLE":
                        chart.Subtitle = StripSubtitleMark(value);
                        break;
                    case "TITLEJA":
                        titleJa = value;
                        break;
                    case "SUBTITLEJA":
                        subtitleJa = StripSubtitleMark(value);
                        break;
                    case "WAVE":
                        chart.Wave = value;
                        break;
                    case "BPM":
                        chart.Bpm = ParseNumber(value, 120);
                        break;
                    case "OFFSET":
                        chart.OffsetSec = ParseNumber(value, 0);
                        break;
                    case "COURSE":
                        courseType = ParseCourse(value);
                        // 前の難易度の配点を、次の難易度へ持ち越さない。
                        courseHeaderSeen = true;
                        scoreMode = commonScoreMode;
                        scoreInit = commonScoreInit;
                        scoreDiff = commonScoreDiff;
                        // LEVEL・風船の回数も、前の難易度の値を持ち越さない(OpenTaiko も難易度ごとに読み直す)
                        level = commonLevel;
                        balloons = new List<int>(commonBalloons);
                        balloonsExpert = new List<int>(commonBalloonsExpert);
                        balloonsMaster = new List<int>(commonBalloonsMaster);
                        break;
                    case "LEVEL":
                        level = (int)ParseNumber(value, 0);
                        if (!courseHeaderSeen) commonLevel = level;
                        break;
                    case "BALLOON":
                    case "BALLOONNOR":
                        // 共通の部分と普通譜面の風船
                        if (ParseBalloons(value) is List<int> normal)
                        {
                            balloons = normal;
                            if (!courseHeaderSeen) commonBalloons = new List<int>(normal);
                        }
                        break;
                    case "BALLOONEXP":
                        // 玄人譜面の風船
                        if (ParseBalloons(value) is List<int> expert)
                        {
                            balloonsExpert = expert;
                            if (!courseHeaderSeen) commonBalloonsExpert = new List<int>(expert);
                        }
                        break;
                    case "BALLOONMAS":
                        // 達人譜面の風船
                        if (ParseBalloons(value) is List<int> master)
                        {
                            balloonsMaster = master;
                            if (!courseHeaderSeen) commonBalloonsMaster = new List<int>(master);
                        }
                        break;
                    case "SCOREMODE":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mode)
                            && mode is >= 0 and <= 2)
                        {
                            scoreMode = mode;
                            if (!courseHeaderSeen) commonScoreMode = mode;
                        }
                        break;
                    case "SCOREINIT":
                        // 2つ目の値は真打用。今回は通常配点の先頭の値だけを使う。
                        string firstScore = value.Split(',')[0].Trim();
                        if (int.TryParse(firstScore, NumberStyles.Integer, CultureInfo.InvariantCulture, out int initial)
                            && initial >= 0)
                        {
                            scoreInit = initial;
                            if (!courseHeaderSeen) commonScoreInit = initial;
                        }
                        break;
                    case "SCOREDIFF":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int difference)
                            && difference >= 0)
                        {
                            scoreDiff = difference;
                            if (!courseHeaderSeen) commonScoreDiff = difference;
                        }
                        break;
                }
            }

            // 日本語の曲名があればそっちを使う
            if (titleJa.Length > 0) chart.Title = titleJa;
            if (subtitleJa.Length > 0) chart.Subtitle = subtitleJa;

            return chart;
        }

        // サブタイトルの先頭の「--」「++」は表示の仕方の印なので外す
        private static string StripSubtitleMark(string value)
        {
            return (value.StartsWith("--") || value.StartsWith("++")) ? value[2..] : value;
        }

        // ===== 譜面本体を読む =====

        // 分岐ごとに覚えておく流れ方(OpenTaiko の BranchScrollStates と同じ考え方)
        //   #N・#E・#M に切り替えたら、その分岐が前に終わったときの状態から続ける
        private struct BranchFlowState
        {
            public double Scroll;
            public bool GoGo;
            public bool BarLineOn;
        }

        private static void ParseBody(Chart chart, Course course, List<string> body)
        {
            // 今の時間(曲の頭からのミリ秒)。OFFSET がマイナスだと、曲が先に始まって譜面が後から来る
            double time = -chart.OffsetSec * 1000.0;
            double bpm = chart.Bpm > 0 ? chart.Bpm : 120;
            double scroll = 1.0;
            double measure = 1.0; // 拍子(4/4 なら 1、3/4 なら 0.75)
            bool gogo = false;
            double gogoStartMs = 0; // 今のゴーゴータイムが始まった時間
            bool barLineOn = true;

            // ----- 譜面分岐 -----
            bool inBranch = false;                  // #N・#E・#M の中か(false なら共通)
            BranchType branch = BranchType.Normal;  // inBranch のとき、どの分岐か
            int branchIndex = -1;                   // 何番目の #BRANCHSTART の後ろか(-1 ならまだ無い)
            // #BRANCHSTART を書いた時点の時間・BPM・拍子(#N・#E・#M はここから書き始める)
            double startTime = time, startBpm = bpm, startMeasure = measure;
            // 分岐の中で一番後ろまで進んだ時間(#END で使う)
            double branchEndMax = time;
            BranchFlowState[] flowStates = new BranchFlowState[3];
            // #BRANCHSTART のあと、まだ最初の小節線を置いていない分岐
            bool[] branchBarPending = new bool[3];

            // 分岐を決める時刻を出すための記録(分岐ごと)
            double?[] lastBarTime = new double?[3];     // 最後の小節線
            double?[] lastRollEndTime = new double?[3]; // 最後の小節線より後で終わった連打

            // 終わりの「8」を待っている連打・風船(分岐ごと。共通の連打は3つとも同じ音符を指す)
            Note?[] openRolls = new Note?[3];

            // 風船の回数を、BALLOON:(普通と共通)・BALLOONEXP:・BALLOONMAS: のどれから何番目まで使ったか
            List<int>[] balloonLists = { course.Balloons, course.BalloonsExpert, course.BalloonsMaster };
            int[] balloonIndex = new int[3];

            // 1小節分の中身(音符の文字1つずつ と 命令 を、書いてある順番で入れる)
            List<string> items = new();

            foreach (string line in body)
            {
                if (line.Length == 0) continue;

                // 命令(#BPMCHANGE など)
                if (line.StartsWith('#'))
                {
                    items.Add(line);
                    continue;
                }

                // 音符の行(「,」で小節が終わる。1行に「,」がいくつあってもOK)
                foreach (char c in line)
                {
                    if (c == ',')
                    {
                        ProcessMeasure();
                        items.Clear();
                    }
                    else if (NoteChars.Contains(c))
                    {
                        items.Add(c.ToString());
                    }
                }
            }

            // 最後の「,」が無い小節が残っていたら処理する。命令だけなら命令だけ反映する
            if (items.Any(item => item.Length == 1)) ProcessMeasure();
            else foreach (string item in items) ApplyCommand(item);

            // 分岐の途中で終わっていたら、一番長い分岐の終わりに合わせる(OpenTaiko の #END と同じ)
            if (inBranch)
            {
                GotoBranchEnd();
                time = Math.Max(time, branchEndMax);
            }

            // 終わりの「8」が無い連打は、譜面の最後で終わらせる
            foreach (Note? roll in openRolls)
            {
                if (roll != null && roll.EndTimeMs <= roll.TimeMs) roll.EndTimeMs = time;
            }

            // #GOGOEND が無いゴーゴータイムも、譜面の最後で終わらせる
            CloseGoGo();

            // ----- ここから下は ParseBody の中だけで使う関数 -----

            // 今のBPM・拍子での1小節の長さ(ミリ秒)
            double MeasureLength() => 60000.0 / bpm * 4.0 * measure;

            // 今書いているところは、どの分岐か(null なら共通)
            BranchType? CurrentBranch() => inBranch ? branch : null;

            // 今書いているところが関係する分岐(共通なら3つとも)
            IEnumerable<int> CurrentTargets() => inBranch ? new[] { (int)branch } : new[] { 0, 1, 2 };

            // 1小節分を処理する
            void ProcessMeasure()
            {
                // 小節の中の音符の文字の数(0 も数える)。これで1小節を等分する
                int noteCount = items.Count(item => item.Length == 1);
                bool barAdded = false;

                foreach (string item in items)
                {
                    // 命令はその場で反映(音符の途中に書かれていてもOK)
                    if (item.Length > 1)
                    {
                        ApplyCommand(item);
                        continue;
                    }

                    // 小節線は、小節の最初の音符の位置(頭の命令を反映したあと)に置く
                    if (!barAdded)
                    {
                        AddBarLine();
                        barAdded = true;
                    }

                    AddNote(item[0]);
                    time += MeasureLength() / noteCount;
                }

                // 音符が1つも無い小節(「,」だけ)は、1小節分まるごと休み
                if (!barAdded) AddBarLine();
                if (noteCount == 0) time += MeasureLength();
            }

            // 命令を反映する
            void ApplyCommand(string command)
            {
                string[] parts = command.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
                string name = parts[0].ToUpperInvariant();
                string arg = parts.Length > 1 ? parts[1].Trim() : "";

                switch (name)
                {
                    case "#BPMCHANGE":
                        double newBpm = ParseNumber(arg, bpm);
                        if (newBpm > 0) bpm = newBpm;
                        break;
                    case "#SCROLL":
                        scroll = ParseNumber(arg, scroll);
                        break;
                    case "#MEASURE":
                        measure = ParseMeasure(arg, measure);
                        break;
                    case "#GOGOSTART":
                        // ゴーゴー中にもう一度書いてあっても、始まりの時間はそのまま
                        if (!gogo) gogoStartMs = time;
                        gogo = true;
                        break;
                    case "#GOGOEND":
                        CloseGoGo();
                        gogo = false;
                        break;
                    case "#DELAY":
                        time += ParseNumber(arg, 0) * 1000.0;
                        break;
                    case "#BARLINEOFF":
                        barLineOn = false;
                        break;
                    case "#BARLINEON":
                        barLineOn = true;
                        break;

                    // ----- 譜面分岐 -----
                    case "#BRANCHSTART":
                        StartBranch(arg);
                        break;
                    case "#N":
                        SwitchBranch(BranchType.Normal);
                        break;
                    case "#E":
                        SwitchBranch(BranchType.Expert);
                        break;
                    case "#M":
                        SwitchBranch(BranchType.Master);
                        break;
                    case "#BRANCHEND":
                        GotoBranchEnd();
                        break;
                    case "#SECTION":
                        // 分岐の判定に使う数を0に戻す(書いてある時刻に起きる)
                        course.BranchEvents.Add(new BranchEvent { TimeMs = time, Type = BranchEventType.Section });
                        break;
                    case "#LEVELHOLD":
                        if (inBranch && branchIndex >= 0)
                        {
                            // 分岐の中：この分岐に決まったら、そのあとは固定
                            course.BranchPoints[branchIndex].LevelHold[(int)branch] = true;
                        }
                        else
                        {
                            // 分岐の外：ここを通ったら固定
                            course.BranchEvents.Add(new BranchEvent { TimeMs = time, Type = BranchEventType.LevelHold });
                        }
                        break;
                }
            }

            // #BRANCHSTART：分岐の条件を読んで、分岐を決める時刻を決める
            void StartBranch(string arg)
            {
                // 前の分岐が #BRANCHEND 無しで続いていたら、ここで閉じる
                GotoBranchEnd();

                BranchCondition condition = ParseBranchCondition(arg);
                bool rollCondition = condition.Type is BranchConditionType.Roll or BranchConditionType.BalloonHits;

                course.BranchPoints.Add(new BranchPoint
                {
                    JudgeTimeMs = GetBranchJudgeTime(rollCondition),
                    BranchTimeMs = time,
                    Condition = condition,
                });
                branchIndex = course.BranchPoints.Count - 1;

                // ここから #N・#E・#M を書き始める
                startTime = time;
                startBpm = bpm;
                startMeasure = measure;
                branchEndMax = time;
                for (int i = 0; i < 3; i++) branchBarPending[i] = true;
            }

            // 分岐を決める時刻(OpenTaiko の GetBranchJudgeChipTime と同じ決め方)
            //   ・基本は、分岐する場所の1小節前の小節線(3つの分岐で一番遅いもの)
            //   ・連打の条件(r・rb)のとき
            //       まだ終わっていない連打がある → 分岐する場所
            //       最後の小節線より後で終わった連打がある → その連打の終わり(分岐する場所より後にはしない)
            //   ・小節線が無い(譜面の頭で分岐) → 譜面の頭の4拍前
            double GetBranchJudgeTime(bool rollCondition)
            {
                if (rollCondition && openRolls.Any(roll => roll != null)) return time;

                double? bar = lastBarTime.Where(t => t != null).Max();
                double judge = bar ?? (-chart.OffsetSec * 1000.0 - 4 * 60000.0 / (chart.Bpm > 0 ? chart.Bpm : 120));
                double minimum = judge;

                if (rollCondition)
                {
                    double? rollEnd = lastRollEndTime.Where(t => t != null).Max();
                    if (rollEnd is double end && end > judge) judge = end;
                }
                return Math.Max(minimum, Math.Min(judge, time));
            }

            // #N・#E・#M：分岐の始まりの時間に戻って、その分岐を書き始める
            void SwitchBranch(BranchType next)
            {
                CloseGoGo();
                SaveFlowState();
                if (inBranch) branchEndMax = Math.Max(branchEndMax, time);

                inBranch = true;
                branch = next;
                time = startTime;
                bpm = startBpm;
                measure = startMeasure;

                BranchFlowState state = flowStates[(int)next];
                scroll = state.Scroll;
                gogo = state.GoGo;
                barLineOn = state.BarLineOn;
                if (gogo) gogoStartMs = time;
            }

            // #BRANCHEND：共通に戻る。時間は最後に書いた分岐の終わりから続ける(今の OpenTaiko と同じ)
            void GotoBranchEnd()
            {
                if (!inBranch) return;

                CloseGoGo();
                SaveFlowState();
                branchEndMax = Math.Max(branchEndMax, time);
                inBranch = false;
                if (gogo) gogoStartMs = time;

                startTime = time;
                startBpm = bpm;
                startMeasure = measure;
            }

            // 今の流れ方を覚えておく(共通なら3つの分岐全部に)
            void SaveFlowState()
            {
                BranchFlowState state = new() { Scroll = scroll, GoGo = gogo, BarLineOn = barLineOn };
                foreach (int i in CurrentTargets()) flowStates[i] = state;
            }

            // ゴーゴーの途中なら、今の時間でいったん区切る(分岐が変わるとき・#GOGOEND)
            void CloseGoGo()
            {
                if (!gogo) return;
                if (time > gogoStartMs)
                {
                    course.GoGoRanges.Add(new GoGoRange
                    {
                        StartMs = gogoStartMs,
                        EndMs = time,
                        Branch = CurrentBranch(),
                        BranchIndex = inBranch ? branchIndex : -1,
                    });
                }
                gogoStartMs = time;
            }

            void AddBarLine()
            {
                BarLine bar = new BarLine
                {
                    TimeMs = time,
                    Bpm = bpm,
                    Scroll = scroll,
                    Visible = barLineOn,
                    Branch = CurrentBranch(),
                    BranchIndex = inBranch ? branchIndex : -1,
                };

                foreach (int i in CurrentTargets())
                {
                    if (branchBarPending[i]) bar.IsBranchStart = true;
                    branchBarPending[i] = false;
                    lastBarTime[i] = time;
                    lastRollEndTime[i] = null; // 「最後の小節線より後で終わった連打」を数え直す
                }
                course.BarLines.Add(bar);
            }

            void AddNote(char c)
            {
                switch (c)
                {
                    case '1': CreateNote(NoteType.Don); break;
                    case '2': CreateNote(NoteType.Ka); break;
                    case '3': case 'A': CreateNote(NoteType.BigDon); break; // A(手つなぎ)は今は大ドンあつかい
                    case '4': case 'B': CreateNote(NoteType.BigKa); break;  // B(手つなぎ)は今は大カッあつかい
                    case '5': StartRoll(NoteType.Roll); break;
                    case '6': StartRoll(NoteType.BigRoll); break;
                    case '7': StartRoll(NoteType.Balloon); break;
                    case '9': StartRoll(NoteType.Kusudama); break;
                    case '8': EndRoll(); break;
                        // 0 と C〜G は休符
                }
            }

            Note CreateNote(NoteType type)
            {
                Note note = new Note
                {
                    Type = type,
                    TimeMs = time,
                    EndTimeMs = time,
                    Bpm = bpm,
                    Scroll = scroll,
                    IsGoGo = gogo,
                    Branch = CurrentBranch(),
                    BranchIndex = inBranch ? branchIndex : -1,
                };
                course.Notes.Add(note);
                return note;
            }

            void StartRoll(NoteType type)
            {
                // 連打の途中にある「5」「7」などは続きなので無視
                if (CurrentTargets().Any(i => openRolls[i] != null)) return;

                Note note = CreateNote(type);
                if (note.IsBalloon) note.BalloonCount = TakeBalloonCount();
                foreach (int i in CurrentTargets()) openRolls[i] = note;
            }

            // 連打・風船の終わり(「8」)
            //   ・共通の連打を分岐の中で終わらせたときは、普通譜面の「8」を終わりにする(OpenTaiko と同じ)
            //   ・分岐の中の連打を共通で終わらせたときは、その連打の終わりにする
            void EndRoll()
            {
                foreach (int i in CurrentTargets())
                {
                    Note? roll = openRolls[i];
                    if (roll == null) continue;
                    openRolls[i] = null;
                    lastRollEndTime[i] = time; // 分岐を決める時刻の計算用

                    bool commonRollInBranch = roll.Branch == null && inBranch;
                    if (commonRollInBranch && i != (int)BranchType.Normal) continue;
                    if (roll.EndTimeMs > roll.TimeMs) continue; // もう終わらせた(共通の連打を共通で終わらせたとき)

                    roll.EndTimeMs = time;
                }
            }

            // 風船を割る回数を、書いてある分岐のリストから順番に取る
            //   共通の風船：BALLOON: の数を使う。3つの分岐のリストを全部1つ進める(OpenTaiko と同じ)
            //   分岐の中の風船：その分岐のリストを使う
            //   リストが空・足りない → 5回
            int TakeBalloonCount()
            {
                int count = 5;
                int source = inBranch ? (int)branch : (int)BranchType.Normal;
                foreach (int i in CurrentTargets())
                {
                    if (balloonIndex[i] >= balloonLists[i].Count) continue;
                    if (i == source) count = balloonLists[i][balloonIndex[i]];
                    balloonIndex[i]++;
                }
                return count;
            }
        }

        // ===== 小さい道具 =====

        // 文字コードを自動で見分けて読む(UTF-8 か Shift-JIS)
        private static string ReadText(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);

            // BOM付きUTF-8
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }

            // UTF-8 として正しく読めればUTF-8、変な文字が出たら Shift-JIS
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // Shift-JIS を使えるようにする
                return Encoding.GetEncoding(932).GetString(bytes);              // 932 = Shift-JIS
            }
        }

        // 「//」から後ろはコメントなので消す
        private static string StripComment(string line)
        {
            int index = line.IndexOf("//", StringComparison.Ordinal);
            return index >= 0 ? line[..index] : line;
        }

        // 数字を読む(読めなければ fallback)
        private static double ParseNumber(string value, double fallback)
        {
            Match match = NumberPattern.Match(value.Trim());
            if (match.Success && double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            {
                return number;
            }
            return fallback;
        }

        // 「3/4」→ 0.75
        private static double ParseMeasure(string value, double fallback)
        {
            string[] parts = value.Split('/');
            if (parts.Length == 2
                && double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double top)
                && double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double bottom)
                && top > 0 && bottom > 0)
            {
                return top / bottom;
            }
            return fallback;
        }

        // 「Oni」「3」などを難易度にする
        private static CourseType ParseCourse(string value)
        {
            if (int.TryParse(value.Trim(), out int number) && number >= 0 && number <= 6)
            {
                return (CourseType)number;
            }

            return value.Trim().ToLowerInvariant() switch
            {
                "easy" => CourseType.Easy,
                "normal" => CourseType.Normal,
                "hard" => CourseType.Hard,
                "oni" => CourseType.Oni,
                "edit" or "ura" => CourseType.Edit,
                "tower" => CourseType.Tower,
                "dan" => CourseType.Dan,
                _ => CourseType.Oni,
            };
        }

        // 「5,10,3」→ [5, 10, 3]
        // OpenTaiko と同じく、0 もそのまま入れる(数がずれないように)。空のところで終わり。
        // 数字じゃないものがあったら null(その行は使わない)
        private static List<int>? ParseBalloons(string value)
        {
            List<int> result = new();
            foreach (string part in value.Split(','))
            {
                string text = part.Trim();
                if (text.Length == 0) break;
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)) return null;
                result.Add(count);
            }
            return result;
        }

        // 「p,80,90」「r 5,10」「P,80,90,l」などを分岐の条件にする(OpenTaiko の #BRANCHSTART と同じ読み方)
        // 読めないときは「条件なし」(今の分岐のまま)
        private static BranchCondition ParseBranchCondition(string argument)
        {
            BranchCondition condition = new();
            if (string.IsNullOrWhiteSpace(argument)) return condition;

            // 名前と数字の間に「,」が無いとき(例：「p80,90」)は「,」を入れる
            argument = Regex.Replace(argument, @"(?<=^\s*[a-zA-Z]+)\s*(?=\d)", ",");
            string[] args = argument.Split(',', StringSplitOptions.TrimEntries);
            if (args.Length < 3 || args[0].Length == 0) return condition;

            if (!double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double expert)
                || !double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double master))
            {
                return condition;
            }

            // 1文字目が大文字なら、大音符(大連打)だけを数える(s はいつも全部、d はいつも大音符だけ)
            bool upper = char.IsUpper(args[0][0]);
            (BranchConditionType type, bool bigOnly) = args[0].ToLowerInvariant() switch
            {
                "p" => (BranchConditionType.Accuracy, upper),
                "pp" => (BranchConditionType.PercentPerfect, upper),
                "jb" => (BranchConditionType.JudgeBad, upper),
                "r" => (BranchConditionType.Roll, upper),
                "rb" => (BranchConditionType.BalloonHits, upper),
                "s" => (BranchConditionType.Score, false),
                "d" => (BranchConditionType.JudgeGood, true),
                "jp" => (BranchConditionType.JudgeGood, upper),
                "jg" => (BranchConditionType.JudgeOk, upper),
                _ => (BranchConditionType.None, false),
            };
            if (type == BranchConditionType.None) return condition;

            // 4つ目：l なら「未満」、m か空なら「以上」。それ以外は読めないので条件なし
            string range = args.Length > 3 ? args[3] : "";
            if (range != "" && range != "l" && range != "m") return condition;

            condition.Type = type;
            condition.BigOnly = bigOnly;
            condition.Less = range == "l";
            condition.Expert = expert;
            condition.Master = master;
            return condition;
        }
    }
}