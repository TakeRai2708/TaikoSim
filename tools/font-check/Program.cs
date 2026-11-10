using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using TakeTaikoSimulator.Graphics;

// ウィンドウもGPUも使わず、実際のRaylibで文字の濃さとページ境界を確認する。
// 引数：フォントファイル [そのOS用のraylib共有ライブラリ]
if (args.Length == 0) throw new ArgumentException("FontCheck <font.ttf/otf> [raylib library]");
Assembly assembly = typeof(DynamicFont).Assembly;
if (args.Length > 1)
    NativeLibrary.SetDllImportResolver(assembly, (name, _, _) => name == "raylib" ? NativeLibrary.Load(Path.GetFullPath(args[1])) : IntPtr.Zero);
Type type = assembly.GetType("TakeTaikoSimulator.Graphics.CoverageFont", throwOnError: true)!;
var font = Activator.CreateInstance(type, new object[] { File.ReadAllBytes(args[0]) })!;
MethodInfo measure = type.GetMethod("Measure")!;
MethodInfo inkBounds = type.GetMethod("InkBounds")!;
MethodInfo rasterSize = type.GetMethod("RasterSize", BindingFlags.NonPublic | BindingFlags.Static)!;
var sizesField = type.GetField("sizes", BindingFlags.NonPublic | BindingFlags.Instance)!;
int passed = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception(label); passed++; }
Vector2 Measure(string text, float size) => (Vector2)measure.Invoke(font, new object[] { text, size })!;
try
{
    foreach (float size in new[] { 16f, 21, 24, 26, 28, 30, 32, 38, 40, 48, 52, 64, 80, 96 })
    {
        int raster = (int)rasterSize.Invoke(null, new object[] { size })!;
        Check(raster >= size, $"{size}pxを小さい文字画像から拡大しない");
        Vector2 measured = Measure("初期設定　読み込み中　TTS 100%", size);
        Check(measured.X > size * 5 && measured.Y == size, $"{size}pxの測定");
        Check(Measure("一時停止\n名前を変更", size).Y == size * 2 + 2, "改行の高さ");
        object ink = inkBounds.Invoke(font, new object[] { "名前を変更", size })!;
        object spaced = inkBounds.Invoke(font, new object[] { " 名前を変更 ", size })!;
        float Width(object rectangle) => (float)rectangle.GetType().GetField("Width")!.GetValue(rectangle)!;
        Check(Math.Abs(Width(ink) - Width(spaced)) < .01f, "中央揃えの字形範囲に透明な空白を含めない");
    }
    string many = string.Concat(Enumerable.Range(0x4e00, 600).Select(char.ConvertFromUtf32));
    Measure(many, 96);
    var caches = (IDictionary)sizesField.GetValue(font)!;
    int pages = 0;
    foreach (DictionaryEntry cacheEntry in caches)
    {
        object cache = cacheEntry.Value!;
        var atlasPages = (IList)cache.GetType().GetField("Pages")!.GetValue(cache)!;
        var glyphs = (IDictionary)cache.GetType().GetField("Glyphs")!.GetValue(cache)!;
        pages += atlasPages.Count;
        foreach (DictionaryEntry glyphEntry in glyphs)
        {
            object glyph = glyphEntry.Value!;
            int page = (int)glyph.GetType().GetProperty("Page")!.GetValue(glyph)!;
            if (page < 0) continue;
            object rectangle = glyph.GetType().GetProperty("Source")!.GetValue(glyph)!;
            Type rectType = rectangle.GetType();
            float Value(string property) => (float)rectType.GetField(property)!.GetValue(rectangle)!;
            Check(Value("X") >= 1 && Value("Y") >= 1 && Value("X") + Value("Width") < 1024 && Value("Y") + Value("Height") < 1024,
                "文字がアトラス境界からはみ出さない");
        }
        bool intermediate = false, visible = false;
        foreach (object page in atlasPages)
        {
            var pixels = (byte[])page.GetType().GetField("Pixels")!.GetValue(page)!;
            for (int i = 1; i < pixels.Length; i += 2)
            { intermediate |= pixels[i] is > 0 and < 255; visible |= pixels[i] >= 128; }
        }
        Check(intermediate && visible, $"文字のアンチエイリアスを二値化せず保持（{cacheEntry.Key}px）");
    }
    Check(pages > caches.Count, "大量の文字を複数ページへ安全に追加");
    int oldPages = pages;
    Measure(many, 96);
    Check((int)type.GetProperty("PageCount")!.GetValue(font)! == oldPages, "同じ文字は再生成しない");
    Console.WriteLine($"PASS: {passed} font/raster/atlas checks ({caches.Count} sizes, {pages} CPU pages; no GPU draw)");
}
finally { ((IDisposable)font).Dispose(); }
