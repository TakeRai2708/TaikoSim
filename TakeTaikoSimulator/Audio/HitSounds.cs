using System.Text.Json;
using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Graphics;

namespace TakeTaikoSimulator.Audio
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
    // その次に、スキンの中の neiro フォルダの音色（11/14 たけらい「スキンの中に neiro っていうフォルダの中を参照してそれを選べるように」）
    //   スキン/neiro/名前/don.ogg・ka.ogg（例：neiro/taiko/don.ogg）。名前はフォルダ名（HitSounds.json があればその名前）
    //   保存するときの名前は「neiro/フォルダ名」（exe の横の HitSounds の音色と区別するため）
    //   ※ 大文字・小文字はどちらでもいい（Neiro・NEIRO も見る）
    // 選んだ音色は GameConfig.HitSound にフォルダ名で保存する(空ならスキンの音)
    public static class HitSounds
    {
        public static string Folder => Configs.DataPaths.Data("HitSounds");
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
            if (skin != null) AddFolder(list, FindNeiroFolder(skin), "neiro/");
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

        private const string NeiroFolderName = "neiro";

        private static string? FindNeiroFolder(string skin)
        {
            try
            {
                foreach (string dir in Directory.GetDirectories(skin))
                    if (Path.GetFileName(dir).Equals(NeiroFolderName, StringComparison.OrdinalIgnoreCase)) return dir;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            return null;
        }

        // folder の中のフォルダを1つずつ音色にする（key は prefix ＋ フォルダ名）
        private static void AddFolder(List<Entry> list, string? folder, string prefix)
        {
            if (folder == null) return;
            try
            {
                foreach (string dir in Directory.GetDirectories(folder).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    string name = Path.GetFileName(dir);
                    string? don = FindFile(dir, "don") ?? FindFile(dir, "dong");
                    string? ka = FindFile(dir, "ka");
                    if (don == null && ka == null) continue;
                    list.Add(new Entry(prefix + name, ReadName(dir) ?? name, don, ka));
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Console.WriteLine($"[音色] {folder} を読めませんでした：{ex.Message}");
            }
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
        //   11/19 たけらい「演奏画面と鳴り方が同じじゃない。たくさん押すと重なる。最初のドンがちゃんと出ない」：
        //   ・演奏画面(PlayScene)と同じく、ドン・カッそれぞれ重ねて鳴らせる数を 1 にした(新しい音が前の音を止める)
        //   ・前は音色を変えるたびに、その場で読み込んで、その場で鳴らしていた。
        //     読み込みと同じフレームで鳴らすと最初のドンがちゃんと出ないことがあったので、
        //     読み込んだ音は音色ごとに取っておき、鳴らすのは次のフレーム(Update)にした
        private const float PreviewKaDelay = 0.15f;
        private static readonly Dictionary<string, (SoundEffect? Don, SoundEffect? Ka)> previews = new();
        private static int previewAudio = -1;
        private static (SoundEffect? Don, SoundEffect? Ka) current;
        private static bool donPending;
        private static float kaTimer = -1f;

        public static void PlayPreview()
        {
            // 出力方式を作り直したら、前の音は使えないので捨てる(古い番号は別の音に使われているかもしれないので Dispose しない)
            if (previewAudio != AudioManager.Generation)
            {
                previews.Clear();
                previewAudio = AudioManager.Generation;
            }

            Entry e = Selected;
            if (!previews.TryGetValue(e.Key, out current))
            {
                current = (DonPath is string d ? SoundEffect.Load(d, out _, 1) : null,
                           KaPath is string k ? SoundEffect.Load(k, out _, 1) : null);
                previews[e.Key] = current;
            }

            // 連打したときは、前のカッを止めて、新しいドンからやり直す
            foreach (var p in previews.Values) p.Ka?.Stop();
            donPending = true;
            kaTimer = -1f;
        }

        // 毎フレーム(ドンは次のフレームで、カッは 0.15 秒あとに鳴らす)
        public static void Update(float dt)
        {
            if (donPending)
            {
                donPending = false;
                current.Don?.Play(Volume);
                kaTimer = PreviewKaDelay;
                return;
            }
            if (kaTimer < 0f) return;
            kaTimer -= dt;
            if (kaTimer < 0f) current.Ka?.Play(Volume);
        }

        private static float Volume => (float)ConfigManager.Sound.GetFinalVolume(ConfigManager.Sound.SeVolume);
    }
}
