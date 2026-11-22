using System.Text;

namespace TakeTaikoSimulator
{
    internal class Program
    {
        // Windowsの「フォルダの選択」画面を使うのに必要
        [STAThread]
        static void Main(string[] args)
        {
            if (!Updates.StartupUpdate.TryStartGame(args, out var applicationLock))
                return;
            using var gameLock = applicationLock;

            // データの場所を決める（ドキュメント\TakeTaikoSimulator。最初の起動で exe の横のデータを引っ越す）
            Configs.DataPaths.Prepare();

            // 黒い画面(コンソール)に出す内容を、ファイルにも残す
            StartLogFile();

            // ゲームを起動する(中身は Game.cs)
            new Game().Run();
        }

        // exe の横の Logs フォルダに、今回のログのファイルを作る
        private static void StartLogFile()
        {
            try
            {
                // Logs\TaikoSimLog_日付_時間\System\TaikoSim.log（古い起動のフォルダは Diagnostics/LogPaths.cs が片付ける）
                Diagnostics.LogPaths.Tidy();
                string path = Path.Combine(Diagnostics.LogPaths.Folder(Diagnostics.LogPaths.SystemKind), "TaikoSim.log");
                StreamWriter file = new StreamWriter(path, false, new UTF8Encoding(true)) { AutoFlush = true };

                // Console.WriteLine の行き先を「黒い画面とファイルの両方」にする
                Console.SetOut(TextWriter.Synchronized(new TeeWriter(Console.Out, file)));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Console.WriteLine($"[Log] ログのファイルを作れませんでした: {ex.Message}");
            }
        }

        // 書いた文字を2か所に同じように書く係
        private sealed class TeeWriter : TextWriter
        {
            private readonly TextWriter first;
            private readonly TextWriter second;

            public TeeWriter(TextWriter first, TextWriter second)
            {
                this.first = first;
                this.second = second;
            }

            public override Encoding Encoding => Encoding.UTF8;

            public override void Write(char value)
            {
                first.Write(value);
                second.Write(value);
            }

            public override void Write(string? value)
            {
                first.Write(value);
                second.Write(value);
            }

            public override void WriteLine(string? value)
            {
                first.WriteLine(value);
                second.WriteLine(value);
                Diagnostics.DebugLog.ConsoleLine(value); // デバッグ記録の events.csv にも「ログ」として残す
            }

            public override void Flush()
            {
                first.Flush();
                second.Flush();
            }
        }
    }
}
