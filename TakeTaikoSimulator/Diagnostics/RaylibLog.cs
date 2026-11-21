using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace TakeTaikoSimulator.Diagnostics
{
    // raylib が出す INFO / WARNING / ERROR を、Console.WriteLine に流す係。
    //   Console はログファイル(Logs/TaikoSim_*.log)にも書かれるので、黒い画面がすぐ消えても後で読める。
    //   11/22：描画方式を Vulkan にすると黒い画面が一瞬出て終わり、何が起きたか見られなかった
    public static unsafe class RaylibLog
    {
        // raylib を初めて呼ぶところ。描画方式(どの raylib を読むか)を決めたあとに呼ぶ
        public static void Start() => Raylib.SetTraceLogCallback(&Write);

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Write(int level, sbyte* text, sbyte* args)
        {
            try
            {
                string message = Logging.GetLogMessage(new IntPtr(text), new IntPtr(args));
                string head = (TraceLogLevel)level switch
                {
                    TraceLogLevel.Warning => "WARNING",
                    TraceLogLevel.Error => "ERROR",
                    TraceLogLevel.Fatal => "FATAL",
                    TraceLogLevel.Debug => "DEBUG",
                    _ => "INFO",
                };
                Console.WriteLine($"{head}: {message}");
            }
            catch
            {
                // ログの係が落ちて、ゲームごと止まるのは避ける（raylib から呼ばれるので例外を外に出せない）
            }
        }
    }
}
