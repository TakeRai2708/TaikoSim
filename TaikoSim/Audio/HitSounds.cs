using System.Text.Json;
using TaikoSim.Configs;
using TaikoSim.Graphics;

namespace TaikoSim.Audio
{
    // 音色(ドン・カッの音)の一覧
    //
    // OpenTaiko と同じ作り：exe の横の HitSounds/ フォルダに、音色ごとのフォルダを並べる
    //   HitSounds/名前/dong.ogg(または .wav)…… ドン(OpenTaiko の名前。don.ogg でもOK)
    //   HitSounds/名前/ka.ogg(または .wav)  …… カッ
    //   HitSounds/名前/HitSounds.json        …… 表示する名前(無ければフォルダ名)
    //     { "name": "名前" } か、OpenTaiko の { "name": { "strings": { "default": "...", "ja": "..." } } }
    //   OpenTaiko の Global/HitSounds の中身を、そのまま入れても使える
    // いちばん上(番号 0)は「スキンの音」(スキンの Sound/Taiko/0.Taiko/don.ogg・ka.ogg)
    // 選んだ音色は GameConfig.HitSound にフォルダ名で保存する(空ならスキンの音)
    public static class HitSounds
    {
        public static string Folder => Path.Combine(AppContext.BaseDirectory, "HitSounds");
        private const string SkinDon = "Sound/Taiko/0.Taiko/don.ogg";
        private const string SkinKa = "Sound/Taiko/0.Taiko/ka.ogg";

        public sealed record Entry(string Key, string Name, string? Don, string? Ka);

        private static List<Entry>? cache;
        private static int cacheSkin = -1;

        // 一覧(スキンを切り替えたら作り直す)
        public static List<Entry> List
        {
            get
            {
                if (cache != null && cacheSkin == SkinManager.Generation) return cache;
                cacheSkin = SkinManager.Generation;
                cache = Scan();
                return cache;
            }
        }

        // フォルダを入れ直したときに、一覧を作り直す
        public static void Refresh() => cache = null;

        private static List<Entry> Scan()
        {
            List<Entry> list = new();
            string? skin = SkinManager.CurrentPath;
            list.Add(new Entry("", "スキン", skin == null ? null : Path.Combine(skin, SkinDon), skin == null ? null : Path.Combine(skin, SkinKa)));
            try
            {
                if (!Directory.Exists(Folder)) return list;
                foreach (string dir in Directory.GetDirectories(Folder).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    string key = Path.GetFileName(dir);
                    string? don = FindFile(dir, "dong") ?? FindFile(dir, "don");
                    string? ka = FindFile(dir, "ka");
                    if (don == null && ka == null) continue; // 音が1つも無いフォルダは出さない
                    list.Add(new Entry(key, ReadName(dir) ?? key, don, ka));
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Console.WriteLine($"[音色] HitSounds フォルダを読めませんでした：{ex.Message}");
            }
            return list;
        }

        private static string? FindFile(string dir, string name)
        {
            foreach (string ext in new[] { ".ogg", ".wav", ".mp3" })
            {
                string path = Path.Combine(dir, name + ext);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        // HitSounds.json の名前(日本語があれば日本語)
        private static string? ReadName(string dir)
        {
            string path = Path.Combine(dir, "HitSounds.json");
            if (!File.Exists(path)) return null;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                if (!doc.RootElement.TryGetProperty("name", out JsonElement name)) return null;
                if (name.ValueKind == JsonValueKind.String) return name.GetString();
                if (name.ValueKind == JsonValueKind.Object && name.TryGetProperty("strings", out JsonElement strings))
                {
                    if (strings.TryGetProperty("ja", out JsonElement ja) && ja.GetString() is string j && j.Length > 0) return j;
                    if (strings.TryGetProperty("default", out JsonElement def)) return def.GetString();
                }
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is InvalidOperationException)
            {
                Console.WriteLine($"[音色] {path} を読めませんでした：{ex.Message}");
            }
            return null;
        }

        // 今選んでいる音色の番号(見つからなければ 0 = スキン)
        public static int SelectedIndex
        {
            get
            {
                string key = ConfigManager.Game.HitSound ?? "";
                int index = List.FindIndex(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                return Math.Max(0, index);
            }
        }

        public static Entry Selected => List[SelectedIndex];

        // ドン・カッのファイル(選んだ音色に無ければ、スキンの音)
        public static string? DonPath => Exists(Selected.Don) ?? Exists(List[0].Don);
        public static string? KaPath => Exists(Selected.Ka) ?? Exists(List[0].Ka);

        private static string? Exists(string? path) => path != null && File.Exists(path) ? path : null;

        // ===== 試し聞き(演奏オプションで音色を変えたとき：ドン → 0.15秒あとにカッ) =====
        private const float PreviewKaDelay = 0.15f;
        private static SoundEffect? previewDon, previewKa;
        private static string previewKey = "\u0000";
        private static int previewAudio = -1;
        private static float kaTimer = -1f;

        public static void PlayPreview()
        {
            Entry e = Selected;
            if (previewKey != e.Key || previewAudio != AudioManager.Generation)
            {
                if (previewAudio == AudioManager.Generation) { previewDon?.Dispose(); previewKa?.Dispose(); }
                previewDon = DonPath is string d ? SoundEffect.Load(d, out _, 2) : null;
                previewKa = KaPath is string k ? SoundEffect.Load(k, out _, 2) : null;
                previewKey = e.Key;
                previewAudio = AudioManager.Generation;
            }
            previewDon?.Play(Volume);
            kaTimer = PreviewKaDelay;
        }

        // 毎フレーム(カッを少し遅らせて鳴らすため)
        public static void Update(float dt)
        {
            if (kaTimer < 0f) return;
            kaTimer -= dt;
            if (kaTimer < 0f) previewKa?.Play(Volume);
        }

        private static float Volume => (float)ConfigManager.Sound.GetFinalVolume(ConfigManager.Sound.SeVolume);
    }
}
