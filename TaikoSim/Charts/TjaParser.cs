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
            List<int> balloons = new();

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
                        Balloons = new List<int>(balloons),
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
                        break;
                    case "LEVEL":
                        level = (int)ParseNumber(value, 0);
                        break;
                    case "BALLOON":
                        balloons = ParseBalloons(value);
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
            int balloonIndex = 0;
            Note? openRoll = null; // 終わりの「8」を待っている連打・風船

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

            // 最後の「,」が無い小節が残っていたら処理する
            if (items.Any(item => item.Length == 1)) ProcessMeasure();

            // 終わりの「8」が無い連打は、譜面の最後で終わらせる
            if (openRoll != null) openRoll.EndTimeMs = time;

            // #GOGOEND が無いゴーゴータイムも、譜面の最後で終わらせる
            if (gogo) course.GoGoRanges.Add(new GoGoRange { StartMs = gogoStartMs, EndMs = time });

            // ----- ここから下は ParseBody の中だけで使う関数 -----

            // 今のBPM・拍子での1小節の長さ(ミリ秒)
            double MeasureLength() => 60000.0 / bpm * 4.0 * measure;

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
                        if (gogo) course.GoGoRanges.Add(new GoGoRange { StartMs = gogoStartMs, EndMs = time });
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
                }
            }

            void AddBarLine()
            {
                course.BarLines.Add(new BarLine { TimeMs = time, Bpm = bpm, Scroll = scroll, Visible = barLineOn });
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
                    case '8':
                        // 連打・風船の終わり
                        if (openRoll != null)
                        {
                            openRoll.EndTimeMs = time;
                            openRoll = null;
                        }
                        break;
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
                };
                course.Notes.Add(note);
                return note;
            }

            void StartRoll(NoteType type)
            {
                // 連打の途中にある「5」「7」などは続きなので無視
                if (openRoll != null) return;

                Note note = CreateNote(type);
                if (note.IsBalloon)
                {
                    // BALLOON: の数字を前から順番に使う(足りなければ5回)
                    note.BalloonCount = balloonIndex < course.Balloons.Count ? course.Balloons[balloonIndex] : 5;
                    balloonIndex++;
                }
                openRoll = note;
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
        private static List<int> ParseBalloons(string value)
        {
            List<int> result = new();
            foreach (string part in value.Split(','))
            {
                if (int.TryParse(part.Trim(), out int count) && count > 0) result.Add(count);
            }
            return result;
        }
    }
}