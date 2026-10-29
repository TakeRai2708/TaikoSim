using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace TaikoSim.Configs
{
    // 全部の設定をまとめて読み込み・保存するクラス
    //
    // JSON の読み書きは、Native AOT でも動くように AppJsonContext(ソース生成)を使う。
    //   型ごとに AppJsonContext.Default.GameConfig のような「設計図」(JsonTypeInfo)を渡す。
    public static class ConfigManager
    {
        // 設定ファイルを置くフォルダ(exeと同じ場所の Config フォルダ)
        public static readonly string ConfigFolder = Path.Combine(AppContext.BaseDirectory, "Config");

        // 読み込んだ設定(どこからでも ConfigManager.Sound.BgmVolume みたいに使える)
        public static SoundConfig Sound { get; private set; } = new();
        public static GameConfig Game { get; private set; } = new();
        public static KeyConfig Key { get; private set; } = new();
        public static SkinConfig Skin { get; private set; } = new();

        // ===== 全画面の設定画面で、適用前の値を本番の設定から分けて持つ =====
        //   JSON にして読み直すことで、まるごとコピーする(中の Dictionary なども別物になる)
        //   型ごとに同じことをするので、オーバーロードで4種類ぶん用意する(呼ぶ側は今までどおり Copy(xxx) でOK)
        public static GameConfig Copy(GameConfig value) => Clone(value, AppJsonContext.Default.GameConfig);
        public static SoundConfig Copy(SoundConfig value) => Clone(value, AppJsonContext.Default.SoundConfig);
        public static KeyConfig Copy(KeyConfig value) => Clone(value, AppJsonContext.Default.KeyConfig);
        public static SkinConfig Copy(SkinConfig value) => Clone(value, AppJsonContext.Default.SkinConfig);

        public static bool Same(GameConfig a, GameConfig b) => SameJson(a, b, AppJsonContext.Default.GameConfig);
        public static bool Same(SoundConfig a, SoundConfig b) => SameJson(a, b, AppJsonContext.Default.SoundConfig);
        public static bool Same(KeyConfig a, KeyConfig b) => SameJson(a, b, AppJsonContext.Default.KeyConfig);
        public static bool Same(SkinConfig a, SkinConfig b) => SameJson(a, b, AppJsonContext.Default.SkinConfig);

        private static T Clone<T>(T value, JsonTypeInfo<T> info) =>
            JsonSerializer.Deserialize(JsonSerializer.Serialize(value, info), info)!;

        private static bool SameJson<T>(T left, T right, JsonTypeInfo<T> info) =>
            JsonSerializer.Serialize(left, info) == JsonSerializer.Serialize(right, info);

        public static void Replace(GameConfig game, SoundConfig sound, KeyConfig key, SkinConfig skin)
        {
            Game = game;
            Sound = sound;
            Key = key;
            Skin = skin;
            SaveAll();
        }

        // 全部の設定を読み込む(起動時に1回呼ぶ)
        public static void LoadAll()
        {
            Sound = Load(SoundConfig.FileName, AppJsonContext.Default.SoundConfig);
            Game = Load(GameConfig.FileName, AppJsonContext.Default.GameConfig);
            Key = Load(KeyConfig.FileName, AppJsonContext.Default.KeyConfig);
            Skin = Load(SkinConfig.FileName, AppJsonContext.Default.SkinConfig);

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
            Save(SoundConfig.FileName, Sound, AppJsonContext.Default.SoundConfig);
            Save(GameConfig.FileName, Game, AppJsonContext.Default.GameConfig);
            Save(KeyConfig.FileName, Key, AppJsonContext.Default.KeyConfig);
            Save(SkinConfig.FileName, Skin, AppJsonContext.Default.SkinConfig);
        }

        // スキンの設定だけ、すぐ保存する(設定パネルでスキンやスキンフォルダを変えたとき)
        public static void SaveSkin()
        {
            try
            {
                Save(SkinConfig.FileName, Skin, AppJsonContext.Default.SkinConfig);
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
                Save(SoundConfig.FileName, Sound, AppJsonContext.Default.SoundConfig);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Console.WriteLine($"[Config] {SoundConfig.FileName} を保存できませんでした: {ex.Message}");
            }
        }

        // 1つの設定ファイルを読み込む(共通処理)
        private static T Load<T>(string fileName, JsonTypeInfo<T> info) where T : class, new()
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
                T? loaded = JsonSerializer.Deserialize(json, info);

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
        private static void Save<T>(string fileName, T config, JsonTypeInfo<T> info)
        {
            Directory.CreateDirectory(ConfigFolder);
            string path = Path.Combine(ConfigFolder, fileName);
            string json = JsonSerializer.Serialize(config, info);
            File.WriteAllText(path, json);
        }
    }
}
