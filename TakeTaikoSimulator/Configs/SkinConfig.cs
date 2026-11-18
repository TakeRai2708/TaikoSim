namespace TakeTaikoSimulator.Configs
{
    // スキンの設定
    public class SkinConfig
    {
        public const string FileName = "SkinConfig.json";

        // スキンを入れてるフォルダの場所。空ならデータの場所（ドキュメント\TakeTaikoSimulator）の Skins フォルダを使う
        // 例: "D:\\TaikoSkins"
        public string SkinsFolder { get; set; } = "";

        // 使うスキンのフォルダ名。例: "A-Style"
        public string SkinName { get; set; } = "";

        // 実際に使う Skins フォルダの場所を返す
        public string GetSkinsFolderPath()
        {
            if (string.IsNullOrWhiteSpace(SkinsFolder))
            {
                return DataPaths.Data("Skins");
            }
            return SkinsFolder;
        }

        // Skins フォルダの中にあるスキン(サブフォルダ)の名前一覧を返す
        public List<string> GetSkinList()
        {
            string folder = GetSkinsFolderPath();
            if (!Directory.Exists(folder))
            {
                return new List<string>();
            }

            try
            {
                // スキンっぽいフォルダだけ(SkinConfig.json か Image フォルダがあるもの)
                return Directory.GetDirectories(folder)
                    .Where(IsSkinFolder)
                    .Select(path => Path.GetFileName(path))
                    .OrderBy(name => name)
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return new List<string>();
            }
        }

        // そのフォルダがスキン1つ分のフォルダか
        public static bool IsSkinFolder(string path)
        {
            return File.Exists(Path.Combine(path, "SkinConfig.json"))
                || Directory.Exists(Path.Combine(path, "Image"));
        }

        // 今使うスキンのフォルダの場所を返す。スキンが1個も無ければ null
        public string? GetCurrentSkinPath()
        {
            List<string> skins = GetSkinList();
            if (skins.Count == 0)
            {
                return null;
            }

            // 指定したスキンが見つからなければ、一覧の最初のスキンを使う
            string name = skins.Contains(SkinName) ? SkinName : skins[0];
            return Path.Combine(GetSkinsFolderPath(), name);
        }

        // おかしな値を直す
        public void Fix()
        {
            // exeの隣の Skins フォルダが無ければ作っておく
            if (string.IsNullOrWhiteSpace(SkinsFolder))
            {
                Directory.CreateDirectory(GetSkinsFolderPath());
            }
        }
    }
}