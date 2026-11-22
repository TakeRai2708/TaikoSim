namespace TakeTaikoSimulator.Configs
{
    // データの置き場所
    //   11/19 たけらい「デバッグもリリースも同じデータで遊びたい」→ B 案：
    //   設定・プレイヤーデータ・曲・スキン・キャッシュ・ログ・スクショは、exe の場所に関係なく
    //   「ドキュメント\TakeTaikoSimulator\」に置く。exe の横には、いっしょに配る物（フォント・dll・同梱のスキンと音色）だけ。
    //   ・最初の起動で、exe の横にある今までのデータを引っ越す（同じドライブなら一瞬。ちがうドライブならコピーして、元は残す）
    //   ・同梱の TaikoSim-Simple スキンと音色は、起動のたびに exe の横から新しいファイルだけコピーする（ビルドで更新されるため）
    //   ドキュメントが見つからない・作れないときは、今までどおり exe の横を使う
    public static class DataPaths
    {
        public const string FolderName = "TakeTaikoSimulator";

        // exe の横（同梱の物）
        public static string AppFolder => AppContext.BaseDirectory;

        // データの場所（Prepare で決まる）
        public static string Root { get; private set; } = AppContext.BaseDirectory;

        public static string Data(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());

        // 引っ越すフォルダ（exe の横 → ドキュメント）
        private static readonly string[] MoveFolders = { "Config", "Songs", "Skins", "Cache", "Logs", "Screenshots", "HitSounds" };
        // 同梱で、起動のたびに新しいファイルを入れ直すフォルダ
        private static readonly string[] BundledFolders = { Path.Combine("Skins", "TaikoSim-Simple"), "HitSounds" };

        // 起動の一番最初に1回呼ぶ（ConfigManager などがパスを使う前）
        public static void Prepare()
        {
            string documents;
            try { documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); }
            catch (PlatformNotSupportedException) { documents = ""; }
            if (string.IsNullOrEmpty(documents)) { Console.WriteLine("[データ] ドキュメントが見つからないので、exe の横を使います"); return; }

            string root = Path.Combine(documents, FolderName);
            try { Directory.CreateDirectory(root); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"[データ] {root} を作れないので、exe の横を使います（{ex.Message}）");
                return;
            }
            Root = root;
            if (SamePath(Root, AppFolder)) return;

            foreach (string name in MoveFolders) MoveOld(name);
            foreach (string name in BundledFolders) SyncBundled(name);
        }

        // exe の横に前のデータがあって、ドキュメント側にまだ無ければ引っ越す（両方あるときは何もしない＝上書きしない）
        private static void MoveOld(string name)
        {
            string from = Path.Combine(AppFolder, name), to = Path.Combine(Root, name);
            if (!Directory.Exists(from) || Directory.Exists(to)) return;
            try
            {
                Directory.Move(from, to);
                Console.WriteLine($"[データ] {name} をドキュメントへ引っ越しました");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // ちがうドライブなどで動かせないとき：コピーして、元は残す
                try
                {
                    CopyFolder(from, to, onlyNewer: false);
                    Console.WriteLine($"[データ] {name} をドキュメントへコピーしました（元は残しています：{ex.Message}）");
                }
                catch (Exception copyEx) when (copyEx is IOException or UnauthorizedAccessException)
                {
                    Console.WriteLine($"[データ] {name} を引っ越せませんでした：{copyEx.Message}");
                }
            }
        }

        // 同梱の物：exe の横の方が新しいファイルだけ、ドキュメント側へコピーする
        private static void SyncBundled(string name)
        {
            string from = Path.Combine(AppFolder, name), to = Path.Combine(Root, name);
            if (!Directory.Exists(from)) return;
            // 既にある音色はプレイヤーが差し替えている場合がある。
            try { CopyFolder(from, to, onlyNewer: true, overwriteExisting: name != "HitSounds"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"[データ] 同梱の {name} をコピーできませんでした：{ex.Message}");
            }
        }

        private static void CopyFolder(string from, string to, bool onlyNewer, bool overwriteExisting = true)
        {
            foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(to, Path.GetRelativePath(from, file));
                if (File.Exists(target) && (!overwriteExisting ||
                    onlyNewer && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(file))) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: overwriteExisting);
            }
        }

        private static bool SamePath(string a, string b) =>
            string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }
}
