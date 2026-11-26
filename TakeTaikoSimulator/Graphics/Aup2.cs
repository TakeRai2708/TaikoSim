using System.Globalization;
using System.Numerics;
using Raylib_cs;

namespace TakeTaikoSimulator.Graphics
{
    // AviUtl2 のプロジェクトファイル（Anime.aup2）を読んで、アニメとして描く。
    //   TaikøNauts は段位道場の入り（06.DanSelect/Background）・演奏中の背景（08.DanGame/Background）・
    //   リザルトのスタンプ（09.DanResult/Stamp）などを aup2 で作っている。これを読めれば本家と同じ動きになる。
    //
    //   TaikøNauts のドキュメント（TAIKONAUTS-SPEC.md の 11章）に書いてある対応範囲と同じものだけ読む：
    //     標準描画：X / Y / 中心X / 中心Y / Z軸回転 / 拡大率 / 透明度 / 合成モード（通常・加算・乗算）
    //     グループ制御：X / Y だけ（下の「対象レイヤー数」ぶんのレイヤーをずらす）
    //     拡大率（フィルタ）：拡大率 / X / Y
    //     時間制御は「開始点と終了点」の2点だけ（直線移動、直線移動(時間制御)）
    //   音声・動画は読まない（ドキュメントでも音声はデモ画面だけ）。
    //
    //   座標：AviUtl は画面のまんなかが (0, 0)。画像は「位置 (X, Y)」に「画像のまんなか＋中心X/Y」の点を合わせて置き、
    //         拡大・回転もその点を軸にする（扉 Loading-Right.png：X=960・中心X=480 → 画像の右はしが画面の右はしに来る）。
    //   コマ：60コマ/秒（video.rate）。オブジェクトは frame=開始,終了 のコマの間だけ出る（両はし含む）。
    public sealed class Aup2
    {
        private enum Blend { Normal, Add, Multiply }

        // 「100.000」か「100.000,114.000,直線移動(時間制御),0|0.2,0.8,0.4,0.05」
        private readonly struct Track
        {
            public readonly float From, To;
            public readonly bool Moves;
            public readonly Vector4? Curve;   // 時間制御のベジェ（x1, y1, x2, y2）。無ければまっすぐ

            public Track(float value) { From = To = value; Moves = false; Curve = null; }
            public Track(float from, float to, Vector4? curve) { From = from; To = to; Moves = true; Curve = curve; }

            public float At(float t)
            {
                if (!Moves) return From;
                if (Curve is Vector4 c) t = Bezier(c, t);
                return From + (To - From) * t;
            }

            public static Track Parse(string? text, float fallback)
            {
                if (string.IsNullOrWhiteSpace(text)) return new Track(fallback);
                string[] parts = text.Split(',');
                if (!TryFloat(parts[0], out float from)) return new Track(fallback);
                if (parts.Length < 3 || !TryFloat(parts[1], out float to)) return new Track(from);

                Vector4? curve = null;
                int bar = text.IndexOf('|');
                if (parts[2].Contains("時間制御") && bar >= 0)
                {
                    string[] c = text[(bar + 1)..].Split(',');
                    if (c.Length >= 4 && TryFloat(c[0], out float x1) && TryFloat(c[1], out float y1)
                        && TryFloat(c[2], out float x2) && TryFloat(c[3], out float y2))
                        // 推測なんだけど：AviUtl2 の時間制御の y は「上が 0」の向きで保存されているみたい。
                        //   そのまま使うと拡大率が行きすぎて戻る変な形になるので、1 - y にして普通のイーズにする
                        curve = new Vector4(x1, 1f - y1, x2, 1f - y2);
                }
                return new Track(from, to, curve);
            }
        }

        private sealed class Item
        {
            public int Layer, Start, End;
            public string? Image;                 // 画像のファイル名（無ければグループ制御だけのオブジェクト）
            public Track X = new(0), Y = new(0), CenterX = new(0), CenterY = new(0), Rotation = new(0);
            public Track Scale = new(100), Alpha = new(0);
            public Track ResizeAll = new(100), ResizeX = new(100), ResizeY = new(100);
            public Blend Blend;
            public int GroupLayers;               // グループ制御のときだけ：下の何レイヤーに効くか
            public bool IsGroup;
            public bool FlipX, FlipY;
        }

        private readonly List<Item> items = new();
        private readonly string folder;
        private readonly Dictionary<string, Texture2D> textures = new();

        /// <summary>
        /// aup2 の画面の大きさ（[scene.0] の video.width / video.height）。座標の (0,0) はこの画面のまんなか。
        /// 段位の入りは 1920×1080 だけど、演奏中の上の背景（08.DanGame/Background）は 1920×276 なので、ファイルごとに読む
        /// </summary>
        public float Width { get; private set; } = 1920f;
        public float Height { get; private set; } = 1080f;

        /// <summary>いちばん最後のコマ（これを過ぎたら終わり）</summary>
        public int LastFrame { get; private set; }

        /// <summary>Config.json の _loopStartFrame（ループするアニメのとき。無ければ -1）</summary>
        public int LoopStartFrame { get; private set; } = -1;

        public const float FramesPerSecond = 60f;

        private Aup2(string folder) => this.folder = folder;

        /// <summary>aup2 を読む。無い・読めないときは null</summary>
        public static Aup2? Load(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                Aup2 anime = new(Path.GetDirectoryName(path) ?? "");
                anime.Parse(File.ReadAllText(path));
                anime.ReadConfig(Path.Combine(anime.folder, "Config.json"));
                return anime;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[aup2] 読めませんでした：{path}：{ex.Message}");
                return null;
            }
        }

        private void ReadConfig(string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("_loopStartFrame", out var loop) && loop.TryGetInt32(out int frame))
                    LoopStartFrame = frame;
            }
            catch { /* 読めなければループなし */ }
        }

        // [0] [0.0] [0.1] … の並び。[n] がオブジェクト、[n.m] がその中の効果
        private void Parse(string text)
        {
            Item? current = null;
            string? section = null;
            Dictionary<string, string> values = new();

            void Flush()
            {
                if (section == null) return;
                if (IsObjectSection(section))
                {
                    current = new Item();
                    current.Layer = ToInt(values.GetValueOrDefault("layer"), 0);
                    string[] frame = (values.GetValueOrDefault("frame") ?? "0,0").Split(',');
                    current.Start = ToInt(frame[0], 0);
                    current.End = frame.Length > 1 ? ToInt(frame[1], current.Start) : current.Start;
                    items.Add(current);
                    LastFrame = Math.Max(LastFrame, current.End);
                }
                else if (current != null && section.Contains('.') && IsObjectSection(section.Split('.')[0]))
                {
                    ApplyEffect(current, values);
                }
                values.Clear();
            }

            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    Flush();
                    section = line[1..^1];
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq > 0) values[line[..eq]] = line[(eq + 1)..];
                if (section == "scene.0" && eq > 0)
                {
                    string key = line[..eq], value = line[(eq + 1)..];
                    if (key == "video.width" && TryFloat(value, out float w) && w > 0) Width = w;
                    if (key == "video.height" && TryFloat(value, out float h) && h > 0) Height = h;
                }
            }
            Flush();

            // 画像も効果も無いもの（音声など）は捨てる
            items.RemoveAll(i => i.Image == null && !i.IsGroup);
            items.Sort((a, b) => a.Layer != b.Layer ? a.Layer.CompareTo(b.Layer) : a.Start.CompareTo(b.Start));
        }

        private static bool IsObjectSection(string name) => name.Length > 0 && name.All(char.IsDigit);

        private static void ApplyEffect(Item item, Dictionary<string, string> v)
        {
            switch (v.GetValueOrDefault("effect.name"))
            {
                case "画像ファイル":
                    // 作った人の PC の絶対パスのこともあるので、ファイル名だけ取る
                    string file = v.GetValueOrDefault("ファイル") ?? "";
                    item.Image = file.Replace('\\', '/').Split('/').Last();
                    break;
                case "標準描画":
                    item.X = Track.Parse(v.GetValueOrDefault("X"), 0);
                    item.Y = Track.Parse(v.GetValueOrDefault("Y"), 0);
                    item.CenterX = Track.Parse(v.GetValueOrDefault("中心X"), 0);
                    item.CenterY = Track.Parse(v.GetValueOrDefault("中心Y"), 0);
                    item.Rotation = Track.Parse(v.GetValueOrDefault("Z軸回転"), 0);
                    item.Scale = Track.Parse(v.GetValueOrDefault("拡大率"), 100);
                    item.Alpha = Track.Parse(v.GetValueOrDefault("透明度"), 0);
                    item.Blend = v.GetValueOrDefault("合成モード") switch
                    {
                        "加算" => Blend.Add,
                        "乗算" => Blend.Multiply,
                        _ => Blend.Normal,
                    };
                    break;
                case "拡大率":
                    item.ResizeAll = Track.Parse(v.GetValueOrDefault("拡大率"), 100);
                    item.ResizeX = Track.Parse(v.GetValueOrDefault("X"), 100);
                    item.ResizeY = Track.Parse(v.GetValueOrDefault("Y"), 100);
                    break;
                case "反転":
                    item.FlipX = ToInt(v.GetValueOrDefault("左右反転"), 0) != 0;
                    item.FlipY = ToInt(v.GetValueOrDefault("上下反転"), 0) != 0;
                    break;
                case "グループ制御":
                    item.IsGroup = true;
                    item.X = Track.Parse(v.GetValueOrDefault("X"), 0);
                    item.Y = Track.Parse(v.GetValueOrDefault("Y"), 0);
                    item.GroupLayers = ToInt(v.GetValueOrDefault("対象レイヤー数"), 0);
                    break;
            }
        }

        /// <summary>秒 → コマ。ずっとくり返す背景用（最後のコマの次は 0 コマ目へ）</summary>
        public float LoopedFrameAt(float seconds)
        {
            float length = LastFrame + 1f;
            return (seconds * FramesPerSecond) % length;
        }

        /// <summary>秒 → コマ。ループ指定があれば、最後まで行ったらループの頭へ戻る</summary>
        public float FrameAt(float seconds)
        {
            float frame = seconds * FramesPerSecond;
            if (LoopStartFrame >= 0 && frame > LastFrame && LastFrame > LoopStartFrame)
                frame = LoopStartFrame + (frame - LoopStartFrame) % (LastFrame - LoopStartFrame + 1);
            return frame;
        }

        /// <summary>
        /// frame コマ目の絵を描く。offset は aup2 の画面の左上を、ゲームの画面のどこに置くか（ふつうは 0）。
        /// holdLast を true にすると、最後のコマを過ぎても最後の絵を出しつづける。
        /// </summary>
        // 絵のあるオブジェクトのうち、いちばん下のレイヤー（ネームプレートなら称号の背景）
        public int BottomImageLayer => items.Where(i => !i.IsGroup && i.Image != null).Select(i => i.Layer).DefaultIfEmpty(0).Min();

        /// <summary>minLayer〜maxLayer のレイヤーだけ描く（間に別の絵を挟みたいとき用）</summary>
        public void Draw(float frame, Vector2 offset, float alpha, int minLayer, int maxLayer) =>
            DrawCore(frame, offset, alpha, false, minLayer, maxLayer);

        public void Draw(float frame, Vector2 offset = default, float alpha = 1f, bool holdLast = false) =>
            DrawCore(frame, offset, alpha, holdLast, int.MinValue, int.MaxValue);

        private void DrawCore(float frame, Vector2 offset, float alpha, bool holdLast, int minLayer, int maxLayer)
        {
            if (holdLast) frame = Math.Min(frame, LastFrame);
            int whole = (int)MathF.Floor(frame);

            foreach (Item item in items)
            {
                if (item.IsGroup || whole < item.Start || whole > item.End) continue;
                if (item.Layer < minLayer || item.Layer > maxLayer) continue;
                float t = Progress(item, frame);

                // このレイヤーにかかっているグループ制御のずらし
                Vector2 group = Vector2.Zero;
                foreach (Item g in items)
                {
                    if (!g.IsGroup || whole < g.Start || whole > g.End) continue;
                    if (item.Layer <= g.Layer) continue;
                    if (g.GroupLayers > 0 && item.Layer > g.Layer + g.GroupLayers) continue;
                    float gt = Progress(g, frame);
                    group += new Vector2(g.X.At(gt), g.Y.At(gt));
                }

                DrawItem(item, t, offset + group, alpha);
            }
        }

        private static float Progress(Item item, float frame) =>
            item.End <= item.Start ? 0f : Math.Clamp((frame - item.Start) / (item.End - item.Start), 0f, 1f);

        private void DrawItem(Item item, float t, Vector2 offset, float alpha)
        {
            if (item.Image == null || Texture(item.Image) is not Texture2D tex) return;

            float opacity = (1f - item.Alpha.At(t) / 100f) * alpha;
            if (opacity <= 0.001f) return;

            float scale = item.Scale.At(t) / 100f * item.ResizeAll.At(t) / 100f;
            float sx = scale * item.ResizeX.At(t) / 100f, sy = scale * item.ResizeY.At(t) / 100f;
            if (sx <= 0f || sy <= 0f) return;

            // 画像の中の軸（まんなか＋中心X/Y）を、画面の (960 + X, 540 + Y) に合わせる
            float pivotX = tex.Width / 2f + item.CenterX.At(t), pivotY = tex.Height / 2f + item.CenterY.At(t);
            if (item.FlipX) pivotX = tex.Width - pivotX;
            if (item.FlipY) pivotY = tex.Height - pivotY;
            Vector2 at = new(Width / 2f + item.X.At(t) + offset.X, Height / 2f + item.Y.At(t) + offset.Y);

            Raylib.BeginBlendMode(item.Blend switch
            {
                Blend.Add => BlendMode.Additive,
                Blend.Multiply => BlendMode.Multiplied,
                _ => BlendMode.Alpha,
            });
            Raylib.DrawTexturePro(tex, new Rectangle(0, 0, item.FlipX ? -tex.Width : tex.Width, item.FlipY ? -tex.Height : tex.Height),
                new Rectangle(at.X, at.Y, tex.Width * sx, tex.Height * sy),
                new Vector2(pivotX * sx, pivotY * sy), item.Rotation.At(t),
                new Color(255, 255, 255, (int)Math.Round(255 * Math.Clamp(opacity, 0f, 1f))));
            Raylib.EndBlendMode();
        }

        private Texture2D? Texture(string name)
        {
            if (textures.TryGetValue(name, out Texture2D tex)) return tex.Id == 0 ? null : tex;
            string path = Path.Combine(folder, name);
            tex = File.Exists(path) ? Raylib.LoadTexture(path) : default;
            if (tex.Id != 0) Raylib.SetTextureFilter(tex, TextureFilter.Bilinear);
            textures[name] = tex;
            return tex.Id == 0 ? null : tex;
        }

        /// <summary>使っている画像を先に全部読んでおく（初めて出るときに止まらないように）</summary>
        public bool HasAllImages => items.Any(i => i.Image != null)
            && items.Where(i => i.Image != null).All(i => Texture(i.Image!) != null);

        public void Preload()
        {
            foreach (Item item in items)
                if (item.Image != null) Texture(item.Image);
        }

        public void Unload()
        {
            foreach (Texture2D tex in textures.Values)
                if (tex.Id != 0) Raylib.UnloadTexture(tex);
            textures.Clear();
        }

        // CSS の cubic-bezier と同じ：x が時間、y が進み具合。x から t を二分探索で求めて y を返す
        private static float Bezier(Vector4 c, float x)
        {
            if (x <= 0f) return 0f;
            if (x >= 1f) return 1f;
            float lo = 0f, hi = 1f, t = x;
            for (int i = 0; i < 24; i++)
            {
                t = (lo + hi) / 2f;
                float bx = Cubic(c.X, c.Z, t);
                if (bx < x) lo = t; else hi = t;
            }
            return Cubic(c.Y, c.W, t);
        }

        private static float Cubic(float p1, float p2, float t)
        {
            float u = 1f - t;
            return 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t;
        }

        private static bool TryFloat(string s, out float value) =>
            float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        private static int ToInt(string? s, int fallback) =>
            int.TryParse(s?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;
    }
}
