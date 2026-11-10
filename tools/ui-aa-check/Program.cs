using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Raylib_cs;
using TakeTaikoSimulator.Graphics;
using TakeTaikoSimulator.UI;

// Linux/Mesaの画面を作らないOpenGLで、本物の描画コードを実行して画素を読む。
// Windowsのゲーム起動や入力・音声の確認とは別。EGLはこの確認用だけの依存。
if (args.Length < 2) throw new ArgumentException("UiAaCheck <libraylib.so> <font.ttf/otf> [output.png]");
NativeLibrary.SetDllImportResolver(typeof(Raylib).Assembly, (name, _, _) =>
    name == "raylib" ? NativeLibrary.Load(Path.GetFullPath(args[0])) : IntPtr.Zero);
IntPtr display = Egl.eglGetPlatformDisplay(0x31DD, IntPtr.Zero, IntPtr.Zero);
if (Egl.eglInitialize(display, out int major, out int minor) == 0) throw new Exception("EGL initialize: " + Egl.eglGetError());
IntPtr surface = IntPtr.Zero, context = IntPtr.Zero;
bool initialized = false;
RenderTexture2D target = default, reduced = default;
int passed = 0;
void Check(bool value, string description) { if (!value) throw new Exception(description); passed++; }
try
{
    Check(Egl.eglBindAPI(0x30A2) != 0, "OpenGL API");
    int[] attributes = { 0x3033, 1, 0x3040, 8, 0x3024, 8, 0x3023, 8, 0x3022, 8, 0x3021, 8, 0x3038 };
    Check(Egl.eglChooseConfig(display, attributes, out IntPtr config, 1, out int count) != 0 && count > 0, "EGL config");
    surface = Egl.eglCreatePbufferSurface(display, config, new[] { 0x3057, 1, 0x3056, 1, 0x3038 });
    context = Egl.eglCreateContext(display, config, IntPtr.Zero, new[] { 0x3098, 3, 0x30FB, 3, 0x30FD, 1, 0x3038 });
    Check(surface != IntPtr.Zero && context != IntPtr.Zero && Egl.eglMakeCurrent(display, surface, surface, context) != 0, "EGL context");
    unsafe { Rlgl.LoadExtensions((void*)NativeLibrary.GetExport(NativeLibrary.Load("libEGL.so.1"), "eglGetProcAddress")); }
    Rlgl.GlInit(1920, 1080); initialized = true;
    Raylib.SetShapesTexture(new Texture2D { Id = Rlgl.GetTextureIdDefault(), Width = 1, Height = 1, Mipmaps = 1, Format = PixelFormat.UncompressedR8G8B8A8 }, new(0, 0, 1, 1));
    var font = DynamicFont.Load(args[1], new[] { (int)'?' }) ?? throw new Exception("font load");
    typeof(FontManager).GetField("systemFont", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, font);
    target = Raylib.LoadRenderTexture(1920, 1080);
    Raylib.BeginTextureMode(target);
    Raylib.ClearBackground(Color.Black);
    SmoothShapes.Circle(new(70.25f, 70.5f), 13, Color.White);
    SmoothShapes.Ring(new(160.25f, 70.5f), 11.9f, 14.1f, Color.White);
    SmoothShapes.Rectangle(new(220.25f, 60.5f, 180, 6), 3, Color.White);
    SmoothShapes.Triangle(new(450, 60), new(460, 80), new(470, 60), Color.White);
    SmoothShapes.Rectangle(new(500, 60, 40, 30), 6, new Color(255, 255, 255, 128));
    Rlgl.PushMatrix(); Rlgl.Translatef(100, 100, 0);
    SmoothShapes.Circle(new(70.25f, 70.5f), 13, Color.White);
    Rlgl.PopMatrix();
    Raylib.BeginScissorMode(700, 50, 8, 40);
    SmoothShapes.Circle(new(700, 70), 13, Color.White);
    Raylib.EndScissorMode();
    SmoothShapes.Border(new(800.25f, 50.5f, 80, 40), 6, 1, Color.White);
    SmoothShapes.Line(new(920, 52), new(960, 82), 3, Color.White);
    SmoothShapes.Circle(new(70.75f, 370.5f), 13, Color.White);
    // 対照群：同じ単一サンプル描画先に、従来の多角形を描く。
    Raylib.DrawCircleV(new(70.25f, 270.5f), 13, Color.White);
    // 実際の部品も描く。OSの入力取得はせず、初期状態だけを検証する。
    Gui.Checkbox(new(100, 530, 400, 50), "ON", true);
    Gui.Checkbox(new(100, 610, 400, 50), "OFF", false);
    Gui.Slider("aa-test", new(100, 720, 500, 50), new(650, 720, 100, 50), 50, 0, 100);
    SmoothShapes.Ring(new(860, 555), 11.9f, 14.1f, Gui.AccentColor);
    Gui.DrawCentered("?", new(845, 540, 30, 30), 18, Gui.AccentColor);
    Gui.PrimaryButton(new(850, 700, 150, 56), "Apply");
    Rlgl.DrawRenderBatchActive();
    byte[] image = Read(target, args.Length > 2 ? args[2] : null);
    int Pixel(int x, int y) => image[(y * 1920 + x) * 4];
    int Edges(int x, int y, int w, int h)
    {
        int result = 0;
        for (int dy = 0; dy < h; dy++) for (int dx = 0; dx < w; dx++) if (Pixel(x + dx, y + dy) is > 0 and < 255) result++;
        return result;
    }
    Check(Pixel(70, 70) == 255 && Pixel(50, 50) == 0, "丸の中心と四角い余白");
    Check(Edges(50, 50, 40, 40) > 20, "丸の輪郭に中間色がある");
    Check(Edges(50, 250, 40, 40) == 0, "従来描画は同じ描画先ではAAされない");
    Check(Pixel(160, 70) == 0 && Edges(140, 50, 40, 40) > 30, "リングの内側は透明、内外の輪郭はAA");
    Check(Pixel(300, 63) == 255 && Edges(215, 55, 190, 16) > 50, "細いバーの中心と端");
    Check(Pixel(460, 70) == 255 && Pixel(460, 83) == 0 && Edges(445, 55, 30, 30) > 10, "三角の輪郭");
    Check(Math.Abs(Pixel(520, 75) - 128) <= 1, "半透明色を保持");
    bool translated = true;
    for (int y = 50; y < 90; y++) for (int x = 50; x < 90; x++) translated &= Pixel(x, y) == Pixel(x + 100, y + 100);
    Check(translated, "スライドの座標変換で輪郭が崩れない");
    Check(Pixel(700, 70) == 255 && Pixel(698, 70) == 0 && Pixel(715, 70) == 0, "クリップ外を描かない");
    Check(Pixel(840, 70) == 0 && Edges(795, 45, 90, 50) > 50, "細い角丸枠も中を塗りつぶさない");
    Check(Pixel(940, 67) > 200 && Edges(910, 40, 60, 55) > 20, "斜めのチェック線もAA");
    bool subpixelChanged = false;
    for (int y = 50; y < 90; y++) for (int x = 50; x < 90; x++) subpixelChanged |= Pixel(x, y) != Pixel(x, y + 300);
    Check(subpixelChanged, "小数位置の移動を丸めず描く");
    Check(Pixel(478, 555) == 255 && Pixel(448, 635) == 255 && Pixel(350, 745) == 255, "実際のON/OFFノブと音量ノブが表示される");
    Check(Edges(842, 537, 36, 36) > 20, "?の輪郭と通常フォントを同じ描画順で使える");
    Raylib.EndTextureMode();
    reduced = Raylib.LoadRenderTexture(1280, 720);
    Raylib.BeginTextureMode(reduced); Raylib.ClearBackground(Color.Black);
    Raylib.SetTextureFilter(target.Texture, TextureFilter.Bilinear);
    Raylib.DrawTexturePro(target.Texture, new(0, 0, 1920, -1080), new(0, 0, 1280, 720), Vector2.Zero, 0, Color.White);
    Rlgl.DrawRenderBatchActive();
    byte[] small = Read(reduced, null);
    Check(small[(47 * 1280 + 47) * 4] > 240, "1280×720への合成でも丸を保持");
    Raylib.EndTextureMode();
    Console.WriteLine($"PASS: {passed} offscreen OpenGL UI-AA checks (EGL {major}.{minor}; no Windows/audio/input test)");
}
finally
{
    if (initialized)
    {
        FontManager.Unload(); SmoothShapes.Unload();
        if (reduced.Id != 0) Raylib.UnloadRenderTexture(reduced);
        if (target.Id != 0) Raylib.UnloadRenderTexture(target);
        Rlgl.GlClose();
    }
    Egl.eglMakeCurrent(display, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
    if (context != IntPtr.Zero) Egl.eglDestroyContext(display, context);
    if (surface != IntPtr.Zero) Egl.eglDestroySurface(display, surface);
    Egl.eglTerminate(display);
}

static unsafe byte[] Read(RenderTexture2D target, string? output)
{
    Image image = Raylib.LoadImageFromTexture(target.Texture);
    try
    {
        Raylib.ImageFlipVertical(ref image);
        Raylib.ImageFormat(ref image, PixelFormat.UncompressedR8G8B8A8);
        var data = new byte[image.Width * image.Height * 4];
        Marshal.Copy((IntPtr)image.Data, data, 0, data.Length);
        if (output != null) Raylib.ExportImage(image, Path.GetFullPath(output));
        return data;
    }
    finally { Raylib.UnloadImage(image); }
}
