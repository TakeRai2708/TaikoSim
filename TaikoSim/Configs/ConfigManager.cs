using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace TaikoSim.Configs
{
    // 全部の設定をまとめて読み込み・保存するクラス
    public static class ConfigManager
    {
        // 設定ファイルを置くフォルダ(exeと同じ場所の Config フォルダ)
        public static readonly string ConfigFolder = Path.Combine(AppContext.BaseDirectory, "Config");

        // 読み込んだ設定(どこからでも ConfigManager.Sound.BgmVolume みたいに使える)
        public static SoundConfig Sound { get; private set; } = new();
        public static GameConfig Game { get; private set; } = new();
        public static KeyConfig Key { get; private set; } = new();
        public static SkinConfig Skin { get; private set; } = new();

        // JSONの書き方の設定
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,                                   // 改行・インデントして読みやすく保存
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),  // 日本語を \u3042 みたいにせずそのまま保存
            Converters = { new JsonStringEnumConverter() },         // キーを数字じゃなく "F" みたいな文字で保存
            ReadCommentHandling = JsonCommentHandling.Skip,         // JSON内の // コメントを無視
            AllowTrailingCommas = true,                             // 最後の余計なカンマを許す
        };

        // 全部の設定を読み込む(起動時に1回呼ぶ)
        public static void LoadAll()
        {
            Sound = Load<SoundConfig>(SoundConfig.FileName);
            Game = Load<GameConfig>(GameConfig.FileName);
            Key = Load<KeyConfig>(KeyConfig.FileName);
            Skin = Load<SkinConfig>(SkinConfig.FileName);

            // おかしな値(音量が200とか)を直す
            Sound.Fix();
            Game.Fix();
            Key.Fix();
            Skin.Fix();

            // 直した値や、新しく増えた項目をファイルに反映
            SaveAll();
        }

        // 全部の設定を保存する
        public static void SaveAll()
        {
            Save(SoundConfig.FileName, Sound);
            Save(GameConfig.FileName, Game);
            Save(KeyConfig.FileName, Key);
            Save(SkinConfig.FileName, Skin);
        }

        // スキンの設定だけ、すぐ保存する(設定パネルでスキンやスキンフォルダを変えたとき)
        public static void SaveSkin()
        {
            try
            {
                Save(SkinConfig.FileName, Skin);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // 保存できなくても、ゲームは止めない(パネルを閉じたとき・終了したときにもう一度保存する)
                Console.WriteLine($"[Config] {SkinConfig.FileName} を保存できませんでした: {ex.Message}");
            }
        }

        // 音の設定だけ、すぐ保存する(設定パネルで［音量を適用］を押したとき)
        public static void SaveSound()
        {
            try
            {
                Save(SoundConfig.FileName, Sound);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Console.WriteLine($"[Config] {SoundConfig.FileName} を保存できませんでした: {ex.Message}");
            }
        }

        // 1つの設定ファイルを読み込む(共通処理)
        private static T Load<T>(string fileName) where T : class, new()
        {
            Directory.CreateDirectory(ConfigFolder);
            string path = Path.Combine(ConfigFolder, fileName);

            // ファイルが無い → 初期値で作る
            if (!File.Exists(path))
            {
                Console.WriteLine($"[Config] {fileName} が無いので初期値で作成します");
                return new T();
            }

            try
            {
                string json = File.ReadAllText(path);
                T? loaded = JsonSerializer.Deserialize<T>(json, Options);

                if (loaded == null)
                {
                    throw new JsonException("中身が空です");
                }

                return loaded;
            }
            catch (Exception ex) when (ex is JsonException || ex is NotSupportedException)
            {
                // ファイルが壊れてる → .bak に退避して初期値で作り直す
                string backupPath = path + ".bak";
                File.Copy(path, backupPath, true);
                Console.WriteLine($"[Config] {fileName} が壊れていたので {fileName}.bak に退避して初期値で作り直します");
                Console.WriteLine($"[Config] 理由: {ex.Message}");
                return new T();
            }
        }

        // 1つの設定ファイルを保存する(共通処理)
        private static void Save<T>(string fileName, T config)
        {
            Directory.CreateDirectory(ConfigFolder);
            string path = Path.Combine(ConfigFolder, fileName);
            string json = JsonSerializer.Serialize(config, Options);
            File.WriteAllText(path, json);
        }
    }
}