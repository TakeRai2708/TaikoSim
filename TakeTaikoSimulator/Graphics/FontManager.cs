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
        private static string skinMainPreparedText = "";

        // スキンの太字フォントを読み直した回数(先読みし直すかどうかの目印)
        public static int SkinMainGeneration { get; private set; }

        // 読み込んだ文字のページの数(システム用＋スキンの太字。デバッグ記録用)
        public static int TotalPageCount => (systemFont?.PageCount ?? 0) + (skinMainFont?.PageCount ?? 0);

        // 使っているフォントの名前(表示用)
        public static string FontName => systemFont != null
            ? $"{Path.GetFileName(systemFont.FilePath)}（ページ {systemFont.PageCount}）"
            : "(default)";

        // 初期化(ウィンドウを作った後に1回呼ぶ)
        public static void Init()
        {
            string? fontPath = FindFontFile();
            if (fontPath == null)
            {
                Console.WriteLine("[Font] Assets/Fonts に .ttf / .otf が無いので標準フォントを使います(日本語は表示できません)");
                return;
            }

            systemFont = DynamicFont.Load(fontPath, InitialCodepoints());
            if (systemFont == null)
            {
                Console.WriteLine($"[Font] {fontPath} を読み込めなかったので標準フォントを使います");
            }
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
        public static void ReloadSkinMainFont(string? skinPath)
        {
            skinMainFont?.Dispose();
            skinMainFont = null;
            SkinMainGeneration++;
            string? path = FindSkinMainFontFile(skinPath);
            if (path != null)
            {
                skinMainFont = DynamicFont.Load(path, InitialCodepoints()
                    .Concat(skinMainPreparedText.EnumerateRunes().Select(rune => rune.Value)));
                if (skinMainFont != null)
                    Console.WriteLine($"[Font] 曲名用：{path}");
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

        // 後片付け(ウィンドウを閉じる前に呼ぶ)
        public static void Unload()
        {
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
