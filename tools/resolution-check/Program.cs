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
    var begin = typeof(Screen).GetMethod("BeginCanvas", BindingFlags.NonPublic | BindingFlags.Static)!;
    var canvas = typeof(Screen).GetField("canvas", BindingFlags.NonPublic | BindingFlags.Static)!;
    var sizeMethod = typeof(DynamicFont).Assembly.GetType("TakeTaikoSimulator.Graphics.CoverageFont")!.GetMethod("RasterSize", BindingFlags.NonPublic | BindingFlags.Static)!;
    foreach (var dimensions in new[] { (1920,1080), (1280,720), (960,540), (1742,980), (1200,900), (1280,720) })
    {
        int w=dimensions.Item1, h=dimensions.Item2;
        begin.Invoke(null, new object[] {w,h});
        Raylib.ClearBackground(new Color(20,30,40,255));
        float density=Screen.TextRasterScale;
        Check(Math.Abs(density-Math.Min(w/1920f,h/1080f))<.0001, "最終描画倍率");
        Check((int)sizeMethod.Invoke(null,new object[]{26f})! == (int)MathF.Ceiling(26*density), "文字の生成サイズが最終画素数に一致");
        Vector2 aligned=Screen.SnapTextPosition(new(101.3f,101.7f));
        float offsetX=(w-1920*density)/2, offsetY=(h-1080*density)/2;
        Check(Math.Abs((aligned.X*density+offsetX)-MathF.Round(aligned.X*density+offsetX))<.001, "文字を物理画素へ揃える");
        FontManager.Draw("設定 / FPS / 読み込み中",100,100,26,Color.White);
        FontManager.DrawOutlined("TTS",100,200,40,Color.White,Color.Black,2);
        Screen.BeginScissorMode(500,100,100,100);
        SmoothShapes.Rectangle(new(400,100,300,100),0,Color.White);
        Screen.EndScissorMode();
        Screen.EndCanvas();
        var rt=(RenderTexture2D)canvas.GetValue(null)!;
        Check(rt.Texture.Width==w && rt.Texture.Height==h, "描画先は固定1080pではない");
        var pixels=Read(rt,null);
        int At(float x,float y)=>pixels[((int)(offsetY+y*density)*w+(int)(offsetX+x*density))*4];
        Check(At(550,150)>240 && At(450,150)==20 && At(650,150)==20, "スクロール・2Pの切り取り位置を維持");
        Check(!Screen.IsDrawingCanvas && Screen.TextRasterScale==1, "画面外の通知描画へ倍率を持ち越さない");
        if(h==900) Check(pixels[0]==0, "縦長ウィンドウの黒帯");
    }
    Screen.Unload();
    Console.WriteLine($"PASS: {passed} native-resolution rendering checks (EGL {major}.{minor}; no Windows/audio/input test)");
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

