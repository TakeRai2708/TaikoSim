using Raylib_cs;

namespace TakeTaikoSimulator.Platform;

// exeにはICO、起動後のウィンドウには同じ絵のPNGを使う。
internal static class AppIcon
{
    public static void Apply()
    {
        // 埋め込みなので、作業フォルダやスキンに関係なく読み込める。
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("TakeTaikoSimulator.AppIcon.png");
        if (stream == null) { Console.WriteLine("[Icon] 埋め込み画像が見つかりません"); return; }
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        Image image = Raylib.LoadImageFromMemory(".png", bytes.ToArray());
        if (image.Width <= 0 || image.Height <= 0) { Console.WriteLine("[Icon] 画像を読み込めませんでした"); return; }
        try { Raylib.SetWindowIcon(image); }
        finally { Raylib.UnloadImage(image); }
    }
}
