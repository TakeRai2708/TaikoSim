using System.Text;
using TaikoSim.Charts;

// 段位ファイルのパーサを実データで動かして、読めた中身をそのまま出す道具。
//   dotnet run --project tools/dan-check -- "<段位カテゴリのフォルダ>"
// 読めているかを目で確かめるためのもの。ゲーム本体には入らない。

// Windows のコンソールは既定だと日本語が化けるので、UTF-8 にしておく
try { Console.OutputEncoding = Encoding.UTF8; } catch { /* 変えられない環境もある */ }

string root = args.Length > 0 ? args[0] : ".";
if (!Directory.Exists(root))
{
    Console.WriteLine($"フォルダが見つかりません：{root}");
    return 1;
}

string defPath = Path.Combine(root, DanJson.DefName);
if (!File.Exists(defPath))
{
    Console.WriteLine($"{DanJson.DefName} がありません：{root}");
    return 1;
}

DanCategory? category = DanJson.ReadDef(defPath);
if (category == null)
{
    Console.WriteLine("dan.def を読めませんでした");
    return 1;
}

Console.WriteLine($"=== カテゴリ「{category.Title}」 ===");
Console.WriteLine($"  練習チート禁止：{(category.DisabledCheat ? "はい" : "いいえ")}");
Console.WriteLine();

foreach (string folder in Directory.EnumerateDirectories(root).OrderBy(f => f, StringComparer.Ordinal))
{
    string jsonPath = Path.Combine(folder, DanJson.JsonName);
    if (!File.Exists(jsonPath)) continue;
    category.Courses.Add(DanJson.ReadCourse(jsonPath));
}
category.Courses.Sort((a, b) => a.DanIndex.CompareTo(b.DanIndex));

foreach (DanCourse c in category.Courses)
{
    Console.WriteLine($"── [{c.DanIndex}] {c.Title} ──");
    if (c.Error.Length > 0) Console.WriteLine($"  ⚠ {c.Error}");

    Console.WriteLine($"  課題曲 {c.Songs.Count} 曲");
    for (int i = 0; i < c.Songs.Count; i++)
    {
        DanSong s = c.Songs[i];
        string exists = File.Exists(s.FullPath) ? "○" : "× 見つからない";
        Console.WriteLine($"    {i + 1}. {s.RelativePath}  [{s.Course}] {s.Genre}{(s.IsHidden ? " (伏せる)" : "")}  {exists}");
    }

    string gold = c.HasGoldGauge ? $"金 {c.Gauge.Gold}%" : "金の条件なし";
    Console.WriteLine($"  ゲージ：赤 {c.Gauge.Red}% / {gold}");

    Console.WriteLine($"  合格条件 {c.Exams.Count} 個");
    foreach (DanExam e in c.Exams)
    {
        string range = e.IsLess ? "未満" : "以上";
        if (!e.PerSong)
        {
            DanThreshold t = e.Thresholds[0];
            Console.WriteLine($"    {e.Label}（全体）：赤 {t.Red} {range} / 金 {t.Gold} {range}");
        }
        else
        {
            Console.WriteLine($"    {e.Label}（曲ごと）：");
            for (int i = 0; i < e.Thresholds.Length; i++)
                Console.WriteLine($"      {i + 1}曲目：赤 {e.Thresholds[i].Red} {range} / 金 {e.Thresholds[i].Gold} {range}");
        }
    }

    if (c.MiniPlateText.Length > 0) Console.WriteLine($"  ミニプレート：「{c.MiniPlateText}」");
    foreach ((string name, string path) in new[]
             {
                 ("プレート", c.PlatePath), ("サイド", c.PanelSidePath),
                 ("タイトルプレート", c.TitlePlatePath), ("ミニプレート", c.MiniPlatePath),
             })
    {
        if (path.Length == 0) continue;
        string full = Path.Combine(c.FolderPath, path);
        Console.WriteLine($"  {name}：{path}  {(File.Exists(full) ? "○" : "× 見つからない")}");
    }
    if (c.ShodanTextPaths.Length > 0) Console.WriteLine($"  昇段テキスト 共通 {c.ShodanTextPaths.Length} 枚");
    if (c.ShodanRedTextPaths.Length > 0) Console.WriteLine($"  昇段テキスト 赤 {c.ShodanRedTextPaths.Length} 枚");
    if (c.ShodanGoldTextPaths.Length > 0) Console.WriteLine($"  昇段テキスト 金 {c.ShodanGoldTextPaths.Length} 枚");

    if (c.Requirement != null) Console.WriteLine($"  解禁条件：{Describe(c.Requirement, 0)}");
    foreach (DanChangeEffect fx in c.ChangeEffects)
        Console.WriteLine($"  曲間演出 {fx.TransitionIndex}：標準画面={fx.PlayDefaultScreen} 開始={fx.EffectStartMs}ms パネル={fx.ConditionPanelChangeMs}ms タイトル={fx.TitleFadeStartMs}ms");

    Console.WriteLine($"  → 遊べる：{(c.Playable ? "はい" : "いいえ")}");
    Console.WriteLine();
}

Console.WriteLine($"合計 {category.Courses.Count} 段位、うち遊べる {category.Courses.Count(c => c.Playable)} 個");
return 0;

static string Describe(DanRequirement r, int depth)
{
    string pad = new(' ', depth * 2);
    string error = r.Error.Length > 0 ? $" ⚠{r.Error}" : "";
    return r.Type switch
    {
        DanRequirementType.All or DanRequirementType.Any or DanRequirementType.Not =>
            $"{r.Type}{error}\n" + string.Join("\n", r.Children.Select(c => $"{pad}    - {Describe(c, depth + 1)}")),
        DanRequirementType.DanClear => $"段位クリア {r.Path}（金={r.DanClear.Gold} {r.DanClear.Combo}）{error}",
        DanRequirementType.SongClear => $"曲クリア {r.Path} [{r.Course}] {r.SongClear}{error}",
        DanRequirementType.SongScore => $"曲スコア {r.Path} [{r.Course}] {r.Score}以上{error}",
        DanRequirementType.SongRank => $"曲ランク {r.Path} [{r.Course}] {r.Rank}以上{error}",
        _ => $"?{error}",
    };
}
