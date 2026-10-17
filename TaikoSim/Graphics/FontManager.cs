using System.Numerics;
using Raylib_cs;

namespace TaikoSim.Graphics
{
    // フォントの係(日本語表示用)
    // 今はシステム用のフォント(Assets/Fonts の NotoSansJP)だけ。スキンのフォントはあとで足す
    public static class FontManager
    {
        // フォントを探すフォルダ(exeと同じ場所の Assets/Fonts)
        public static readonly string FontFolder = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");

        // システム用のフォント(読み込めなかったら null で、Raylib の標準フォントを使う)
        private static DynamicFont? systemFont;

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
                systemFont.Draw(text, new Vector2(x, y), size, color);
                return;
            }
            // 標準フォントは Raylib の DrawText と同じ間隔
            Raylib.DrawTextEx(Raylib.GetFontDefault(), text, new Vector2(x, y), size, size / 10f, color);
        }

        // 文字の幅と高さを測る(中央寄せとかで使う)
        public static Vector2 Measure(string text, float size)
        {
            if (systemFont != null) return systemFont.Measure(text, size);
            return Raylib.MeasureTextEx(Raylib.GetFontDefault(), text, size, size / 10f);
        }

        // 後片付け(ウィンドウを閉じる前に呼ぶ)
        public static void Unload()
        {
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