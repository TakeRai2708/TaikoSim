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
        // 枠の内側に収めた先端の光。0:赤、3:段差、4:黄、5:右端。
        private static readonly Texture2D?[] gaugeGlowCells = new Texture2D?[6];
        // 魂の輪から切り出した丸。スキン適用時に一度だけ用意し、演奏中は使い回す。
        private static Texture2D? soulExplosionDot;
        public static int SoulExplosionDotCount { get; private set; } = 12;
        public static float SoulExplosionRadius { get; private set; }
        private static Shader rollHeatShader;
        private static int rollHeatLocation = -1;
        private const string RollHeatFragment = """
            #version 330
            in vec2 fragTexCoord;
            in vec4 fragColor;
            uniform sampler2D texture0;
            uniform vec4 colDiffuse;
            uniform float heat;
            out vec4 finalColor;
            void main()
            {
                vec4 pixel = texture(texture0, fragTexCoord);
                float yellow = smoothstep(0.08, 0.35, min(pixel.r, pixel.g) - pixel.b);
                vec3 red = vec3(pixel.r, pixel.g * (1.0 - heat), pixel.b * (1.0 - heat));
                pixel.rgb = mix(pixel.rgb, red, yellow);
                finalColor = pixel * colDiffuse * fragColor;
            }
            """;

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

            // 通常の顔つき画像。無いスキンでは上の0番へフォールバックする。
            "Image/04.EnsoGame/Notes/Normal/1/0.png",
            "Image/04.EnsoGame/Notes/Normal/1/1.png",
            "Image/04.EnsoGame/Notes/Normal/1/2.png",
            "Image/04.EnsoGame/Notes/Normal/1/3.png",
            "Image/04.EnsoGame/Notes/Normal/1/4.png",
            "Image/04.EnsoGame/Notes/Normal/1/5.png",
            "Image/04.EnsoGame/Notes/Normal/1/6.png",
            "Image/04.EnsoGame/Notes/Normal/1/7.png",
            "Image/04.EnsoGame/Notes/Normal/1/8.png",
            "Image/04.EnsoGame/Notes/Normal/1/9.png",
            "Image/04.EnsoGame/Notes/Normal/1/10.png",
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

            // 譜面分岐のレーンの色(玄人・達人)
            "Image/04.EnsoGame/Lane/Branch_Expert.png",
            "Image/04.EnsoGame/Lane/Branch_Master.png",
            "Image/04.EnsoGame/Effects/Hit_Explosion.png",
            "Image/04.EnsoGame/Effects/Hit_Explosion_Big.png",
            "Image/04.EnsoGame/Effects/Hit_Effects.png",
            "Image/04.EnsoGame/Effects/GoGoSplash.png",
            "Image/04.EnsoGame/Effects/Soul_Explosion.png",
            "Image/04.EnsoGame/Effects/Note_Flush.png",
            "Image/04.EnsoGame/Effects/FireWorks.png",
            "Image/04.EnsoGame/MiniTaiko/MiniTaiko.png",
            "Image/04.EnsoGame/MiniTaiko/Combo_Number.png",
            "Image/04.EnsoGame/MiniTaiko/Combo_MiniText.png",
            "Image/04.EnsoGame/MiniTaiko/Effect.png",
            "Image/04.EnsoGame/Lane/GoGoSoul.png",

            // 得点・難易度・魂ゲージ(満タンになったときの虹と魂も先に読む)
            "Image/04.EnsoGame/MiniTaiko/Background_Score.png",
            "Image/04.EnsoGame/MiniTaiko/Score.png",
            "Image/04.EnsoGame/MiniTaiko/CourseSymbol.png",
            "Image/04.EnsoGame/Gauge/Gauge_Base.png",
            "Image/04.EnsoGame/Gauge/Gauge_Light.png",
            "Image/04.EnsoGame/Gauge/Gauge_Light_Glow.png",
            "Image/04.EnsoGame/Gauge/Gauge_Clear_Text.png",
            "Image/04.EnsoGame/Gauge/Rainbow.png",
            "Image/04.EnsoGame/Gauge/Soul_Anime.png",
            "Image/04.EnsoGame/Gauge/Soul_Text.png",

            // 分岐の文字・レベルアップの吹き出し・判定の数の板
            "Image/04.EnsoGame/Lane/Branch_Normal_Text.png",
            "Image/04.EnsoGame/Lane/Branch_Expert_Text.png",
            "Image/04.EnsoGame/Lane/Branch_Master_Text.png",
            "Image/04.EnsoGame/MiniTaiko/Branch_Popup.png",
            "Image/04.EnsoGame/Scoreboard/Base.png",
            "Image/04.EnsoGame/Scoreboard/Number.png",

            // スコアランク(演奏中に出る 粋・雅・極)
            "Image/04.EnsoGame/ScoreRankSymbol.png",

            // 演奏後の演出(クリア失敗・クリア成功・フルコンボ・ドンダフルコンボ)
            "Image/04.EnsoGame/End/Text.png",
            "Image/04.EnsoGame/End/Chara.png",
            "Image/04.EnsoGame/End/Fan.png",
            "Image/04.EnsoGame/End/FullCombo_Text.png",
            "Image/04.EnsoGame/End/DondaFullCombo_Text.png",
            "Image/04.EnsoGame/End/DondaFullCombo_Background.png",
            "Image/04.EnsoGame/End/Confetti.png",
            "Image/04.EnsoGame/End/Star.png",
        };

        // 真っ白にした画像(形はそのまま。白く光らせる演出に使う)。スキンの画像と一緒に片付ける
        private static readonly Dictionary<string, Texture2D?> whiteTextures = new();

        // 演奏中に白い画像を作ると一瞬止まるので、スキンを読むときに作っておく
        private static readonly string[] PlayWhiteTexturePaths =
        {
            "Image/04.EnsoGame/End/Chara.png",
            "Image/04.EnsoGame/End/Text.png",
            "Image/04.EnsoGame/MiniTaiko/Effect.png", // コンボのキラキラ(白くして、銀・金の色をかける)
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
            foreach (string path in PlayWhiteTexturePaths) GetWhiteTexture(path);
            PrepareSoulExplosionDot();
            PrepareGaugeGlowCells();
            PrepareRollHeatShader();
            FontManager.ReloadSkinMainFont(CurrentPath);
            PlayBackground.ReloadSkin();

            Generation++;
            Console.WriteLine($"[Skin] {CurrentName} を使います");
        }

        // 画像を取り出す(初めてなら読み込む)。無ければ null
        // relativePath はスキンフォルダからの場所。区切りは「/」
        public static Texture2D? GetTexture(string relativePath)
        {
            if (CurrentPath == null || SystemSkin) return null;

            if (textures.TryGetValue(relativePath, out Texture2D? cached))
            {
                return cached;
            }

            string fullPath = Path.Combine(CurrentPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Texture2D? texture = LoadTexture(fullPath);
            textures[relativePath] = texture;
            return texture;
        }

        // 画像を真っ白にしたもの(透明なところはそのまま)。元の画像が無ければ null
        // テスト用：true の間は、スキンの画像を全部「無い」ことにする(TaikoSim が自分で描く見た目になる)
        //   読み込んだ画像は捨てないので、false に戻すとすぐ元のスキンに戻る
        public static bool SystemSkin { get; set; }

        public static Texture2D? GetWhiteTexture(string relativePath)
        {
            if (SystemSkin) return null;
            if (whiteTextures.TryGetValue(relativePath, out Texture2D? cached)) return cached;
            Texture2D? result = null;
            if (GetTexture(relativePath) is Texture2D source)
            {
                Image image = Raylib.LoadImageFromTexture(source);
                // 明るさ +255 で、色は全部白になる。透明度(アルファ)は変わらない
                Raylib.ImageColorBrightness(ref image, 255);
                Texture2D texture = Raylib.LoadTextureFromImage(image);
                Raylib.UnloadImage(image);
                if (texture.Id != 0)
                {
                    Raylib.SetTextureFilter(texture, TextureFilter.Bilinear);
                    result = texture;
                }
            }
            whiteTextures[relativePath] = result;
            return result;
        }

        // 画像をそのまま描く(scale で拡大・縮小)。描けたら true
        public static bool Draw(string relativePath, float x, float y, float scale = 1f)
        {
            Texture2D? texture = GetTexture(relativePath);
            if (texture is not Texture2D tex) return false;

            Raylib.DrawTextureEx(tex, new Vector2(x, y), 0f, scale, Color.White);
            return true;
        }

        public static Texture2D? GetSoulExplosionDot() => SystemSkin ? null : soulExplosionDot;

        public static Texture2D? GetGaugeGlowCell(int lightColumn) =>
            !SystemSkin && lightColumn >= 0 && lightColumn < gaugeGlowCells.Length ? gaugeGlowCells[lightColumn] : null;

        private static void PrepareSoulExplosionDot()
        {
            if (GetTexture("Image/04.EnsoGame/Effects/Soul_Explosion.png") is not Texture2D atlas
                || atlas.Height <= 0 || atlas.Width < atlas.Height) return;

            int size = atlas.Height;
            bool hasOuterRing = atlas.Width >= size * 2;
            int tileX = hasOuterRing ? size : 0;
            SoulExplosionDotCount = hasOuterRing ? 12 : 8;
            float center = (size - 1) / 2f;
            float sectorSlope = MathF.Tan(MathF.PI / SoulExplosionDotCount);
            Image source = Raylib.LoadImageFromTexture(atlas);
            try
            {
                if (source.Width < tileX + size || source.Height < size) return;
                List<(int X, int Y, Color Pixel)> pixels = new();
                int left = size, top = size, right = -1, bottom = -1;
                double weight = 0, weightedY = 0;
                for (int y = 0; y < size; y++)
                {
                    float dy = y - center;
                    if (dy >= 0f) continue;
                    for (int x = 0; x < size; x++)
                    {
                        // 真上の丸の領域だけを取る。隣の丸が接していても混ざらない。
                        if (MathF.Abs(x - center) > -dy * sectorSlope) continue;
                        Color pixel = Raylib.GetImageColor(source, tileX + x, y);
                        if (pixel.A == 0) continue;
                        pixels.Add((x, y, pixel));
                        left = Math.Min(left, x); top = Math.Min(top, y);
                        right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                        weight += pixel.A;
                        weightedY += y * pixel.A;
                    }
                }
                if (weight == 0) return;

                // 周囲に透明な1pxを置き、補間で隣の丸の色を拾わないようにする。
                Image dot = Raylib.GenImageColor(right - left + 3, bottom - top + 3, Color.Blank);
                try
                {
                    foreach (var pixel in pixels)
                        Raylib.ImageDrawPixel(ref dot, pixel.X - left + 1, pixel.Y - top + 1, pixel.Pixel);
                    Texture2D texture = Raylib.LoadTextureFromImage(dot);
                    if (texture.Id != 0)
                    {
                        Raylib.SetTextureFilter(texture, TextureFilter.Bilinear);
                        soulExplosionDot = texture;
                        SoulExplosionRadius = center - (float)(weightedY / weight);
                    }
                }
                finally { Raylib.UnloadImage(dot); }
            }
            finally { Raylib.UnloadImage(source); }
        }

        // GPUから読むのも加工するのもスキン読込時だけ。演奏中は小さな画像を描くだけにする。
        private static void PrepareGaugeGlowCells()
        {
            const string dir = "Image/04.EnsoGame/Gauge/";
            if (GetTexture(dir + "Gauge_Base.png") is not Texture2D frame
                || GetTexture(dir + "Gauge_Light_Glow.png") is not Texture2D glow
                || frame.Width < 1195 || frame.Height < 84 || glow.Width < 120 || glow.Height < 83)
                return;

            Image mask = Raylib.LoadImageFromTexture(frame);
            Image bloom = Raylib.LoadImageFromTexture(glow);
            try
            {
                if (mask.Width < 1195 || mask.Height < 84 || bloom.Width < 120 || bloom.Height < 83) return;
                foreach (int column in new[] { 0, 3, 4, 5 })
                {
                    // マスの外側にあるぼかしも残す。配置は「マスの左端-6、ゲージ上端+3」。
                    const int width = 32, height = 81;
                    int templateCell = column == 0 ? 0 : column == 3 ? 39 : column == 4 ? 40 : 49;
                    int maskX = 12 + templateCell * 21 - 6;
                    int glowColumn = column == 0 ? 0 : column == 4 ? 3 : 2;
                    int sampleOffset = column == 4 ? 1 : 2;
                    Image cell = Raylib.GenImageColor(width, height, Color.Blank);
                    try
                    {
                        for (int y = 0; y < height; y++)
                        {
                            // 右端の丸みは段差用の上下反転。点灯面の中心は元画像のY=41.5。
                            int sourceY = column == 5 ? 83 - y : y;
                            if (sourceY >= 83) continue;
                            for (int x = 0; x < width; x++)
                            {
                                byte frameAlpha = Raylib.GetImageColor(mask, maskX + x, 3 + y).A;
                                if (frameAlpha == 0) continue;
                                int sourceX = x - sampleOffset;
                                Color a = sourceX >= 0 && sourceX < 30
                                    ? Raylib.GetImageColor(bloom, glowColumn * 30 + sourceX, sourceY) : Color.Blank;
                                Color b = sourceX + 1 >= 0 && sourceX + 1 < 30
                                    ? Raylib.GetImageColor(bloom, glowColumn * 30 + sourceX + 1, sourceY) : Color.Blank;
                                int alphaSum = a.A + b.A;
                                if (alphaSum == 0) continue;
                                // 半ピクセルの位置を補間。透明な画素の黒がぼかしに混ざらないようにする。
                                int round = alphaSum / 2;
                                int alpha = ((alphaSum + 1) / 2 * frameAlpha + 127) / 255;
                                Color color = new Color((byte)((a.R * a.A + b.R * b.A + round) / alphaSum),
                                    (byte)((a.G * a.A + b.G * b.A + round) / alphaSum),
                                    (byte)((a.B * a.A + b.B * b.A + round) / alphaSum), (byte)alpha);
                                Raylib.ImageDrawPixel(ref cell, x, y, color);
                            }
                        }
                        Texture2D texture = Raylib.LoadTextureFromImage(cell);
                        if (texture.Id != 0)
                        {
                            Raylib.SetTextureFilter(texture, TextureFilter.Bilinear);
                            gaugeGlowCells[column] = texture;
                        }
                    }
                    finally { Raylib.UnloadImage(cell); }
                }
            }
            finally
            {
                Raylib.UnloadImage(mask);
                Raylib.UnloadImage(bloom);
            }
        }

        // 黄色い部分だけを赤くする。白いふちや顔はそのまま。シェーダーはスキン適用時に準備する。
        public static bool BeginRollHeat(float heat)
        {
            if (heat <= 0f || rollHeatLocation < 0) return false;
            Raylib.SetShaderValue(rollHeatShader, rollHeatLocation, Math.Clamp(heat, 0f, 1f), ShaderUniformDataType.Float);
            Raylib.BeginShaderMode(rollHeatShader);
            return true;
        }

        private static void PrepareRollHeatShader()
        {
            rollHeatShader = Raylib.LoadShaderFromMemory(null!, RollHeatFragment);
            rollHeatLocation = Raylib.GetShaderLocation(rollHeatShader, "heat");
            if (rollHeatLocation < 0)
                Console.WriteLine("[Skin] 連打の着色シェーダーを準備できませんでした（通常の色で表示します）");
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
            PlayBackground.UnloadSkin();
            if (soulExplosionDot is Texture2D dot) Raylib.UnloadTexture(dot);
            soulExplosionDot = null;
            SoulExplosionDotCount = 12;
            SoulExplosionRadius = 0f;
            for (int i = 0; i < gaugeGlowCells.Length; i++)
            {
                if (gaugeGlowCells[i] is Texture2D cell) Raylib.UnloadTexture(cell);
                gaugeGlowCells[i] = null;
            }
            // 読み込み失敗時に返される既定シェーダーは解放しない。
            if (rollHeatLocation >= 0) Raylib.UnloadShader(rollHeatShader);
            rollHeatShader = default;
            rollHeatLocation = -1;
            foreach (Texture2D? texture in textures.Values)
            {
                if (texture is Texture2D tex) Raylib.UnloadTexture(tex);
            }
            textures.Clear();
            foreach (Texture2D? texture in whiteTextures.Values)
            {
                if (texture is Texture2D tex) Raylib.UnloadTexture(tex);
            }
            whiteTextures.Clear();
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