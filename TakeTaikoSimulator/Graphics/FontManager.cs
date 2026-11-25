using System.Numerics;
using System.Text;
using Raylib_cs;

namespace TakeTaikoSimulator.Graphics
{
    // フォントの係(日本語表示用)
    // システム用と、曲名に使うスキンの default フォントを別々に管理する。
    public static class FontManager
    {
        // フォントを探すフォルダ(exeと同じ場所の Assets/Fonts)
        public static readonly string FontFolder = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");

        // システム用のフォント(読み込めなかったら null で、Raylib の標準フォントを使う)
        private static DynamicFont? systemFont;
        private static DynamicFont? skinMainFont;
        // スキンの細かい字のフォント（TaikøNauts の detailFontPath。A-Style は Font/Default2.ttf）
        //   11/13 たけらい「太鼓の達人って二つのフォント使って表示してる。太鼓ノーツもそう」
        //   太い字（曲名・ボタン）は skinMainFont、数字や説明の細かい字はこちら。無ければシステム用のフォント
        private static DynamicFont? skinDetailFont;
        private static string skinMainPreparedText = "";

        // スキンの太字フォントを読み直した回数(先読みし直すかどうかの目印)
        public static int SkinMainGeneration { get; private set; }

        // 読み込んだ文字のページの数(システム用＋スキンの太字。デバッグ記録用)
        public static int TotalPageCount => (systemFont?.PageCount ?? 0) + (skinMainFont?.PageCount ?? 0) + (skinDetailFont?.PageCount ?? 0);

        // 使っているフォントの名前(表示用)
        public static string FontName => systemFont != null
            ? $"{Path.GetFileName(systemFont.FilePath)}（ページ {systemFont.PageCount}）"
            : "(default)";

        // 初期化(ウィンドウを作った後に1回呼ぶ)。スキンを読む前なので、Assets/Fonts のフォントを使う
        public static void Init()
        {
            LoadSystemFont(FindFontFile());
        }

        // スキンを読んだときに呼ぶ。SkinConfig.json の systemFont があればそれ、無ければ Assets/Fonts のフォント
        //   同じファイルなら読み直さない(読み直すと、もう読んだ漢字をまた読むことになるので)
        public static void ReloadSystemFont(string? skinPath, string? configured)
        {
            string? path = ResolveSkinFont(skinPath, configured, "systemFont") ?? FindFontFile();
            if (systemFont != null && path != null && string.Equals(systemFont.FilePath, path, StringComparison.OrdinalIgnoreCase)) return;
            systemFont?.Dispose();
            systemFont = null;
            LoadSystemFont(path);
        }

        private static void LoadSystemFont(string? fontPath)
        {
            if (fontPath == null)
            {
                Console.WriteLine("[Font] Assets/Fonts に .ttf / .otf が無いので標準フォントを使います(日本語は表示できません)");
                return;
            }

            systemFont = DynamicFont.Load(fontPath, InitialCodepoints());
            if (systemFont == null)
                Console.WriteLine($"[Font] {fontPath} を読み込めなかったので標準フォントを使います");
            else
                Console.WriteLine($"[Font] ふつうの字：{fontPath}");
        }

        // SkinConfig.json に書いたフォントの場所を、本当のファイルの場所にする(見つからなければ null)
        //   書き方：スキンのフォルダからの場所("Font/MyFont.otf")。ファイル名だけなら Font・Fonts フォルダも探す
        //   スキンのフォルダの外は読まない("../" などは使えない)
        private static string? ResolveSkinFont(string? skinPath, string? configured, string key)
        {
            if (skinPath == null || string.IsNullOrWhiteSpace(configured)) return null;
            try
            {
                string root = Path.GetFullPath(skinPath);
                string[] candidates =
                {
                    Path.Combine(root, configured),
                    Path.Combine(root, "Font", configured),
                    Path.Combine(root, "Fonts", configured),
                };
                foreach (string candidate in candidates)
                {
                    string full = Path.GetFullPath(candidate);
                    bool inside = full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                    if (inside && File.Exists(full)) return full;
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is IOException)
            {
            }
            Console.WriteLine($"[Font] SkinConfig.json の {key}「{configured}」が見つからないので、いつものフォントを使います");
            return null;
        }

        // 文字を表示する(Raylib.DrawText の代わりに使う)
        public static void Draw(string text, float x, float y, float size, Color color)
        {
            if (systemFont != null)
            {
                systemFont.DrawSharp(text, new Vector2(x, y), size, color);
                return;
            }
            // 標準フォントは Raylib の DrawText と同じ間隔
            Raylib.DrawTextEx(Raylib.GetFontDefault(), text, new Vector2(x, y), size, size / 10f, color);
        }

        // 縁取りした文字(システム用のフォント)。thickness は縁の太さ(画面の px)
        public static void DrawOutlined(string text, float x, float y, float size, Color fill, Color outline, float thickness)
        {
            if (systemFont != null) systemFont.DrawOutlined(text, new Vector2(x, y), size, fill, outline, thickness);
            else DrawOutlinedFallback(text, x, y, size, fill, outline, thickness, Draw);
        }

        // フォントが読めなかったとき(Raylib の標準フォント)は、まわりにずらして描いて縁にする
        private static void DrawOutlinedFallback(string text, float x, float y, float size, Color fill, Color outline,
            float thickness, Action<string, float, float, float, Color> draw)
        {
            if (outline.A > 0 && thickness > 0f)
            {
                for (int i = 0; i < 16; i++)
                {
                    float angle = i * MathF.Tau / 16;
                    draw(text, x + MathF.Cos(angle) * thickness, y + MathF.Sin(angle) * thickness, size, outline);
                }
            }
            draw(text, x, y, size, fill);
        }

        // たくさんの文字列の文字を、まとめて先に読み込む(選曲画面の曲名など。描くときに止まらないように)
        public static void Preload(IEnumerable<string> texts) => systemFont?.Preload(texts);

        // 文字の幅と高さを測る(中央寄せとかで使う)
        public static Vector2 Measure(string text, float size)
        {
            if (systemFont != null) return systemFont.MeasureSharp(text, size);
            return Raylib.MeasureTextEx(Raylib.GetFontDefault(), text, size, size / 10f);
        }

        public static Rectangle MeasureInk(string text, float size) =>
            systemFont?.InkBoundsSharp(text, size) ?? new(0, 0, Measure(text, size).X, size);

        // 起動時とスキン適用時に読む。現在の曲名の漢字もここで準備しておく。
        //   SkinConfig.json の mainFont があればそれ、無ければ Font/default.ttf か .otf
        public static void ReloadSkinMainFont(string? skinPath, string? configured = null)
        {
            skinMainFont?.Dispose();
            skinMainFont = null;
            SkinMainGeneration++;
            string? path = ResolveSkinFont(skinPath, configured, "mainFont") ?? FindSkinMainFontFile(skinPath);
            if (path != null)
            {
                skinMainFont = DynamicFont.Load(path, InitialCodepoints()
                    .Concat(skinMainPreparedText.EnumerateRunes().Select(rune => rune.Value)));
                if (skinMainFont != null)
                    Console.WriteLine($"[Font] 曲名用：{path}");
                else
                    Console.WriteLine($"[Font] 曲名用のフォント {path} を読み込めませんでした(.ttc・.woff などの形式は使えません。.ttf か .otf にしてね)。いつものフォントを使います");
            }
            // 未配置・読み込み失敗時も、システムフォントで曲名を表示できるようにする。
            PrepareSkinMainText(skinMainPreparedText);
        }

        // 曲の開始前に呼ぶ。Draw中に初めて漢字を読み込んで止まらないようにする。
        public static void PrepareSkinMainText(string text)
        {
            skinMainPreparedText = text;
            MeasureSkinMain(text, DynamicFont.BaseSize);
        }

        public static void DrawSkinMain(string text, float x, float y, float size, Color color)
        {
            if (skinMainFont != null)
                skinMainFont.Draw(text, new Vector2(x, y), size, color);
            else Draw(text, x, y, size, color);
        }

        // 縁取りした文字(スキンの太字フォント。無ければシステム用のフォント)
        public static void DrawSkinMainOutlined(string text, float x, float y, float size, Color fill, Color outline, float thickness)
        {
            if (skinMainFont != null) skinMainFont.DrawOutlined(text, new Vector2(x, y), size, fill, outline, thickness);
            else DrawOutlined(text, x, y, size, fill, outline, thickness);
        }

        public static Vector2 MeasureSkinMain(string text, float size) =>
            skinMainFont?.Measure(text, size) ?? Measure(text, size);

        public static Rectangle MeasureSkinMainInk(string text, float size) =>
            skinMainFont?.InkBounds(text, size) ?? systemFont?.InkBounds(text, size) ?? new(0, 0, Measure(text, size).X, size);

        // スキンの太字フォントに、たくさんの文字列の文字をまとめて先に読み込む(選曲画面の曲名など)
        //   スキンにフォントが無いときは、代わりに使うシステム用のフォントに読み込む
        public static void PreloadSkinMain(IEnumerable<string> texts)
        {
            if (skinMainFont != null) skinMainFont.Preload(texts);
            else Preload(texts);
        }

        // ===== スキンの細かい字（detail） =====

        /// <summary>スキンを読んだときに呼ぶ。SkinConfig.json の detailFontPath、無ければ Font/Default2.ttf か .otf</summary>
        public static void ReloadSkinDetailFont(string? skinPath, string? configured = null)
        {
            string? path = ResolveSkinFont(skinPath, configured, "detailFontPath") ?? FindSkinFontFile(skinPath, "default2");
            if (skinDetailFont != null && path != null && string.Equals(skinDetailFont.FilePath, path, StringComparison.OrdinalIgnoreCase)) return;
            skinDetailFont?.Dispose();
            skinDetailFont = null;
            if (path == null) return;
            skinDetailFont = DynamicFont.Load(path, InitialCodepoints());
            if (skinDetailFont != null) Console.WriteLine($"[Font] 細かい字：{path}");
            else Console.WriteLine($"[Font] 細かい字のフォント {path} を読み込めませんでした(.ttf か .otf にしてね)");
        }

        private static string? FindSkinFontFile(string? skinPath, string baseName)
        {
            if (skinPath == null) return null;
            string folder = Path.Combine(skinPath, "Font");
            if (!Directory.Exists(folder)) return null;
            foreach (string file in Directory.EnumerateFiles(folder))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if ((ext == ".ttf" || ext == ".otf") && string.Equals(Path.GetFileNameWithoutExtension(file), baseName, StringComparison.OrdinalIgnoreCase))
                    return file;
            }
            return null;
        }

        /// <summary>スキンに細かい字のフォントがあるか</summary>
        public static bool HasSkinDetail => skinDetailFont != null;

        public static void DrawDetail(string text, float x, float y, float size, Color color)
        {
            if (skinDetailFont != null) skinDetailFont.Draw(text, new Vector2(x, y), size, color);
            else Draw(text, x, y, size, color);
        }

        public static void DrawDetailOutlined(string text, float x, float y, float size, Color fill, Color outline, float thickness)
        {
            if (skinDetailFont != null) skinDetailFont.DrawOutlined(text, new Vector2(x, y), size, fill, outline, thickness);
            else DrawOutlined(text, x, y, size, fill, outline, thickness);
        }

        /// <summary>細かい字のフォントに、文字を先に読み込む（無ければシステム用のフォント）</summary>
        public static void PreloadDetail(IEnumerable<string> texts)
        {
            if (skinDetailFont != null) skinDetailFont.Preload(texts);
            else Preload(texts);
        }

        public static Vector2 MeasureDetail(string text, float size) =>
            skinDetailFont?.Measure(text, size) ?? Measure(text, size);

        public static Rectangle MeasureDetailInk(string text, float size) =>
            skinDetailFont?.InkBounds(text, size) ?? MeasureInk(text, size);

        // 後片付け(ウィンドウを閉じる前に呼ぶ)
        public static void Unload()
        {
            skinDetailFont?.Dispose();
            skinDetailFont = null;
            skinMainFont?.Dispose();
            skinMainFont = null;
            skinMainPreparedText = "";
            systemFont?.Dispose();
            systemFont = null;
        }

        // 最初に読んでおく文字(よく使うもの)
        private static IEnumerable<int> InitialCodepoints()
        {
            IEnumerable<int> Range(int start, int end) => Enumerable.Range(start, end - start + 1);

            return Range(0x0020, 0x007E)          // 英数字・記号
                .Concat(Range(0x3000, 0x303F))   // 「」、。などの日本語の記号
                .Concat(Range(0x3040, 0x309F))   // ひらがな
                .Concat(Range(0x30A0, 0x30FF))   // カタカナ
                .Concat(Range(0xFF00, 0xFFEF));  // 全角英数字・半角カナ
        }

        // Font / Fonts フォルダを優先し、スキン直下にも対応する。default2 は選ばない。
        private static string? FindSkinMainFontFile(string? skinPath)
        {
            if (skinPath == null || !Directory.Exists(skinPath)) return null;
            try
            {
                IEnumerable<string> folders = Directory.EnumerateDirectories(skinPath)
                    .Where(folder => Path.GetFileName(folder).Equals("Font", StringComparison.OrdinalIgnoreCase)
                        || Path.GetFileName(folder).Equals("Fonts", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase)
                    .Append(skinPath);
                foreach (string folder in folders)
                {
                    string? path = Directory.EnumerateFiles(folder)
                        .Where(file => Path.GetFileNameWithoutExtension(file).Equals("default", StringComparison.OrdinalIgnoreCase)
                            && (Path.GetExtension(file).Equals(".ttf", StringComparison.OrdinalIgnoreCase)
                                || Path.GetExtension(file).Equals(".otf", StringComparison.OrdinalIgnoreCase)))
                        .OrderBy(file => Path.GetExtension(file).Equals(".ttf", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                        .ThenBy(file => file, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();
                    if (path != null) return path;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Console.WriteLine($"[Font] スキンの曲名用フォントを探せませんでした：{ex.Message}");
            }
            return null;
        }

        // Assets/Fonts の中から .ttf か .otf を探す(複数あったら名前順で最初のもの)
        private static string? FindFontFile()
        {
            if (!Directory.Exists(FontFolder)) return null;

            return Directory.GetFiles(FontFolder)
                .Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f)
                .FirstOrDefault();
        }
    }
}
