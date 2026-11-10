using System.Text.Json;

namespace TakeTaikoSimulator.Charts
{
    // 段位道場のファイルを読む。
    //   dan.def             … 段位カテゴリの目印(#TITLE・#DISABLEDCHEAT)
    //   dan.json            … 課題曲と合格条件
    //   requirements.json   … 解禁条件(任意)
    //   ChangeEffect/*/Config.json … 曲間演出(任意)
    //
    //   どれもキーが camelCase。TakeTaikoSimulator の設定ファイル(GameConfig など)は PascalCase なので、
    //   ソース生成の AppJsonContext に混ぜるとそっちの読み書きが壊れる。
    //   なので JsonDocument で手読みする(JsonDocument.Parse は Native AOT でも使える)。
    public static class DanJson
    {
        public const string DefName = "dan.def";
        public const string JsonName = "dan.json";
        public const string RequirementsName = "requirements.json";
        public const string ChangeEffectFolder = "ChangeEffect";

        // ===== dan.def =====

        /// <summary>dan.def を読む。読めなければ null</summary>
        public static DanCategory? ReadDef(string path)
        {
            try
            {
                string folder = Path.GetDirectoryName(path) ?? "";
                DanCategory category = new()
                {
                    FolderPath = folder,
                    Title = Path.GetFileName(folder),   // #TITLE が無ければフォルダ名
                };

                foreach (string raw in ChartText.Read(path).Split('\n'))
                {
                    string line = raw.Trim();
                    if (!line.StartsWith('#')) continue;
                    int colon = line.IndexOf(':');
                    if (colon <= 0) continue;

                    string key = line[..colon].Trim().ToUpperInvariant();
                    string value = line[(colon + 1)..].Split(';')[0].Trim();
                    switch (key)
                    {
                        case "#TITLE":
                            if (value.Length > 0) category.Title = value;
                            break;
                        case "#DISABLEDCHEAT":
                            category.DisabledCheat = value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1";
                            break;
                    }
                }
                return category;
            }
            catch
            {
                return null;
            }
        }

        // ===== dan.json =====

        /// <summary>dan.json を読む。読めなければ Error に理由を入れたものを返す(段位選択画面に出すため)</summary>
        public static DanCourse ReadCourse(string path)
        {
            string folder = Path.GetDirectoryName(path) ?? "";
            DanCourse course = new()
            {
                JsonPath = path,
                FolderPath = folder,
                Title = Path.GetFileName(folder),
            };

            try
            {
                using JsonDocument doc = JsonDocument.Parse(ChartText.Read(path));
                JsonElement r = doc.RootElement;
                if (r.ValueKind != JsonValueKind.Object)
                {
                    course.Error = "dan.json の中身がオブジェクトではありません";
                    return course;
                }

                string title = Str(r, "title");
                if (title.Length > 0) course.Title = title;
                course.DanIndex = Int(r, "danIndex", 0);

                course.PlatePath = Str(r, "danPlatePath");
                course.PanelSidePath = Str(r, "danPanelSidePath");
                course.TitlePlatePath = Str(r, "danTitlePlatePath");
                course.MiniPlatePath = Str(r, "danMiniPlatePath");
                course.MiniPlateText = Str(r, "danMiniPlateText");

                course.ShodanTextPaths = StrArray(r, "shodanTextPaths");
                course.ShodanRedTextPaths = StrArray(r, "shodanRedTextPaths");
                course.ShodanGoldTextPaths = StrArray(r, "shodanGoldTextPaths");

                // 課題曲
                if (r.TryGetProperty("danSongs", out JsonElement songs) && songs.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement s in songs.EnumerateArray())
                    {
                        if (s.ValueKind != JsonValueKind.Object) continue;
                        string rel = Str(s, "path");
                        if (rel.Length == 0) continue;
                        course.Songs.Add(new DanSong
                        {
                            RelativePath = rel,
                            FullPath = ResolveRelative(folder, rel),
                            // 0=かんたん 1=ふつう 2=むずかしい 3=おに 4=おに(裏)。TakeTaikoSimulator の CourseType と同じ並び
                            Course = (CourseType)Math.Clamp(Int(s, "difficulty", 3), 0, 4),
                            Genre = Str(s, "genre"),
                            IsHidden = Flag(s, "isHidden", false),
                        });
                    }
                }
                if (course.Songs.Count == 0) course.Error = "danSongs に曲が1つもありません";

                // ゲージ条件
                if (r.TryGetProperty("conditionGauge", out JsonElement g) && g.ValueKind == JsonValueKind.Object)
                    course.Gauge = new DanThreshold(Int(g, "red", 0), Int(g, "gold", 0));

                // 合格条件
                if (r.TryGetProperty("conditions", out JsonElement conds) && conds.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement c in conds.EnumerateArray())
                    {
                        if (c.ValueKind != JsonValueKind.Object) continue;
                        if (DanExamTypes.Parse(Str(c, "type")) is not DanExamType type) continue; // 知らない種類は飛ばす

                        DanThreshold[] thresholds = ReadThresholds(c, course.Songs.Count);
                        if (thresholds.Length == 0) continue;
                        course.Exams.Add(new DanExam { Type = type, Thresholds = thresholds });
                    }
                }

                // 任意のファイル
                course.Requirement = ReadRequirements(Path.Combine(folder, RequirementsName));
                ReadChangeEffects(course);
            }
            catch (Exception ex)
            {
                course.Error = $"dan.json を読めませんでした（{ex.GetType().Name}）";
            }
            return course;
        }

        /// <summary>
        /// threshold を読む。単一オブジェクトなら全体条件(長さ1)、配列なら曲ごと条件。
        /// 曲数と長さが合わない配列は、足りないぶんを最後の値で埋める。
        /// </summary>
        private static DanThreshold[] ReadThresholds(JsonElement condition, int songCount)
        {
            if (!condition.TryGetProperty("threshold", out JsonElement t)) return Array.Empty<DanThreshold>();

            if (t.ValueKind == JsonValueKind.Object)
                return new[] { One(t) };

            if (t.ValueKind != JsonValueKind.Array) return Array.Empty<DanThreshold>();

            List<DanThreshold> list = new();
            foreach (JsonElement e in t.EnumerateArray())
                if (e.ValueKind == JsonValueKind.Object) list.Add(One(e));

            if (list.Count == 0) return Array.Empty<DanThreshold>();
            // 長さ1の配列は「全体条件」と同じ扱いにする(書き方のゆれを吸収)
            if (list.Count == 1) return list.ToArray();
            // 曲数より短ければ最後の値で埋め、長ければ切る
            while (list.Count < songCount) list.Add(list[^1]);
            if (songCount > 0 && list.Count > songCount) list.RemoveRange(songCount, list.Count - songCount);
            return list.ToArray();

            static DanThreshold One(JsonElement e) => new(Long(e, "red", 0), Long(e, "gold", 0));
        }

        // ===== requirements.json =====

        /// <summary>解禁条件を読む。ファイルが無ければ null(＝条件なし＝いつでも遊べる)</summary>
        public static DanRequirement? ReadRequirements(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(ChartText.Read(path));
                return ReadRequirement(doc.RootElement, Path.GetDirectoryName(path) ?? "");
            }
            catch (Exception ex)
            {
                // 解決できない条件は「ロックしない」(ドキュメントの決まり)。理由だけ残す
                return new DanRequirement { Type = DanRequirementType.All, Error = $"requirements.json を読めませんでした（{ex.GetType().Name}）" };
            }
        }

        private static DanRequirement ReadRequirement(JsonElement e, string baseFolder, int depth = 0)
        {
            if (e.ValueKind != JsonValueKind.Object || depth > 16)
                return new DanRequirement { Error = "解禁条件の形式が不正か、入れ子が深すぎます" };

            string typeText = Str(e, "type").Trim().ToLowerInvariant();
            DanRequirement req = new();

            switch (typeText)
            {
                case "all":
                case "any":
                    req.Type = typeText == "all" ? DanRequirementType.All : DanRequirementType.Any;
                    if (e.TryGetProperty("conditions", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement child in list.EnumerateArray())
                            req.Children.Add(ReadRequirement(child, baseFolder, depth + 1));
                    // 不正な子条件も残す。評価時に必ずエラーを通知して、ロックしない。
                    if (req.Children.Count == 0) req.Error = $"{typeText} の conditions が空です";
                    return req;

                case "not":
                    req.Type = DanRequirementType.Not;
                    if (e.TryGetProperty("condition", out JsonElement only))
                        req.Children.Add(ReadRequirement(only, baseFolder, depth + 1));
                    if (req.Children.Count == 0) req.Error = "not の condition がありません";
                    return req;

                case "danclear":
                    req.Type = DanRequirementType.DanClear;
                    SetPath(req, e, baseFolder);
                    if (DanClearTypes.ParseDan(Str(e, "clearType")) is { } dan) req.DanClear = dan;
                    else req.Error = "danClear の clearType が読めません";
                    return req;

                case "songclear":
                    req.Type = DanRequirementType.SongClear;
                    SetPath(req, e, baseFolder);
                    if (ParseCourse(Str(e, "course")) is { } clearCourse) req.Course = clearCourse;
                    else req.Error = "songClear の course が読めません";
                    req.SongClear = DanClearTypes.ParseSong(Str(e, "clearType"));
                    if (req.SongClear == SongClearType.None) req.Error = "songClear の clearType が読めません";
                    return req;

                case "songscore":
                    req.Type = DanRequirementType.SongScore;
                    SetPath(req, e, baseFolder);
                    if (ParseCourse(Str(e, "course")) is { } scoreCourse) req.Course = scoreCourse;
                    else req.Error = "songScore の course が読めません";
                    if (e.TryGetProperty("score", out JsonElement score) && score.ValueKind == JsonValueKind.Number && score.TryGetInt64(out long points) && points >= 0)
                        req.Score = points;
                    else req.Error = "songScore の score は0以上の整数で指定してください";
                    return req;

                case "songrank":
                    req.Type = DanRequirementType.SongRank;
                    SetPath(req, e, baseFolder);
                    if (ParseCourse(Str(e, "course")) is { } rankCourse) req.Course = rankCourse;
                    else req.Error = "songRank の course が読めません";
                    if (e.TryGetProperty("rank", out JsonElement rank) && rank.ValueKind == JsonValueKind.Number && rank.TryGetInt32(out int rankValue) && rankValue is >= 1 and <= 7)
                        req.Rank = rankValue;
                    else req.Error = "songRank の rank は1〜7の整数で指定してください";
                    return req;

                default:
                    return new DanRequirement { Type = DanRequirementType.All, Error = $"知らない条件の種類「{typeText}」です" };
            }

            static void SetPath(DanRequirement req, JsonElement e, string baseFolder)
            {
                req.Path = Str(e, "path");
                req.FullPath = req.Path.Length > 0 ? ResolveRelative(baseFolder, req.Path) : "";
                if (req.FullPath.Length == 0) req.Error = "path がありません";
            }
        }

        // requirements.json の course 名(Easy / Normal / Hard / Oni / Ura)
        private static CourseType? ParseCourse(string text) => text.Trim().ToLowerInvariant() switch
        {
            "easy" => CourseType.Easy,
            "normal" => CourseType.Normal,
            "hard" => CourseType.Hard,
            "ura" => CourseType.Edit,
            "oni" => CourseType.Oni,
            _ => null,
        };

        // ===== ChangeEffect =====

        private static void ReadChangeEffects(DanCourse course)
        {
            string root = Path.Combine(course.FolderPath, ChangeEffectFolder);
            if (!Directory.Exists(root)) return;

            foreach (string folder in Directory.EnumerateDirectories(root))
            {
                string configPath = Path.Combine(folder, "Config.json");
                if (!File.Exists(configPath)) continue;
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(ChartText.Read(configPath));
                    JsonElement r = doc.RootElement;
                    if (r.ValueKind != JsonValueKind.Object) continue;
                    course.ChangeEffects.Add(new DanChangeEffect
                    {
                        FolderPath = folder,
                        TransitionIndex = Int(r, "transitionIndex", 0),
                        PlayDefaultScreen = Flag(r, "playDefaultScreen", true),
                        EffectStartMs = Int(r, "effectStartMs", 0),
                        ConditionPanelChangeMs = Int(r, "conditionPanelChangeMs", 0),
                        TitleFadeStartMs = Int(r, "titleFadeStartMs", 0),
                        TitleFadeInDurationMs = Int(r, "titleFadeInDurationMs", 0),
                        TitleDisplayDurationMs = Int(r, "titleDisplayDurationMs", 0),
                        PreviousTitleHideMs = Int(r, "previousTitleHideMs", 0),
                        NextNoteHitMs = Int(r, "nextNoteHitMs", 0),
                    });
                }
                catch { /* 1つ読めなくても段位自体は遊べる */ }
            }
            course.ChangeEffects.Sort((a, b) => a.TransitionIndex.CompareTo(b.TransitionIndex));
        }

        /// <summary>その曲のあとに流す曲間演出(無ければ null)</summary>
        public static DanChangeEffect? ChangeEffectAfter(DanCourse course, int songIndex) =>
            course.ChangeEffects.FirstOrDefault(e => e.TransitionIndex == songIndex);

        // ===== 小さい道具 =====

        /// <summary>相対パスを解く。絶対パスは使わせない決まりなので、フォルダの外に出るものは空にする</summary>
        private static string ResolveRelative(string baseFolder, string relative)
        {
            try
            {
                if (Path.IsPathRooted(relative)) return "";   // 絶対パスは指定できない
                return Path.GetFullPath(Path.Combine(baseFolder, relative.Replace('\\', Path.DirectorySeparatorChar)));
            }
            catch
            {
                return "";
            }
        }

        private static string Str(JsonElement r, string key) =>
            r.TryGetProperty(key, out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";

        private static string[] StrArray(JsonElement r, string key)
        {
            if (!r.TryGetProperty(key, out JsonElement e) || e.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
            List<string> list = new();
            foreach (JsonElement item in e.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s) list.Add(s);
            return list.ToArray();
        }

        private static long Long(JsonElement r, string key, long fallback) =>
            r.TryGetProperty(key, out JsonElement e) && e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out double v) && double.IsFinite(v)
                ? (long)Math.Round(v) : fallback;

        private static int Int(JsonElement r, string key, int fallback)
        {
            long v = Long(r, key, fallback);
            return v is >= int.MinValue and <= int.MaxValue ? (int)v : fallback;
        }

        private static bool Flag(JsonElement r, string key, bool fallback) =>
            r.TryGetProperty(key, out JsonElement e) && e.ValueKind is JsonValueKind.True or JsonValueKind.False ? e.GetBoolean() : fallback;
    }
}
