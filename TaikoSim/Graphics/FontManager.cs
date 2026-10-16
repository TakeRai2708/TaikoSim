using System.Numerics;
using System.Text;
using Raylib_cs;

namespace TaikoSim.Graphics
{
    // フォントの係(日本語表示用)
    public static class FontManager
    {
        // フォントを探すフォルダ(exeと同じ場所の Assets/Fonts)
        public static readonly string FontFolder = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");

        // この大きさで読み込んで、表示するときに拡大・縮小する
        private const int BaseSize = 48;

        private static Font font;
        private static bool hasCustomFont = false;   // 自分のフォントを読み込めたか
        private static string? fontPath;            // 使っているフォントファイルの場所

        // 読み込み済みの文字(Unicodeの番号)
        private static readonly HashSet<int> loadedCodepoints = new();

        // 使っているフォントの名前(表示用)
        public static string FontName => hasCustomFont && fontPath != null ? Path.GetFileName(fontPath) : "(default)";

        // 初期化(ウィンドウを作った後に1回呼ぶ)
        public static void Init()
        {
            fontPath = FindFontFile();

            if (fontPath == null)
            {
                Console.WriteLine("[Font] Assets/Fonts に .ttf / .otf が無いので標準フォントを使います(日本語は表示できません)");
                font = Raylib.GetFontDefault();
                return;
            }

            // よく使う文字を最初に読み込んでおく
            AddRange(0x0020, 0x007E); // 英数字・記号
            AddRange(0x3000, 0x303F); // 「」、。などの日本語の記号
            AddRange(0x3040, 0x309F); // ひらがな
            AddRange(0x30A0, 0x30FF); // カタカナ
            AddRange(0xFF00, 0xFFEF); // 全角英数字・半角カナ

            Reload();
        }

        // 文字を表示する(Raylib.DrawText の代わりに使う)
        public static void Draw(string text, float x, float y, float size, Color color)
        {
            if (hasCustomFont) EnsureCodepoints(text);
            Raylib.DrawTextEx(font, text, new Vector2(x, y), size, GetSpacing(size), color);
        }

        // 文字の幅と高さを測る(中央寄せとかで使う)
        public static Vector2 Measure(string text, float size)
        {
            if (hasCustomFont) EnsureCodepoints(text);
            return Raylib.MeasureTextEx(font, text, size, GetSpacing(size));
        }

        // 後片付け(ウィンドウを閉じる前に呼ぶ)
        public static void Unload()
        {
            if (hasCustomFont)
            {
                Raylib.UnloadFont(font);
                hasCustomFont = false;
            }
        }

        // 文字と文字の間隔
        private static float GetSpacing(float size)
        {
            // 自分のフォントは0、標準フォントはRaylibの DrawText と同じ間隔
            return hasCustomFont ? 0f : size / 10f;
        }

        // まだ読み込んでいない文字があったら、追加して読み直す
        private static void EnsureCodepoints(string text)
        {
            bool added = false;
            foreach (Rune rune in text.EnumerateRunes())
            {
                if (rune.Value < 0x20) continue; // 改行などの制御文字は無視
                if (loadedCodepoints.Add(rune.Value)) added = true;
            }

            if (added) Reload();
        }

        // フォントを読み込み直す
        private static void Reload()
        {
            if (fontPath == null) return;

            int[] codepoints = loadedCodepoints.ToArray();
            Font newFont = Raylib.LoadFontEx(fontPath, BaseSize, codepoints, codepoints.Length);

            // 読み込みに失敗するとRaylibは標準フォントを返すので、それで判定する
            if (newFont.Texture.Id == Raylib.GetFontDefault().Texture.Id)
            {
                Console.WriteLine($"[Font] {fontPath} を読み込めませんでした");
                if (!hasCustomFont)
                {
                    font = Raylib.GetFontDefault();
                    fontPath = null;
                }
                return;
            }

            // 古いフォントを捨てて、新しいフォントに入れ替える
            if (hasCustomFont) Raylib.UnloadFont(font);
            font = newFont;
            hasCustomFont = true;

            // 拡大・縮小してもガタガタにならないようにする
            Raylib.SetTextureFilter(font.Texture, TextureFilter.Bilinear);
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

        // 文字の範囲をまとめて追加する
        private static void AddRange(int start, int end)
        {
            for (int c = start; c <= end; c++)
            {
                loadedCodepoints.Add(c);
            }
        }
    }
}