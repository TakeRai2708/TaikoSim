using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Raylib_cs;
using TakeTaikoSimulator.Graphics;
using TakeTaikoSimulator.UI;

// Linux/Mesaの画面を作らないOpenGLで、本物の描画コードを実行して画素を読む。
// Windowsのゲーム起動や入力・音声の確認とは別。EGLはこの確認用だけの依存。
if (args.Length < 1) throw new ArgumentException("RecordRenderCheck <libraylib.so>");
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
    Image atlas = Raylib.GenImageColor(200, 50, new Color(0,0,0,0));
    for (int i = 0; i < 10; i++) Raylib.ImageDrawRectangle(ref atlas, i * 20 + 10, 30, 6, 10, Color.White);
    Texture2D texture = Raylib.LoadTextureFromImage(atlas); Raylib.UnloadImage(atlas);
    var textures = (Dictionary<string, Texture2D?>)typeof(SkinManager).GetField("textures", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
    typeof(SkinManager).GetProperty("CurrentPath")!.SetValue(null, ".");
    textures[MenuSkin.SongFolder + "test.png"] = texture;
    var number = typeof(SongRecordPanel).GetMethod("Number", BindingFlags.NonPublic | BindingFlags.Static)!;
    target = Raylib.LoadRenderTexture(1920, 1080);
    Raylib.BeginTextureMode(target); Raylib.ClearBackground(Color.Black);
    number.Invoke(null, new object[] { "test.png", 0L, 200f, 200f, 18f, 22f, 16f, 70f, 1f });
    number.Invoke(null, new object[] { "test.png", 1234L, 200f, 260f, 18f, 22f, 16f, 70f, 1f });
    number.Invoke(null, new object[] { "test.png", 1234L, 200f, 320f, 18f, 22f, 16f, 32f, 1f });
    Rlgl.DrawRenderBatchActive();
    byte[] pixels = Read(target, null);
    (int left, int top, int right, int bottom) Bounds(int y)
    {
        int l=300,t=y+30,r=100,b=y-30;
        for(int yy=y-30;yy<y+30;yy++) for(int x=100;x<300;x++)
            if(pixels[(yy*1920+x)*4]>100) { l=Math.Min(l,x);r=Math.Max(r,x);t=Math.Min(t,yy);b=Math.Max(b,yy); }
        return(l,t,r,b);
    }
    var single=Bounds(200);var multi=Bounds(260);var narrow=Bounds(320);
    Check(Math.Abs((single.left+single.right+1)/2f-200)<1, "透明余白の偏った数字もX中心へ");
    Check(Math.Abs((single.top+single.bottom+1)/2f-200)<1, "透明余白の偏った数字もY中心へ");
    Check(single.bottom-single.top+1==22, "指定した数字高さ22px");
    Check(Math.Abs((multi.left+multi.right+1)/2f-200)<1, "複数桁も中央揃え");
    Check(multi.right-multi.left+1<=70, "複数桁の幅制限");
    Check(narrow.right-narrow.left+1<=32, "狭い欄の幅制限");
    var cache=(System.Collections.IDictionary)typeof(SongRecordPanel).GetField("digitArt",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
    Check(cache.Count==1, "同じ数字画像の余白計測を再利用");
    Raylib.EndTextureMode();
    textures.Clear();Raylib.UnloadTexture(texture);
    Console.WriteLine($"PASS: {passed} record-digit rendering checks (synthetic atlas, not A-Style/Windows)");

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
