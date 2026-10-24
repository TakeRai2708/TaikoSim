using System.Numerics;
using System.Text;
using Raylib_cs;

namespace TaikoSim.Graphics
{
    // フォント1つ分(.ttf / .otf)。足りない文字はあとから「ページ」を足して読み込む
    //
    // しくみ：
    //   ・フォントのファイルは最初に1回だけ読んで、メモリに置いておく
    //   ・最初によく使う文字(英数字・ひらがな・カタカナなど)を1ページ目として読む
    //   ・知らない文字(漢字など)が出てきたら、その文字だけの小さいページを1枚足す
    //     (前は全部の文字を読み直していたので、文字が増えるほど重くなっていた)
    //   ・描くときは1文字ずつ「どのページにあるか」を見て描く
    public sealed class DynamicFont : IDisposable
    {
        // この大きさで読み込んで、表示するときに拡大・縮小する
        public const int BaseSize = 48;

        // 改行したときの行の間(文字の大きさに足す分)
        private const float LineGap = 2f;

        // フォントのファイルの場所と中身
        public string FilePath { get; }
        private readonly byte[] fileData;
        private readonly string fileType; // ".ttf" など

        // 読み込んだページ
        private readonly List<Font> pages = new();

        // 文字ごとの「どのページにあるか」と「次の文字までの幅(BaseSize のとき)」
        // ページが -1 の文字は、読み込めなかった文字(表示しない)
        private readonly Dictionary<int, (int Page, float Advance)> glyphs = new();

        // ページの数(重さの確認用)
        public int PageCount => pages.Count;

        private DynamicFont(string path, byte[] data)
        {
            FilePath = path;
            fileData = data;
            fileType = Path.GetExtension(path).ToLowerInvariant();
        }

        // フォントを読み込む(最初に読んでおく文字を渡す)。失敗したら null
        public static DynamicFont? Load(string path, IEnumerable<int> initialCodepoints)
        {
            try
            {
                DynamicFont font = new DynamicFont(path, File.ReadAllBytes(path));
                if (!font.AddPage(initialCodepoints.Distinct().ToArray()))
                {
                    font.Dispose();
                    return null;
                }
                return font;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Console.WriteLine($"[Font] {path} を読めませんでした: {ex.Message}");
                return null;
            }
        }

        // 文字を描く
        public void Draw(string text, Vector2 position, float size, Color color)
        {
            EnsureGlyphs(text);

            float scale = size / BaseSize;
            float x = position.X;
            float y = position.Y;

            foreach (Rune rune in text.EnumerateRunes())
            {
                int codepoint = rune.Value;

                if (codepoint == '\n')
                {
                    x = position.X;
                    y += size + LineGap;
                    continue;
                }
                if (codepoint < 0x20) continue; // ほかの制御文字は無視

                if (!glyphs.TryGetValue(codepoint, out var glyph) || glyph.Page < 0)
                {
                    x += size * 0.5f; // 読み込めなかった文字は、半分の幅だけ空ける
                    continue;
                }

                if (codepoint != ' ' && codepoint != '\t')
                {
                    Raylib.DrawTextCodepoint(pages[glyph.Page], codepoint, new Vector2(x, y), size, color);
                }
                x += glyph.Advance * scale;
            }
        }

        // 文字の幅と高さを測る
        public Vector2 Measure(string text, float size)
        {
            EnsureGlyphs(text);

            float scale = size / BaseSize;
            float lineWidth = 0f;
            float maxWidth = 0f;
            int lines = 1;

            foreach (Rune rune in text.EnumerateRunes())
            {
                int codepoint = rune.Value;

                if (codepoint == '\n')
                {
                    maxWidth = Math.Max(maxWidth, lineWidth);
                    lineWidth = 0f;
                    lines++;
                    continue;
                }
                if (codepoint < 0x20) continue;

                if (glyphs.TryGetValue(codepoint, out var glyph) && glyph.Page >= 0) lineWidth += glyph.Advance * scale;
                else lineWidth += size * 0.5f;
            }

            maxWidth = Math.Max(maxWidth, lineWidth);
            return new Vector2(maxWidth, lines * size + (lines - 1) * LineGap);
        }

        // たくさんの文字列の文字を、まとめて先に読み込んでおく(選曲画面の曲名など)
        //   描くときに1つずつページを足すと、新しい文字が一度にたくさん出たときに止まってしまうため
        //   1ページが大きくなりすぎないように、PreloadChunk 文字ずつに分けて読む
        private const int PreloadChunk = 1000;

        public void Preload(IEnumerable<string> texts)
        {
            HashSet<int> missing = new();
            foreach (string text in texts)
            {
                foreach (Rune rune in text.EnumerateRunes())
                {
                    int codepoint = rune.Value;
                    if (codepoint >= 0x20 && !glyphs.ContainsKey(codepoint)) missing.Add(codepoint);
                }
            }
            if (missing.Count == 0) return;

            var watch = System.Diagnostics.Stopwatch.StartNew();
            int[] all = missing.OrderBy(c => c).ToArray();
            for (int i = 0; i < all.Length; i += PreloadChunk)
            {
                int[] chunk = all[i..Math.Min(all.Length, i + PreloadChunk)];
                if (!AddPage(chunk))
                {
                    foreach (int codepoint in chunk) glyphs[codepoint] = (-1, 0f);
                }
            }
            Console.WriteLine($"[Font] {all.Length}文字をまとめて読み込みました（{watch.ElapsedMilliseconds} ms、ページ {pages.Count}）");
        }

        // まだ読み込んでいない文字があったら、その文字だけのページを足す
        private void EnsureGlyphs(string text)
        {
            List<int>? missing = null;

            foreach (Rune rune in text.EnumerateRunes())
            {
                int codepoint = rune.Value;
                if (codepoint < 0x20 || glyphs.ContainsKey(codepoint)) continue;

                missing ??= new List<int>();
                if (!missing.Contains(codepoint)) missing.Add(codepoint);
            }

            if (missing == null) return;

            if (!AddPage(missing.ToArray()))
            {
                // 読み込めなかった文字は、何度も読みに行かないように覚えておく
                foreach (int codepoint in missing) glyphs[codepoint] = (-1, 0f);
            }
        }

        // 渡した文字だけのページを1枚読み込む
        private bool AddPage(int[] codepoints)
        {
            if (codepoints.Length == 0) return true;

            var watch = System.Diagnostics.Stopwatch.StartNew();
            Font page = Raylib.LoadFontFromMemory(fileType, fileData, BaseSize, codepoints, codepoints.Length);
            // 重かったときは記録しておく(止まる原因を調べるため)
            if (watch.ElapsedMilliseconds >= 30)
                Console.WriteLine($"[Font] ページを1枚足すのに {watch.ElapsedMilliseconds} ms（{codepoints.Length}文字、ページ {pages.Count + 1}）");

            // 読み込みに失敗すると、Raylib は標準フォントを返すので、それで判定する
            if (page.Texture.Id == 0 || page.Texture.Id == Raylib.GetFontDefault().Texture.Id)
            {
                Console.WriteLine($"[Font] {Path.GetFileName(FilePath)} の文字を読み込めませんでした");
                return false;
            }

            // 拡大・縮小してもガタガタにならないようにする
            Raylib.SetTextureFilter(page.Texture, TextureFilter.Bilinear);

            int pageIndex = pages.Count;
            pages.Add(page);

            // 文字ごとに「このページにある」と「幅」を覚えておく(毎回調べなくていいように)
            foreach (int codepoint in codepoints)
            {
                GlyphInfo info = Raylib.GetGlyphInfo(page, codepoint);
                float advance = info.AdvanceX != 0 ? info.AdvanceX : Raylib.GetGlyphAtlasRec(page, codepoint).Width;
                glyphs[codepoint] = (pageIndex, advance);
            }
            return true;
        }

        // 後片付け
        public void Dispose()
        {
            foreach (Font page in pages) Raylib.UnloadFont(page);
            pages.Clear();
            glyphs.Clear();
        }
    }
}