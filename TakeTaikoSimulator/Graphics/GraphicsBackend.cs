using System.Reflection;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace TakeTaikoSimulator.Graphics
{
    // 描画方式（設定の「描画方式」）
    //   OpenGL    … 今までどおり。Raylib-cs についてくる raylib.dll（OpenGL 3.3）
    //   DirectX11 … ANGLE で OpenGL ES 3.0 を DirectX 11 に変換して出す（初期値。11/22 たけらい）
    //   Vulkan    … ANGLE で OpenGL ES 3.0 を Vulkan に変換して出す
    //   ※ DirectX 12 は ANGLE に無いので選べない（ANGLE の出し先は D3D9 / D3D11 / OpenGL / Vulkan / Metal）
    public enum GraphicsBackendType
    {
        OpenGL = 0,
        DirectX11 = 1,
        Vulkan = 2,
    }

    // 起動したときに、どの raylib を使うかを決める係。
    //   ANGLE のときは、自分でビルドした raylib_angle.dll（raylib 6.0 を OpenGL ES 3.0 でビルドしたもの。tools/angle-build）を
    //   raylib.dll の代わりに読ませる。exe の横に libEGL.dll・libGLESv2.dll（ANGLE。OpenTaiko から）が要る。
    //   1回のプロセスでは raylib を入れ替えられないので、変えたら再起動。
    public static class GraphicsBackend
    {
        public const string AngleRaylibFile = "raylib_angle.dll";
        private static readonly string[] AngleFiles = { AngleRaylibFile, "libEGL.dll", "libGLESv2.dll" };

        // 前の起動が「ANGLE で窓を出す前後」で止まったかを見る印（最初の1枚を出せたら消す）
        private static string MarkerPath => Configs.DataPaths.Data("Config", "GraphicsStarting.txt");

        /// <summary>今回の起動で実際に使っている描画方式</summary>
        public static GraphicsBackendType Active { get; private set; } = GraphicsBackendType.OpenGL;
        public static bool IsAngle => Active != GraphicsBackendType.OpenGL;

        /// <summary>今回 OpenGL にもどした理由（無ければ null）。設定画面に出す</summary>
        public static string? FallbackReason { get; private set; }

        // GLFW の ANGLE の出し先の番号（glfw3.h の GLFW_ANGLE_PLATFORM_TYPE_*）
        private const int GlfwAngleD3D11 = 0x00037005;
        private const int GlfwAngleVulkan = 0x00037007;

        [DllImport("raylib_angle", EntryPoint = "TakeTaikoSetAnglePlatform")]
        private static extern void SetAnglePlatform(int glfwAnglePlatformType);

        /// <summary>
        /// Raylib を初めて呼ぶ前に1回だけ呼ぶ。wanted が使えなければ OpenGL にする。
        /// 前の起動が ANGLE で止まっていたら、今回は OpenGL で起動する（もう一度選べば ANGLE に戻る）
        /// </summary>
        public static void Select(GraphicsBackendType wanted)
        {
            Active = GraphicsBackendType.OpenGL;
            if (wanted == GraphicsBackendType.OpenGL) { DeleteMarker(); return; }
            if (!OperatingSystem.IsWindows()) { FallbackReason = "Windows 以外なので OpenGL で起動しました"; return; }

            if (File.Exists(MarkerPath))
            {
                // 前回、窓を出して最初の1枚を描く前に落ちた（または固まった）
                DeleteMarker();
                FallbackReason = $"前回 {Name(wanted)} で起動できなかったので、OpenGL で起動しました";
                Console.WriteLine($"[Graphics] {FallbackReason}");
                return;
            }
            string? missing = AngleFiles.FirstOrDefault(f => !File.Exists(Path.Combine(AppContext.BaseDirectory, f)));
            if (missing != null)
            {
                FallbackReason = $"{missing} が見つからないので、OpenGL で起動しました";
                Console.WriteLine($"[Graphics] {FallbackReason}");
                return;
            }

            try
            {
                // Raylib-cs の "raylib" を、raylib_angle.dll に読みかえる
                NativeLibrary.SetDllImportResolver(typeof(Raylib).Assembly, ResolveRaylib);
                SetAnglePlatform(wanted == GraphicsBackendType.Vulkan ? GlfwAngleVulkan : GlfwAngleD3D11);
                Active = wanted;
                WriteMarker(wanted);
                Console.WriteLine($"[Graphics] {Name(wanted)}（ANGLE）で起動します");
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException || ex is BadImageFormatException || ex is InvalidOperationException)
            {
                FallbackReason = $"ANGLE を読めなかったので、OpenGL で起動しました（{ex.GetType().Name}）";
                Console.WriteLine($"[Graphics] {FallbackReason}: {ex.Message}");
                Active = GraphicsBackendType.OpenGL;
            }
        }

        [DllImport("libGLESv2", EntryPoint = "glGetString")]
        private static extern IntPtr GlGetString(int name);
        private const int GlRenderer = 0x1F01;
        private static bool broken;

        /// <summary>
        /// InitWindow のすぐ後に呼ぶ。ANGLE の描き先がちゃんと使えるかを確かめて、ログに出す。
        /// 名前が取れない（ANGLE の文脈が無い）ときは、起動できた印を残して、次の起動を OpenGL にする
        /// （11/22：raylib_angle.dll が opengl32.dll にもつながっていて、真っ黒なのに動き続けた。こういうのを拾う）
        /// </summary>
        public static void CheckAfterInit()
        {
            if (!IsAngle) return;
            IntPtr renderer = IntPtr.Zero;
            try { renderer = GlGetString(GlRenderer); }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException) { }
            if (renderer == IntPtr.Zero)
            {
                broken = true;
                Console.WriteLine($"[Graphics] {Name(Active)}（ANGLE）の画面が使えません。次の起動は OpenGL にします");
                return;
            }
            Console.WriteLine($"[Graphics] {Marshal.PtrToStringAnsi(renderer)}");
        }

        /// <summary>最初の1枚を画面に出せたら呼ぶ（起動できた印を消す）</summary>
        public static void FirstFrameShown() { if (!broken) DeleteMarker(); }

        /// <summary>シェーダー（#version 330 で書いたもの）を、今の描画方式で読める形にする</summary>
        public static string Glsl(string fragment) => !IsAngle ? fragment
            : fragment.Replace("#version 330", "#version 300 es\nprecision highp float;");

        public static string Name(GraphicsBackendType type) => type switch
        {
            GraphicsBackendType.DirectX11 => "DirectX 11",
            GraphicsBackendType.Vulkan => "Vulkan",
            _ => "OpenGL",
        };

        private static IntPtr ResolveRaylib(string name, Assembly assembly, DllImportSearchPath? path)
        {
            if (name != "raylib") return IntPtr.Zero;
            return NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, AngleRaylibFile));
        }

        private static void WriteMarker(GraphicsBackendType type)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath)!);
                File.WriteAllText(MarkerPath, $"{type} {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }

        private static void DeleteMarker()
        {
            try { if (File.Exists(MarkerPath)) File.Delete(MarkerPath); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }
    }
}
