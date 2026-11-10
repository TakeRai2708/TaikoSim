using System.Numerics;
using System.Text;
using Raylib_cs;
using TakeTaikoSimulator.Diagnostics;

namespace TakeTaikoSimulator.Graphics;

// 設定・セットアップ・起動案内の文字。表示サイズ以上の解像度で字形を作り、小さい画像を拡大しない。
// 太い縁取りを使うゲーム文字は、これまでどおりDynamicFontのSDFで描く。
internal sealed class CoverageFont(byte[] fileData) : IDisposable
{
    private const int PageSize = 1024;
    private static readonly int[] SizeSteps = { 16, 20, 24, 28, 32, 40, 48, 64, 80, 96, 128 };
    private sealed class Page
    {
        public readonly byte[] Pixels = new byte[PageSize * PageSize * 2];
        public Texture2D Texture;
        public int X = 1, Y = 1, RowHeight, DirtyTop = int.MaxValue, DirtyBottom;
    }
    private readonly record struct Glyph(int Page, Rectangle Source, int X, int Y, int Advance);
    private sealed class Cache
    {
        public readonly Dictionary<int, Glyph> Glyphs = new();
        public readonly List<Page> Pages = new();
    }
    private readonly Dictionary<int, Cache> sizes = new();
    public int PageCount => sizes.Values.Sum(c => c.Pages.Count);

    // サイズが少し変わるたびにページを増やさない。必ず表示サイズ以上の区切りを使う。
    internal static int RasterSize(float size)
    {
        foreach (int value in SizeSteps)
            if (size <= value) return value;
        return (int)MathF.Ceiling(size);
    }
    private Cache Prepare(string text, int size)
    {
        if (!sizes.TryGetValue(size, out var cache)) sizes[size] = cache = new();
        List<int>? missing = null;
        foreach (Rune rune in text.EnumerateRunes())
            if (rune.Value >= 32 && !cache.Glyphs.ContainsKey(rune.Value))
            { missing ??= new(); if (!missing.Contains(rune.Value)) missing.Add(rune.Value); }
        if (missing != null) Add(cache, size, missing.ToArray());
        return cache;
    }
    private unsafe void Add(Cache cache, int size, int[] codepoints)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int count = 0;
        GlyphInfo* infos;
        fixed (byte* bytes = fileData)
        fixed (int* values = codepoints)
            infos = Raylib.LoadFontData(bytes, fileData.Length, size, values, codepoints.Length, FontType.Default, &count);
        if (infos == null) return;
        try
        {
            for (int i = 0; i < count; i++)
            {
                GlyphInfo info = infos[i];
                int w = info.Image.Width, h = info.Image.Height;
                var advance = info.AdvanceX > 0 ? info.AdvanceX : w;
                if (w <= 0 || h <= 0 || info.Image.Data == null)
                { cache.Glyphs[info.Value] = new(-1, default, 0, 0, advance); continue; }
                if (w + 2 > PageSize || h + 2 > PageSize) continue;
                Page page;
                if (cache.Pages.Count == 0) cache.Pages.Add(new());
                page = cache.Pages[^1];
                if (page.X + w + 1 > PageSize) { page.X = 1; page.Y += page.RowHeight + 2; page.RowHeight = 0; }
                if (page.Y + h + 1 > PageSize) { cache.Pages.Add(new()); page = cache.Pages[^1]; }
                // LoadFontDataのアンチエイリアス（濃さ）をそのまま透明度に使う。
                // Grayscaleの画像は濃さがRにあるので、単なる形式変換でA=255にしない。
                byte[] pixels = DynamicFont.CopyGray(info.Image);
                if (pixels.Length != w * h) continue;
                if (!pixels.Any(value => value > 0))
                { cache.Glyphs[info.Value] = new(-1, default, 0, 0, advance); continue; }
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int dest = ((page.Y + y) * PageSize + page.X + x) * 2;
                        page.Pixels[dest] = 255;
                        page.Pixels[dest + 1] = pixels[y * w + x];
                    }
                cache.Glyphs[info.Value] = new(cache.Pages.Count - 1, new(page.X, page.Y, w, h), info.OffsetX, info.OffsetY, advance);
                page.DirtyTop = Math.Min(page.DirtyTop, page.Y);
                page.DirtyBottom = Math.Max(page.DirtyBottom, page.Y + h);
                page.X += w + 2; page.RowHeight = Math.Max(page.RowHeight, h);
            }
            foreach (int codepoint in codepoints) cache.Glyphs.TryAdd(codepoint, new(-1, default, 0, 0, size / 2));
        }
        finally { Raylib.UnloadFontData(infos, count); }
        DebugLog.Event("UI文字の追加", $"{size}px / {count}文字（ページ {cache.Pages.Count}）", watch.Elapsed.TotalMilliseconds, count);
    }
    public Vector2 Measure(string text, float size)
    {
        if (text.Length == 0 || size <= 0) return new(0, Math.Max(0, size));
        int pixels = RasterSize(size);
        Cache cache = Prepare(text, pixels);
        float scale = size / pixels, width = 0, maximum = 0;
        int lines = 1;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (rune.Value == '\n') { maximum = Math.Max(maximum, width); width = 0; lines++; }
            else if (rune.Value >= 32 && cache.Glyphs.TryGetValue(rune.Value, out var glyph)) width += glyph.Advance * scale;
        }
        return new(Math.Max(maximum, width), lines * size + (lines - 1) * 2);
    }
    public Rectangle InkBounds(string text, float size)
    {
        int pixels = RasterSize(size);
        Cache cache = Prepare(text, pixels);
        float scale = size / pixels, x = 0, y = 0;
        float left = float.PositiveInfinity, top = left, right = float.NegativeInfinity, bottom = right;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (rune.Value == '\n') { x = 0; y += size + 2; continue; }
            if (rune.Value < 32 || !cache.Glyphs.TryGetValue(rune.Value, out var glyph)) continue;
            if (glyph.Page >= 0)
            {
                left = Math.Min(left, x + glyph.X * scale); top = Math.Min(top, y + glyph.Y * scale);
                right = Math.Max(right, x + (glyph.X + glyph.Source.Width) * scale);
                bottom = Math.Max(bottom, y + (glyph.Y + glyph.Source.Height) * scale);
            }
            x += glyph.Advance * scale;
        }
        return float.IsFinite(left) ? new(left, top, right - left, bottom - top) : new(0, 0, 0, 0);
    }
    public void Draw(string text, Vector2 position, float size, Color color)
    {
        if (text.Length == 0 || size <= 0) return;
        long started = DebugLog.Begin();
        int pixels = RasterSize(size), drawn = 0;
        Cache cache = Prepare(text, pixels);
        foreach (Page page in cache.Pages) Upload(page);
        float scale = size / pixels, x = position.X, y = position.Y;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (rune.Value == '\n') { x = position.X; y += size + 2; continue; }
            if (rune.Value < 32 || !cache.Glyphs.TryGetValue(rune.Value, out var glyph)) continue;
            if (glyph.Page >= 0)
            {
                Raylib.DrawTexturePro(cache.Pages[glyph.Page].Texture, glyph.Source,
                    new(x + glyph.X * scale, y + glyph.Y * scale, glyph.Source.Width * scale, glyph.Source.Height * scale), Vector2.Zero, 0, color);
                drawn++;
            }
            x += glyph.Advance * scale;
        }
        DebugLog.CountText(drawn, started);
    }
    private static void Upload(Page page)
    {
        if (page.DirtyBottom == 0) return;
        if (page.Texture.Id == 0)
        {
            Image image = Raylib.GenImageColor(PageSize, PageSize, Color.Blank);
            Raylib.ImageFormat(ref image, PixelFormat.UncompressedGrayAlpha);
            page.Texture = Raylib.LoadTextureFromImage(image);
            Raylib.UnloadImage(image);
            Raylib.SetTextureFilter(page.Texture, TextureFilter.Bilinear);
            page.DirtyTop = 0; page.DirtyBottom = PageSize;
        }
        int top = page.DirtyTop, rows = page.DirtyBottom - top;
        Raylib.UpdateTextureRec(page.Texture, new(0, top, PageSize, rows), new ReadOnlySpan<byte>(page.Pixels, top * PageSize * 2, rows * PageSize * 2));
        page.DirtyTop = int.MaxValue; page.DirtyBottom = 0;
    }
    public void Dispose()
    {
        foreach (var cache in sizes.Values)
            foreach (var page in cache.Pages) if (page.Texture.Id != 0) Raylib.UnloadTexture(page.Texture);
        sizes.Clear();
    }
}
