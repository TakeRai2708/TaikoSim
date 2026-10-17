using System.Numerics;
using System.Text.Json;
using Raylib_cs;
using TaikoSim.Configs;

namespace TaikoSim.Graphics
{
    // スキンの画像を読み込んで管理する係
    // 使い方: SkinManager.Draw("Image/04.EnsoGame/Notes/Normal/0/0.png", x, y);
    public static class SkinManager
    {
        // 読み込んだ画像(読めなかったものは null で覚えておいて、何度も読みに行かないようにする)
        private static readonly Dictionary<string, Texture2D?> textures = new();

        // 今使っているスキンのフォルダ(スキンが無ければ null)
        public static string? CurrentPath { get; private set; }

        // 今使っているスキンのフォルダ名
        public static string CurrentName => CurrentPath != null ? Path.GetFileName(CurrentPath) : "なし";

        // スキンの SkinConfig.json に書いてある情報
        public static string DisplayName { get; private set; } = "";
        public static string Version { get; private set; } = "";
        public static string Description { get; private set; } = "";

        // スキンフォルダの中にあるスキンの一覧(毎フレーム探すと重いので覚えておく)
        public static List<string> SkinNames { get; private set; } = new();

        // スキンを読み直すたびに1増える(画面側で「スキンが変わった？」を調べる用)
        public static int Generation { get; private set; } = 0;

        // 準備(ウィンドウを作った後に呼ぶ)
        public static void Init()
        {
            Reload();
        }

        // 設定に合わせてスキンを読み直す(スキンやフォルダを変えたときに呼ぶ)
        public static void Reload()
        {
            UnloadTextures();

            SkinNames = ConfigManager.Skin.GetSkinList();
            CurrentPath = ConfigManager.Skin.GetCurrentSkinPath();
            ReadSkinInfo();

            Generation++;
            Console.WriteLine($"[Skin] {CurrentName} を使います");
        }

        // 画像を取り出す(初めてなら読み込む)。無ければ null
        // relativePath はスキンフォルダからの場所。区切りは「/」
        public static Texture2D? GetTexture(string relativePath)
        {
            if (CurrentPath == null) return null;

            if (textures.TryGetValue(relativePath, out Texture2D? cached))
            {
                return cached;
            }

            string fullPath = Path.Combine(CurrentPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Texture2D? texture = LoadTexture(fullPath);
            textures[relativePath] = texture;
            return texture;
        }

        // 画像をそのまま描く(scale で拡大・縮小)。描けたら true
        public static bool Draw(string relativePath, float x, float y, float scale = 1f)
        {
            Texture2D? texture = GetTexture(relativePath);
            if (texture is not Texture2D tex) return false;

            Raylib.DrawTextureEx(tex, new Vector2(x, y), 0f, scale, Color.White);
            return true;
        }

        // 後片付け(ウィンドウを閉じる前に呼ぶ)
        public static void Unload()
        {
            UnloadTextures();
        }

        // ===== 中身 =====

        // ファイルを自分で読んでからRaylibに渡す(パスに日本語があっても読めるように)
        private static Texture2D? LoadTexture(string fullPath)
        {
            if (!File.Exists(fullPath)) return null;

            try
            {
                byte[] data = File.ReadAllBytes(fullPath);
                Image image = Raylib.LoadImageFromMemory(Path.GetExtension(fullPath).ToLowerInvariant(), data);

                if (image.Width == 0 || image.Height == 0)
                {
                    Raylib.UnloadImage(image);
                    Console.WriteLine($"[Skin] 画像を読めませんでした: {fullPath}");
                    return null;
                }

                Texture2D texture = Raylib.LoadTextureFromImage(image);
                Raylib.UnloadImage(image);
                Raylib.SetTextureFilter(texture, TextureFilter.Bilinear); // 拡大・縮小してもガタガタにしない
                return texture;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Console.WriteLine($"[Skin] 画像を読めませんでした: {fullPath} ({ex.Message})");
                return null;
            }
        }

        // 読み込んだ画像を全部捨てる
        private static void UnloadTextures()
        {
            foreach (Texture2D? texture in textures.Values)
            {
                if (texture is Texture2D tex) Raylib.UnloadTexture(tex);
            }
            textures.Clear();
        }

        // スキンの SkinConfig.json から名前・バージョン・説明を読む
        private static void ReadSkinInfo()
        {
            DisplayName = CurrentName;
            Version = "";
            Description = "";

            if (CurrentPath == null) return;

            string path = Path.Combine(CurrentPath, "SkinConfig.json");
            if (!File.Exists(path)) return;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                JsonElement root = doc.RootElement;

                if (root.TryGetProperty("skinName", out JsonElement name)) DisplayName = name.GetString() ?? DisplayName;
                if (root.TryGetProperty("skinVersion", out JsonElement version)) Version = version.GetString() ?? "";
                if (root.TryGetProperty("skinDescription", out JsonElement description)) Description = description.GetString() ?? "";
            }
            catch (Exception ex) when (ex is JsonException || ex is IOException || ex is InvalidOperationException)
            {
                Console.WriteLine($"[Skin] SkinConfig.json を読めませんでした: {ex.Message}");
            }
        }
    }
}