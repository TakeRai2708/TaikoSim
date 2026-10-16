namespace TaikoSim.Configs
{
    // スキンの設定
    public class SkinConfig
    {
        public const string FileName = "SkinConfig.json";

        // スキンを入れてるフォルダの場所。空ならexeの隣の Skins フォルダを使う
        // 例: "D:\\TaikoSkins"
        public string SkinsFolder { get; set; } = "";

        // 使うスキンのフォルダ名。例: "A-Style"
        public string SkinName { get; set; } = "";

        // 実際に使う Skins フォルダの場所を返す
        public string GetSkinsFolderPath()
        {
            if (string.IsNullOrWhiteSpace(SkinsFolder))
            {
                return Path.Combine(AppContext.BaseDirectory, "Skins");
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

            return Directory.GetDirectories(folder)
                .Select(path => Path.GetFileName(path))
                .OrderBy(name => name)
                .ToList();
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