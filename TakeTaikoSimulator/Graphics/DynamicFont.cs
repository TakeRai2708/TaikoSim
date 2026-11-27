using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Raylib_cs;
using TakeTaikoSimulator.Diagnostics;

namespace TakeTaikoSimulator.Graphics
{
    // フォント1つ分(.ttf / .otf)。足りない文字はあとから足す
    //
    // しくみ(SDF フォント)：
    //   ・文字の絵ではなく「文字のふちまでの距離」を画像にしておく(SDF = Signed Distance Field)
    //     描くときにシェーダーが「ふちの内側か外側か」を1ピクセルずつ決める。大きい通常UI文字はCoverageFontで別に作る
    //     (前は大きい絵を縮めて描いていて、小さい文字がにじんでいた)
    //   ・縁取りは、同じ距離の画像を「太らせて」描くだけ。縁 → 文字 の2回で描ける(前は 25回ずらして描いていた)
    //     太らせた形は、角が自然に丸くなる
    //   ・作り方：128px で文字を描く → すき間を埋める → 距離を計算 → 半分(64px)にして 2048×2048 の画像(ページ)に詰める
    //   ・知らない文字(漢字など)が出てきたら、その文字だけ計算して、ページの空いているところに足す
    public sealed class DynamicFont : IDisposable
    {
        // 文字の大きさの基準(ページに入っている文字の大きさ)。幅・位置はこの大きさで覚える
        //   11/27 たけらい「ジャンルの文字全部汚くね？」：48 だと 60px 以上の大きい字で、ふちがガタガタしていた → 64
        public const int BaseSize = 64;
        // この倍の大きさで描いてから距離を計算する(細かいところまで正しい距離にするため)
        private const int RasterScale = 2;
        // 文字のまわりに持っておく距離(ページの px)。縁取りはこれより細くないといけない(基準の大きさの 1/4)
        //   10/30 に 8 → 12：TaikøNauts のサブタイトルは縁が文字の大きさの約 0.16 倍あって、8 だと上限(約 0.16 倍)ぎりぎりで足りなかった
        //   11/27 BaseSize を 48 → 64 にしたので、縁の太さの上限(文字の大きさに対する割合)が変わらないように 12 → 16
        private const int Spread = 16;
        // ページ1枚の大きさ(1文字 96px 四方くらいなので、1枚に約 450 文字)
        private const int PageSize = 2048;
        // ページの中で、文字と文字の間に空けるすき間
        private const int CellGap = 1;

        // 改行したときの行の間(文字の大きさに足す分)
        private const float LineGap = 2f;

        public string FilePath { get; }
        private readonly byte[] fileData;
        private CoverageFont? coverage;
        public void DrawSharp(string text, Vector2 position, float size, Color color) =>
            (coverage ??= new(fileData)).Draw(text, position, size, color);
        public Vector2 MeasureSharp(string text, float size) => (coverage ??= new(fileData)).Measure(text, size);
        public Rectangle InkBoundsSharp(string text, float size) => (coverage ??= new(fileData)).InkBounds(text, size);

        // ページ(画像)と、まだ GPU に送っていない中身
        private sealed class Page
        {
            public readonly byte[] Pixels = new byte[PageSize * PageSize];
            public Texture2D Texture;
            public bool HasTexture;
            public int CursorX = CellGap, CursorY = CellGap, RowHeight;
            public int DirtyTop = int.MaxValue, DirtyBottom = -1; // GPU に送り直す行の範囲
        }
        private readonly List<Page> pages = new();

        // 文字1つ分(大きさ・位置は BaseSize のときの px)
        //   Page が -1 の文字は読み込めなかった文字、-2 は絵の無い文字(空白など。幅だけある)
        private readonly record struct Glyph(int Page, Rectangle Source, float OffsetX, float OffsetY, float Advance);
        private readonly Dictionary<int, Glyph> glyphs = new();

        // ページの数(重さの確認用)
        public int PageCount => pages.Count + (coverage?.PageCount ?? 0);

        private DynamicFont(string path, byte[] data)
        {
            FilePath = path;
            fileData = data;
        }

        // フォントを読み込む(最初に読んでおく文字を渡す)。失敗したら null
        public static DynamicFont? Load(string path, IEnumerable<int> initialCodepoints)
        {
            try
            {
                DynamicFont font = new DynamicFont(path, File.ReadAllBytes(path));
                if (!font.AddGlyphs(initialCodepoints.Distinct().ToArray()))
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

        // ===== 描く =====

        // 文字を描く
        public void Draw(string text, Vector2 position, float size, Color color) => DrawPass(text, position, size, color, 0f);

        // 縁取りした文字を描く(縁 → 文字 の2回)。thickness は縁の太さ(画面の px)
        //   先に文字列全部の縁を描いてから文字を描くので、となりの文字の縁が文字の上にかぶらない
        public void DrawOutlined(string text, Vector2 position, float size, Color fill, Color outline, float thickness)
        {
            if (outline.A > 0 && thickness > 0f) DrawPass(text, position, size, outline, thickness);
            DrawPass(text, position, size, fill, 0f);
        }

        // 縁の太さの上限(画面の px)。ページに持っている距離より太くはできない
        public static float MaxOutline(float size) => Spread * (size / BaseSize) * 0.95f;

        // 1回分。dilate は文字を太らせる量(画面の px。0 ならふつうの文字)
        private void DrawPass(string text, Vector2 position, float size, Color color, float dilate)
        {
            if (text.Length == 0 || size <= 0f) return;
            long started = DebugLog.Begin(); // デバッグ記録：文字を描いた時間と数
            int drawn = 0;
            EnsureGlyphs(text);

            float scale = size / BaseSize;
            float x = position.X;
            float y = position.Y;

            // 距離の 1.0(画像の 0〜255 全部)が画面の何 px か。シェーダーが、ふちの前後 0.5px だけをなめらかにするのに使う
            SdfShader.Begin(2f * Spread * scale, Math.Min(dilate, MaxOutline(size)));
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

                if (!glyphs.TryGetValue(codepoint, out Glyph glyph) || glyph.Page == -1)
                {
                    x += size * 0.5f; // 読み込めなかった文字は、半分の幅だけ空ける
                    continue;
                }

                if (glyph.Page >= 0)
                {
                    Rectangle dest = new Rectangle(x + glyph.OffsetX * scale, y + glyph.OffsetY * scale,
                        glyph.Source.Width * scale, glyph.Source.Height * scale);
                    Raylib.DrawTexturePro(pages[glyph.Page].Texture, glyph.Source, dest, Vector2.Zero, 0f, color);
                    drawn++;
                }
                x += glyph.Advance * scale;
            }
            SdfShader.End();
            DebugLog.CountText(drawn, started);
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

                if (glyphs.TryGetValue(codepoint, out Glyph glyph) && glyph.Page != -1) lineWidth += glyph.Advance * scale;
                else lineWidth += size * 0.5f;
            }

            maxWidth = Math.Max(maxWidth, lineWidth);
            return new Vector2(maxWidth, lines * size + (lines - 1) * LineGap);
        }

        // 文字の送り幅だけでなく、実際に絵がある範囲を返す。小さいプレートの上下揃えに使う。
        public Rectangle InkBounds(string text, float size)
        {
            EnsureGlyphs(text);
            float scale = size / BaseSize, x = 0, y = 0;
            float left = float.PositiveInfinity, top = left, right = float.NegativeInfinity, bottom = right;
            foreach (Rune rune in text.EnumerateRunes())
            {
                if (rune.Value == '\n') { x = 0; y += size + LineGap; continue; }
                if (rune.Value < 0x20) continue;
                if (!glyphs.TryGetValue(rune.Value, out var glyph) || glyph.Page == -1) { x += size * .5f; continue; }
                if (glyph.Page >= 0)
                {
                    left = Math.Min(left, x + (glyph.OffsetX + Spread) * scale);
                    top = Math.Min(top, y + (glyph.OffsetY + Spread) * scale);
                    right = Math.Max(right, x + (glyph.OffsetX + glyph.Source.Width - Spread) * scale);
                    bottom = Math.Max(bottom, y + (glyph.OffsetY + glyph.Source.Height - Spread) * scale);
                }
                x += glyph.Advance * scale;
            }
            return float.IsFinite(left) ? new(left, top, right - left, bottom - top) : new(0, 0, 0, 0);
        }

        // ===== 文字を足す =====

        // たくさんの文字列の文字を、まとめて先に読み込んでおく(選曲画面の曲名など)
        //   描くときに1つずつ足すと、新しい文字が一度にたくさん出たときに止まってしまうため
        //   1回に読む文字が多すぎないように、PreloadChunk 文字ずつに分けて読む
        private const int PreloadChunk = 400;

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
                AddGlyphs(all[i..Math.Min(all.Length, i + PreloadChunk)]);
            Console.WriteLine($"[Font] {all.Length}文字をまとめて読み込みました（{watch.ElapsedMilliseconds} ms、ページ {pages.Count}）");
        }

        // まだ読み込んでいない文字があったら、その文字だけ足す
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

            if (missing != null) AddGlyphs(missing.ToArray());
        }

        // 1文字分の、距離の画像(ページに詰める前)
        private sealed class Prepared
        {
            public int Codepoint;
            public int Width, Height;        // ページの px(BaseSize のとき)
            public float OffsetX, OffsetY;   // 描く位置から、この画像の左上まで
            public float Advance;
            public byte[]? Pixels;           // null なら絵の無い文字(空白など)
        }

        // 渡した文字を読み込んで、距離の画像にして、ページに詰める
        //   フォントに無い文字は「読めない」と覚えておく(何度も読みに行かないように)
        private unsafe bool AddGlyphs(int[] codepoints)
        {
            if (codepoints.Length == 0) return true;
            var watch = System.Diagnostics.Stopwatch.StartNew();

            // ① 128px で文字の絵を作る(Raylib の中の stb_truetype)
            GlyphInfo* infos;
            int count = 0;
            fixed (byte* data = fileData)
            fixed (int* cps = codepoints)
            {
                infos = Raylib.LoadFontData(data, fileData.Length, BaseSize * RasterScale, cps, codepoints.Length,
                    FontType.Default, &count);
            }
            if (infos == null)
            {
                Console.WriteLine($"[Font] {Path.GetFileName(FilePath)} の文字を読み込めませんでした");
                foreach (int codepoint in codepoints) glyphs.TryAdd(codepoint, new Glyph(-1, default, 0, 0, 0));
                return false;
            }

            // ② 絵を取り出す(Raylib のメモリは、ここで写してすぐ返す)
            var sources = new (int Codepoint, int W, int H, int OffX, int OffY, int Advance, byte[] Gray)[count];
            for (int i = 0; i < count; i++)
            {
                GlyphInfo info = infos[i];
                sources[i] = (info.Value, info.Image.Width, info.Image.Height, info.OffsetX, info.OffsetY, info.AdvanceX,
                    CopyGray(info.Image));
            }
            Raylib.UnloadFontData(infos, count);

            // ③ 距離を計算して半分にする(文字ごとに別々なので、CPU のコアを全部使う)
            var prepared = new Prepared[count];
            Parallel.For(0, count, i =>
            {
                var s = sources[i];
                prepared[i] = MakeDistanceGlyph(s.Codepoint, s.W, s.H, s.OffX, s.OffY, s.Advance, s.Gray);
            });

            // ④ ページに詰めて、変わったところだけ GPU に送る
            foreach (Prepared p in prepared) Pack(p);
            foreach (Page page in pages) Upload(page);

            // フォントに無かった文字は「読めない」
            foreach (int codepoint in codepoints) glyphs.TryAdd(codepoint, new Glyph(-1, default, 0, 0, 0));

            DebugLog.Event("文字の追加", $"{Path.GetFileName(FilePath)} に {count}文字を足した（ページ {pages.Count}）",
                watch.Elapsed.TotalMilliseconds, count);
            if (watch.ElapsedMilliseconds >= 30)
                Console.WriteLine($"[Font] 文字を足すのに {watch.ElapsedMilliseconds} ms（{count}文字、ページ {pages.Count}）");
            return true;
        }

        // Raylib の画像から、1ピクセル1バイトの濃さだけを写す
        internal static unsafe byte[] CopyGray(Image image)
        {
            int w = image.Width, h = image.Height;
            if (w <= 0 || h <= 0 || image.Data == null) return Array.Empty<byte>();
            byte* src = (byte*)image.Data;
            var gray = new byte[w * h];
            switch (image.Format)
            {
                case PixelFormat.UncompressedGrayscale:
                    Marshal.Copy((IntPtr)src, gray, 0, w * h);
                    break;
                case PixelFormat.UncompressedGrayAlpha:
                    for (int i = 0; i < w * h; i++) gray[i] = src[i * 2 + 1];
                    break;
                case PixelFormat.UncompressedR8G8B8A8:
                    for (int i = 0; i < w * h; i++) gray[i] = src[i * 4 + 3];
                    break;
                default:
                    return Array.Empty<byte>();
            }
            return gray;
        }

        // 1文字分：128px の絵 → すき間を埋める → 距離 → 半分(64px)
        private static Prepared MakeDistanceGlyph(int codepoint, int w, int h, int offX, int offY, int advance, byte[] gray)
        {
            var result = new Prepared { Codepoint = codepoint, Advance = advance / (float)RasterScale };
            bool any = false;
            for (int i = 0; i < gray.Length && !any; i++) any = gray[i] >= 128;
            if (!any) return result; // 空白など：絵は無くて、幅だけ

            int pad = Spread * RasterScale;
            int hw = w + pad * 2, hh = h + pad * 2;
            if (hw % RasterScale != 0) hw += RasterScale - hw % RasterScale;
            if (hh % RasterScale != 0) hh += RasterScale - hh % RasterScale;

            // 文字の中か外か
            var inside = new bool[hw * hh];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    inside[(y + pad) * hw + x + pad] = gray[y * w + x] >= 128;

            // 線のすき間を埋める（11/27 たけらい「ジャンルの文字全部汚くね？」）
            //   A-Style の Default.otf は、画を重ねて作ってある字（「近」「さ」「加」など）があって、
            //   Raylib の中の stb_truetype で描くと、重なったところに細い黒い線（すき間）が出ていた（FreeType で描くと出ない）。
            //   中か外かを決めたあとで「太らせてから同じだけ細らせる」と、細いすき間だけが埋まり、形はほぼ変わらない
            Close(inside, hw, hh, SeamRadius);

            // 中の点から一番近い「外」まで・外の点から一番近い「中」まで(2乗の距離)
            float[] toOutside = DistanceSquared(inside, hw, hh, target: false);
            float[] toInside = DistanceSquared(inside, hw, hh, target: true);

            // 2×2 をまとめて半分にする。距離も半分(ページの px)にして、0〜255 にする(128 がふち)
            int aw = hw / RasterScale, ah = hh / RasterScale;
            var pixels = new byte[aw * ah];
            float toByte = 255f / (2f * Spread);
            for (int y = 0; y < ah; y++)
            {
                for (int x = 0; x < aw; x++)
                {
                    float sum = 0f;
                    for (int dy = 0; dy < RasterScale; dy++)
                    {
                        for (int dx = 0; dx < RasterScale; dx++)
                        {
                            int i = (y * RasterScale + dy) * hw + x * RasterScale + dx;
                            // 中なら +、外なら -(ふちはピクセルとピクセルの間なので 0.5 ずらす)
                            sum += inside[i] ? MathF.Sqrt(toOutside[i]) - 0.5f : -(MathF.Sqrt(toInside[i]) - 0.5f);
                        }
                    }
                    float distance = sum / (RasterScale * RasterScale) / RasterScale; // ページの px
                    pixels[y * aw + x] = (byte)Math.Clamp(128f + distance * toByte, 0f, 255f);
                }
            }

            result.Width = aw;
            result.Height = ah;
            result.OffsetX = (offX - pad) / (float)RasterScale;
            result.OffsetY = (offY - pad) / (float)RasterScale;
            result.Pixels = pixels;
            return result;
        }

        // すき間を埋める幅（描いた大きさ 128px での px。1 = 3×3 で、太さ1〜2px のすき間が埋まる）
        //   FreeType で描いた Default.otf で試した：2（5×5）だと「最」「題」の字の中の本当のすき間まで埋まった。
        //   1 なら変わるのは字の内側の角が 1px 丸くなるくらい（64px にしたら見えない）
        private const int SeamRadius = 1;

        // 太らせてから細らせる（正方形 (2r+1)×(2r+1)。縦と横に分けて計算）
        private static void Close(bool[] inside, int w, int h, int r)
        {
            if (r <= 0) return;
            bool[] tmp = new bool[inside.Length];
            Spread1D(inside, tmp, w, h, r, grow: true);
            Spread1D(tmp, inside, w, h, r, grow: false);
        }

        // grow=true：まわり r の中に1つでも「中」があれば「中」　grow=false：まわり r が全部「中」のときだけ「中」
        //   はしの外は、grow のときは「外」、細らせるときも「外」とみなす（文字のまわりには余白があるので困らない）
        private static void Spread1D(bool[] src, bool[] dst, int w, int h, int r, bool grow)
        {
            bool[] mid = new bool[src.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool v = !grow;
                    for (int k = -r; k <= r; k++)
                    {
                        int xx = x + k;
                        bool s = xx >= 0 && xx < w && src[y * w + xx];
                        if (grow ? s : !s) { v = grow; break; }
                    }
                    mid[y * w + x] = v;
                }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool v = !grow;
                    for (int k = -r; k <= r; k++)
                    {
                        int yy = y + k;
                        bool s = yy >= 0 && yy < h && mid[yy * w + x];
                        if (grow ? s : !s) { v = grow; break; }
                    }
                    dst[y * w + x] = v;
                }
        }

        // いちばん近い target の点までの距離の2乗(Felzenszwalb の方法。縦 → 横 の順に1列ずつ)
        private static float[] DistanceSquared(bool[] inside, int w, int h, bool target)
        {
            const float Far = 1e20f;
            var grid = new float[w * h];
            for (int i = 0; i < grid.Length; i++) grid[i] = inside[i] == target ? 0f : Far;

            int n = Math.Max(w, h);
            var f = new float[n];
            var d = new float[n];
            var v = new int[n];
            var z = new float[n + 1];

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++) f[y] = grid[y * w + x];
                Transform1D(f, h, d, v, z);
                for (int y = 0; y < h; y++) grid[y * w + x] = d[y];
            }
            for (int y = 0; y < h; y++)
            {
                Array.Copy(grid, y * w, f, 0, w);
                Transform1D(f, w, d, v, z);
                Array.Copy(d, 0, grid, y * w, w);
            }
            return grid;
        }

        private static void Transform1D(float[] f, int n, float[] d, int[] v, float[] z)
        {
            int k = 0;
            v[0] = 0;
            z[0] = float.NegativeInfinity;
            z[1] = float.PositiveInfinity;
            for (int q = 1; q < n; q++)
            {
                float s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2f * q - 2f * v[k]);
                while (s <= z[k])
                {
                    k--;
                    s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2f * q - 2f * v[k]);
                }
                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = float.PositiveInfinity;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                float dq = q - v[k];
                d[q] = dq * dq + f[v[k]];
            }
        }

        // ページの空いているところに詰める(左から右へ、いっぱいになったら次の段へ)
        private void Pack(Prepared p)
        {
            if (p.Pixels == null)
            {
                glyphs[p.Codepoint] = new Glyph(-2, default, 0, 0, p.Advance);
                return;
            }

            int pageIndex = pages.Count > 0 ? pages.Count - 1 : AddPage();
            Page page = pages[pageIndex];
            if (page.CursorX + p.Width + CellGap > PageSize)
            {
                page.CursorX = CellGap;
                page.CursorY += page.RowHeight + CellGap;
                page.RowHeight = 0;
            }
            if (page.CursorY + p.Height + CellGap > PageSize)
            {
                pageIndex = AddPage();
                page = pages[pageIndex];
            }

            int x0 = page.CursorX, y0 = page.CursorY;
            for (int y = 0; y < p.Height; y++)
                Array.Copy(p.Pixels, y * p.Width, page.Pixels, (y0 + y) * PageSize + x0, p.Width);
            page.CursorX += p.Width + CellGap;
            page.RowHeight = Math.Max(page.RowHeight, p.Height);
            page.DirtyTop = Math.Min(page.DirtyTop, y0);
            page.DirtyBottom = Math.Max(page.DirtyBottom, y0 + p.Height);

            glyphs[p.Codepoint] = new Glyph(pageIndex, new Rectangle(x0, y0, p.Width, p.Height), p.OffsetX, p.OffsetY, p.Advance);
        }

        private int AddPage()
        {
            pages.Add(new Page());
            return pages.Count - 1;
        }

        // 変わった行だけ GPU に送る(はじめてのページは、画像を作ってから)
        private static void Upload(Page page)
        {
            if (page.DirtyBottom < 0) return;
            if (!page.HasTexture)
            {
                Image image = Raylib.GenImageColor(PageSize, PageSize, Color.Black);
                Raylib.ImageFormat(ref image, PixelFormat.UncompressedGrayscale);
                page.Texture = Raylib.LoadTextureFromImage(image);
                Raylib.UnloadImage(image);
                // 距離の画像は、なめらかに広げて読む(ミップマップは使わない。SDF なので縮めてもにじまない)
                Raylib.SetTextureFilter(page.Texture, TextureFilter.Bilinear);
                page.HasTexture = true;
                page.DirtyTop = 0;
                page.DirtyBottom = PageSize;
            }
            int top = page.DirtyTop, rows = page.DirtyBottom - top;
            Raylib.UpdateTextureRec(page.Texture, new Rectangle(0, top, PageSize, rows),
                new ReadOnlySpan<byte>(page.Pixels, top * PageSize, rows * PageSize));
            page.DirtyTop = int.MaxValue;
            page.DirtyBottom = -1;
        }

        // 後片付け
        public void Dispose()
        {
            coverage?.Dispose(); coverage = null;
            foreach (Page page in pages)
                if (page.HasTexture) Raylib.UnloadTexture(page.Texture);
            pages.Clear();
            glyphs.Clear();
        }

        // ===== 距離の画像から文字を描くシェーダー =====
        //   pxPerUnit：距離の 1.0(画像の 0〜255 全部)が画面の何 px か
        //   dilate    ：文字を太らせる量(画面の px)。縁取りのときに使う
        //   ふちの前後 0.5px だけを半透明にするので、どの大きさでもくっきりする
        //   11/12 たけらい「フォントがちょっとギザギザしてる。演奏ゲームとかの（大きい）文字はきれい」
        //   → 小さい字のために、1px の中を4か所読んで平均するようにした（大きい字の見た目はほぼ変わらない）
        private static class SdfShader
        {
            private const string Fragment = @"#version 330
in vec2 fragTexCoord;
in vec4 fragColor;
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform float pxPerUnit;
uniform float dilate;
out vec4 finalColor;
float cover(vec2 uv, float w)
{
    float d = (texture(texture0, uv).r - 0.5) * pxPerUnit + dilate;
    return clamp(d / w + 0.5, 0.0, 1.0);
}
void main()
{
    float d = (texture(texture0, fragTexCoord).r - 0.5) * pxPerUnit + dilate;
    float w = max(fwidth(d), 0.00001);
    // 画面の1px の中を4か所（少し回した十字）読んで平均する
    //   小さい字（距離の画像 64px を縮めて描くとき）は、1px に画像の何 px 分も入るので、1か所だけ読むとギザギザになる
    vec2 dx = dFdx(fragTexCoord), dy = dFdy(fragTexCoord);
    float a = 0.25 * (cover(fragTexCoord - 0.375 * dx - 0.125 * dy, w) + cover(fragTexCoord + 0.125 * dx - 0.375 * dy, w)
                    + cover(fragTexCoord + 0.375 * dx + 0.125 * dy, w) + cover(fragTexCoord - 0.125 * dx + 0.375 * dy, w));
    vec4 c = fragColor * colDiffuse;
    finalColor = vec4(c.rgb, c.a * a);
}
";
            private static Shader shader;
            private static bool loaded;
            private static int pxPerUnitLocation, dilateLocation;

            public static void Begin(float pxPerUnit, float dilate)
            {
                if (!loaded)
                {
                    shader = Raylib.LoadShaderFromMemory(null!, GraphicsBackend.Glsl(Fragment));
                    pxPerUnitLocation = Raylib.GetShaderLocation(shader, "pxPerUnit");
                    dilateLocation = Raylib.GetShaderLocation(shader, "dilate");
                    loaded = true;
                }
                // 前に描いたもの(ちがう値で描く文字など)を先に描き終えてから、値を変える
                Raylib.BeginShaderMode(shader);
                Raylib.SetShaderValue(shader, pxPerUnitLocation, pxPerUnit, ShaderUniformDataType.Float);
                Raylib.SetShaderValue(shader, dilateLocation, dilate, ShaderUniformDataType.Float);
            }

            // 描き終わる(ここで GPU に送るので、次の文字の値と混ざらない)
            public static void End() => Raylib.EndShaderMode();
        }
    }
}

