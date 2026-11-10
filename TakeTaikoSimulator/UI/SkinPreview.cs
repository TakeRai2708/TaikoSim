using System.Numerics;
using System.Text.Json;
using Raylib_cs;
using TakeTaikoSimulator.Graphics;

namespace TakeTaikoSimulator.UI
{
    // 設定の「スキン」で、選んでいるスキンの音符を並べて見せる(まだ切り替えていないスキンも見られる)
    //   今使っているスキン(SkinManager)ではなく、選んだスキンのフォルダから直接読む。スキンを変えたら読み直す
    //   並べるもの：判定枠・ドン・カッ・大ドン・大カッ・連打(頭・胴・おわり)・風船
    public sealed class SkinPreview
    {
        private const string Notes1 = "Image/04.EnsoGame/Notes/Normal/1/"; // 顔つき(無いスキンは 0 番)
        private const string Notes0 = "Image/04.EnsoGame/Notes/Normal/0/";
        private const string Frame = "Image/04.EnsoGame/Notes/JudgementFrame.png";
        private static readonly string[] NoteFiles = { "0.png", "1.png", "2.png", "3.png", "4.png", "5.png", "6.png", "10.png" }; // ドン・カッ・大ドン・大カッ・連打(頭・胴・おわり)・風船

        private string? loadedPath;
        private readonly Dictionary<string, Texture2D> textures = new();

        public string DisplayName { get; private set; } = "";
        public string Version { get; private set; } = "";
        public string Description { get; private set; } = "";

        // skinPath のスキンを読む(前と同じなら何もしない)
        public void Load(string? skinPath)
        {
            if (skinPath == loadedPath) return;
            Unload();
            loadedPath = skinPath;
            DisplayName = skinPath != null ? Path.GetFileName(skinPath) : "";
            if (skinPath == null || !Directory.Exists(skinPath)) return;

            void Add(string key, string rel)
            {
                string path = Path.Combine(skinPath, rel.Replace('/', Path.DirectorySeparatorChar));
                if (SkinManager.LoadTextureFile(path) is Texture2D t) textures[key] = t;
            }
            Add("frame", Frame);
            foreach (string file in NoteFiles)
            {
                Add(file, Notes1 + file);
                if (!textures.ContainsKey(file)) Add(file, Notes0 + file);
            }
            ReadInfo(skinPath);
        }

        public void Unload()
        {
            foreach (Texture2D t in textures.Values) Raylib.UnloadTexture(t);
            textures.Clear();
            loadedPath = null;
            DisplayName = Version = Description = "";
        }

        private void ReadInfo(string skinPath)
        {
            string path = Path.Combine(skinPath, "SkinConfig.json");
            if (!File.Exists(path)) return;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                JsonElement root = doc.RootElement;
                if (root.TryGetProperty("skinName", out JsonElement name) && name.ValueKind == JsonValueKind.String) DisplayName = name.GetString() ?? DisplayName;
                if (root.TryGetProperty("skinVersion", out JsonElement version) && version.ValueKind == JsonValueKind.String) Version = version.GetString() ?? "";
                if (root.TryGetProperty("skinDescription", out JsonElement desc) && desc.ValueKind == JsonValueKind.String) Description = desc.GetString() ?? "";
            }
            catch (Exception ex) when (ex is JsonException || ex is IOException || ex is InvalidOperationException)
            {
                Console.WriteLine($"[Skin] プレビュー：SkinConfig.json を読めませんでした: {ex.Message}");
            }
        }

        // box の中に、左から並べて描く(入りきるように小さくする)。1枚も無ければ false
        //   音符の画像は 195×195(連打の胴は横に伸ばす、風船は 390×195)
        public bool Draw(Rectangle box)
        {
            if (textures.Count == 0) return false;
            // 連打の胴は音符の大きさに合わせて伸ばす(固定の長さだと、小さく描いたときに頭と尻尾が重なってはみ出して見える)
            const float note = 195f, gap = 14f;
            const float rollBody = note * 1.2f;
            // 並べるもの7つ：判定枠・ドン・カッ・大ドン・大カッ・連打(頭+胴)・風船(390幅=音符2つ分)。すき間は6か所
            float total = note * 5 + (note + rollBody) + note * 2 + gap * 6;
            float s = Math.Min(0.5f, (box.Width - 24f) / total);
            float size = note * s;
            float x = box.X + 10f, y = box.Y + (box.Height - size) / 2f;

            void One(string key)
            {
                if (textures.TryGetValue(key, out Texture2D t))
                    Raylib.DrawTexturePro(t, new Rectangle(0, 0, t.Width, t.Height), new Rectangle(x, y, t.Width * s, t.Height * s), Vector2.Zero, 0f, Color.White);
            }

            foreach (string key in new[] { "frame", "0.png", "1.png", "2.png", "3.png" })
            {
                One(key);
                x += size + gap * s;
            }

            // 連打：胴を伸ばして(頭の中心〜おわり)、おわり、頭の順に重ねる
            if (textures.TryGetValue("5.png", out Texture2D body) && textures.TryGetValue("4.png", out Texture2D head)
                && textures.TryGetValue("6.png", out Texture2D tail))
            {
                float bodyX = x + size / 2f, bodyW = rollBody * s;
                Raylib.DrawTexturePro(body, new Rectangle(0, 0, body.Width, body.Height), new Rectangle(bodyX, y, bodyW, size), Vector2.Zero, 0f, Color.White);
                // おわりの画像は、丸いところが画像の左側にある。演奏画面(PlayScene)と同じく、画像の左はしを胴の終わりに置く
                //   (前は半分左にずれていて、丸が胴の中に埋まり、おわりが四角く見えていた)
                Raylib.DrawTexturePro(tail, new Rectangle(0, 0, tail.Width, tail.Height), new Rectangle(bodyX + bodyW, y, size, size), Vector2.Zero, 0f, Color.White);
                Raylib.DrawTexturePro(head, new Rectangle(0, 0, head.Width, head.Height), new Rectangle(x, y, size, size), Vector2.Zero, 0f, Color.White);
            }
            x += size + rollBody * s + gap * s;

            One("10.png"); // 風船(横長)
            return true;
        }
    }
}
