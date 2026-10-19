using System.Diagnostics;
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

        // 今の演奏画面で使う画像を、表示する前にまとめて読む。
        // ゴーゴーや大音符など、曲の途中で初めて出る画像もここに入れておく。
        // 演奏用の画像を増やしたときは、この一覧にも追加する。
        // 使っていない背景や別スキンの画像、曲の音源までは読み込まない。
        private static readonly string[] PlayTexturePaths =
        {
            // ドン・カッ・大音符・連打・風船
            "Image/04.EnsoGame/Notes/Normal/0/0.png",
            "Image/04.EnsoGame/Notes/Normal/0/1.png",
            "Image/04.EnsoGame/Notes/Normal/0/2.png",
            "Image/04.EnsoGame/Notes/Normal/0/3.png",
            "Image/04.EnsoGame/Notes/Normal/0/4.png",
            "Image/04.EnsoGame/Notes/Normal/0/5.png",
            "Image/04.EnsoGame/Notes/Normal/0/6.png",
            "Image/04.EnsoGame/Notes/Normal/0/7.png",
            "Image/04.EnsoGame/Notes/Normal/0/8.png",
            "Image/04.EnsoGame/Notes/Normal/0/9.png",
            "Image/04.EnsoGame/Notes/Normal/0/10.png",
            "Image/04.EnsoGame/Notes/JudgementFrame.png",

            // レーン・左パネル・音符の下の文字・判定文字
            "Image/04.EnsoGame/Lane/Background_1P.png",
            "Image/04.EnsoGame/MiniTaiko/Background_1P.png",
            "Image/04.EnsoGame/SENotes.png",
            "Image/04.EnsoGame/SENotes_Rolling.png",
            "Image/04.EnsoGame/Judge.png",

            // 連打と風船の表示
            "Image/04.EnsoGame/Popup/Roll_Base.png",
            "Image/04.EnsoGame/Popup/Roll_Number.png",
            "Image/04.EnsoGame/Popup/Roll_Text.png",
            "Image/04.EnsoGame/Popup/Balloon_Base.png",
            "Image/04.EnsoGame/Popup/Balloon_Number.png",
            "Image/04.EnsoGame/Popup/Balloon_Anime.png",

            // 打鍵・判定・ミニ太鼓・コンボ・ゴーゴーの演出
            "Image/04.EnsoGame/Lane/Red.png",
            "Image/04.EnsoGame/Lane/Blue.png",
            "Image/04.EnsoGame/Lane/Yellow.png",
            "Image/04.EnsoGame/Lane/GoGo.png",
            "Image/04.EnsoGame/Effects/Hit_Explosion.png",
            "Image/04.EnsoGame/Effects/Hit_Explosion_Big.png",
            "Image/04.EnsoGame/Effects/Hit_Effects.png",
            "Image/04.EnsoGame/Effects/GoGoSplash.png",
            "Image/04.EnsoGame/MiniTaiko/MiniTaiko.png",
            "Image/04.EnsoGame/MiniTaiko/Combo_Number.png",
            "Image/04.EnsoGame/MiniTaiko/Combo_MiniText.png",
        };

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

            // 起動時もスキン変更時も、演奏用画像の準備が終わってから戻る。
            // GPUに画像を送るので、これまでどおり画面と同じスレッドで実行する。
            PreloadPlayTextures();

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

        // 同じ GetTexture を使うので、読み込んだ画像は演奏中もそのまま使い回せる。
        // 無い画像や読めない画像も null で覚え、演奏中に繰り返し読みに行かない。
        private static void PreloadPlayTextures()
        {
            if (CurrentPath == null) return;

            Stopwatch watch = Stopwatch.StartNew();
            int loaded = 0;
            foreach (string path in PlayTexturePaths)
            {
                if (GetTexture(path) is Texture2D) loaded++;
            }
            watch.Stop();

            Console.WriteLine($"[Skin] 演奏用画像の事前読み込み：{loaded}/{PlayTexturePaths.Length}枚"
                + $"（未配置または読み込み失敗 {PlayTexturePaths.Length - loaded}枚、{watch.Elapsed.TotalMilliseconds:F1}ms）");
        }

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
