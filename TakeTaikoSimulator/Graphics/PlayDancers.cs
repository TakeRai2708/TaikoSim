using System.Numerics;
using System.Text.Json;
using Raylib_cs;

namespace TakeTaikoSimulator.Graphics
{
    // 演奏画面の下で踊るダンサー（TaikøNauts の Image/04.EnsoGame/Dancer/）
    //   11/19 たけらい「背景のダンサー表示」。作りは TaikøNauts の説明書（TAIKONAUTS-SPEC.md の 8. Dancer）どおり：
    //   ・Dancer/[組]/MainConfig.json … dancerShowCount（何人）、dancerTypeLock（その場所に出す個体。-1 でランダム）、
    //     dancerGaugePercent（魂ゲージが何%で出てくるか）
    //   ・Dancer/[組]/[個体]/Config.json … 登場・ループ・退場の長さ（小節）、場所（dancerInX/Y・LoopX/Y・OutX/Y）、基準点(BasePoint)、使う画像の順番
    //   ・In … 連番画像。Loop … Anime.aup2（あれば。無ければ連番画像）。Out … useSameMotionForInOut なら In の逆再生
    //   場所の数字は、連番画像なら基準点(7 = 下のまん中)の場所、aup2 ならキャンバスの左上（A-Style の 0番：
    //   In は (960,1080) を下のまん中にした 540×630、Loop は 550×630 のキャンバスの左上が (685,450) で、ちょうど重なる）
    //   ゲージが下がって条件を割ったら退場、また上がったら登場する（推測なんだけど、OpenTaiko も同じ動き）
    //   重さ対策：連番は1コマおきに読み、まわりの透明を切り取り、同じ中身のファイルは1枚にまとめる
    public sealed class PlayDancers
    {
        private const string Root = "Image/04.EnsoGame/Dancer/";

        // ===== スキンから読んだもの（曲ごとにランダムで選び直す） =====
        private sealed class Frame
        {
            public Texture2D Texture;
            public float OffsetX, OffsetY;   // 切り取る前の画像の左上から、残した部分の左上まで
            public float Width, Height;      // 切り取る前の画像の大きさ
        }

        private sealed class DancerType
        {
            public Frame?[] In = Array.Empty<Frame?>();     // dancerInMotionIndex の順（読まなかったコマは null）
            public Frame?[] Out = Array.Empty<Frame?>();
            public Frame?[] Loop = Array.Empty<Frame?>();
            public PlayBackground.Aup2Clip? LoopClip;
            public bool OutReversesIn = true;   // useSameMotionForInOut：退場は登場の逆再生
            public float InMeasures = 1, LoopMeasures = 4, OutMeasures = 1;
            public int InBase = 7, LoopBase = 7, OutBase = 7;
            public float[] InX = Array.Empty<float>(), InY = Array.Empty<float>();
            public float[] LoopX = Array.Empty<float>(), LoopY = Array.Empty<float>();
            public float[] OutX = Array.Empty<float>(), OutY = Array.Empty<float>();
        }

        private static DancerType?[] slotTypes = Array.Empty<DancerType?>();
        private static float[] slotPercent = Array.Empty<float>();
        private static readonly List<Texture2D> loaded = new();

        // ===== 1曲ぶんの動き =====
        private enum State { Hidden, In, Loop, Out }
        private State[] state = Array.Empty<State>();
        private double[] progress = Array.Empty<double>(); // 今の動きの何割まで進んだか(0〜1)。Loop は何周目かも込み

        public static void Unload()
        {
            foreach (Texture2D texture in loaded) Raylib.UnloadTexture(texture);
            loaded.Clear();
            slotTypes = Array.Empty<DancerType?>();
            slotPercent = Array.Empty<float>();
        }

        // 組を1つ選んで、出す個体を決めて読み込む（PlayBackground.Randomize から。曲の読み込み中）
        public static void Randomize(string? set)
        {
            Unload();
            if (set == null) return;
            try
            {
                JsonElement main = ReadJson(set + "/MainConfig.json");
                if (main.ValueKind != JsonValueKind.Object) return;
                int count = Math.Clamp(ReadInt(main, "dancerShowCount", 0), 0, 16);
                int[] locks = ReadInts(main, "dancerTypeLock");
                float[] percents = ReadFloats(main, "dancerGaugePercent");
                string full = PlayBackground.FullPath(set);
                int[] available = Directory.EnumerateDirectories(full)
                    .Select(path => int.TryParse(Path.GetFileName(path), out int n) ? n : -1)
                    .Where(n => n >= 0).OrderBy(n => n).ToArray();
                if (available.Length == 0 || count == 0) return;

                var cache = new Dictionary<int, DancerType?>();
                var types = new DancerType?[count];
                for (int i = 0; i < count; i++)
                {
                    int want = i < locks.Length ? locks[i] : -1;
                    int type = want >= 0 && available.Contains(want) ? want : available[Random.Shared.Next(available.Length)];
                    if (!cache.TryGetValue(type, out DancerType? dancer))
                    {
                        dancer = LoadType(set + "/" + type);
                        cache[type] = dancer;
                    }
                    types[i] = dancer;
                }
                slotTypes = types;
                slotPercent = Enumerable.Range(0, count).Select(i => i < percents.Length ? percents[i] : 0f).ToArray();
                Console.WriteLine($"[Dancer] {set}（{count}人・画像 {loaded.Count}枚）");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException)
            {
                Console.WriteLine($"[Dancer] 読み込みに失敗しました: {ex.Message}");
                Unload();
            }
        }

        private static DancerType? LoadType(string folder)
        {
            JsonElement config = ReadJson(folder + "/Config.json");
            if (config.ValueKind != JsonValueKind.Object) return null;
            var dancer = new DancerType
            {
                InMeasures = Math.Max(0.01f, ReadFloat(config, "inMotionMeasureCount", 1)),
                LoopMeasures = Math.Max(0.01f, ReadFloat(config, "loopMotionMeasureCount", 4)),
                OutMeasures = Math.Max(0.01f, ReadFloat(config, "outMotionMeasureCount", 1)),
                InBase = ReadInt(config, "inMotionBasePoint", 7),
                LoopBase = ReadInt(config, "loopMotionBasePoint", 7),
                OutBase = ReadInt(config, "outMotionBasePoint", 7),
                InX = ReadFloats(config, "dancerInX"), InY = ReadFloats(config, "dancerInY"),
                LoopX = ReadFloats(config, "dancerLoopX"), LoopY = ReadFloats(config, "dancerLoopY"),
                OutX = ReadFloats(config, "dancerOutX"), OutY = ReadFloats(config, "dancerOutY"),
            };
            bool sameImage = ReadBool(config, "useSameImageForInOut", true);
            bool sameMotion = ReadBool(config, "useSameMotionForInOut", true);
            var files = new Dictionary<string, Frame?>();

            dancer.In = LoadSequence(folder + "/In", ReadInts(config, "dancerInMotionIndex"), files);
            dancer.OutReversesIn = sameMotion;
            if (!sameMotion) dancer.Out = LoadSequence(folder + (sameImage ? "/In" : "/Out"), ReadInts(config, "dancerOutMotionIndex"), files);

            string loopAup2 = folder + "/Loop/Anime.aup2";
            if (File.Exists(PlayBackground.FullPath(loopAup2)))
            {
                try { dancer.LoopClip = PlayBackground.Aup2Clip.Load(loopAup2, 0); }
                catch (Exception ex) when (ex is IOException or FormatException or NotSupportedException or OverflowException)
                {
                    Console.WriteLine($"[Dancer] {loopAup2}: {ex.Message}");
                }
            }
            if (dancer.LoopClip == null) dancer.Loop = LoadSequence(folder + "/Loop", ReadInts(config, "dancerLoopMotionIndex"), files);
            return dancer;
        }

        // 連番画像を読む。1コマおき（偶数番目だけ）、同じ中身のファイルは1枚にまとめる
        private static Frame?[] LoadSequence(string folder, int[] order, Dictionary<string, Frame?> files)
        {
            var frames = new Frame?[order.Length];
            var byContent = new Dictionary<string, Frame?>();
            for (int i = 0; i < order.Length; i += 2)
            {
                string path = PlayBackground.FullPath($"{folder}/{order[i]}.png");
                if (files.TryGetValue(path, out Frame? frame)) { frames[i] = frame; continue; }
                frame = null;
                if (File.Exists(path))
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    string key = bytes.Length + ":" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(bytes));
                    if (!byContent.TryGetValue(key, out frame))
                    {
                        frame = LoadFrame(bytes);
                        byContent[key] = frame;
                    }
                }
                files[path] = frame;
                frames[i] = frame;
            }
            return frames;
        }

        private static Frame? LoadFrame(byte[] bytes)
        {
            Image image = Raylib.LoadImageFromMemory(".png", bytes);
            if (image.Width <= 0) return null;
            float width = image.Width, height = image.Height;
            // 11/19 たけらい：ダンサーの足もとに、ダンサー3人ぶんの幅の黒い線が出た。切り取った画像のはしと同じ高さだったので、
            //   まわりの透明を切り取るのをやめた（何も写っていないコマだけ読まない）。重さは「1コマおき・同じ中身はまとめる」で減らす
            Rectangle box = Raylib.GetImageAlphaBorder(image, 0f);
            if (box.Width <= 0 || box.Height <= 0) { Raylib.UnloadImage(image); return null; } // まだ何も写っていないコマ
            box = new Rectangle(0, 0, width, height);
            Texture2D texture = Raylib.LoadTextureFromImage(image);
            Raylib.UnloadImage(image);
            Raylib.SetTextureFilter(texture, TextureFilter.Bilinear);
            // 11/19 たけらい「ダンサーの上に微妙に黒線ある」：くり返し(Repeat)のままだと、なめらかにするときに
            //   上のはしが下のはしの色を拾って線になる。はしの色をそのまま使う(Clamp)
            Raylib.SetTextureWrap(texture, TextureWrap.Clamp);
            loaded.Add(texture);
            return new Frame { Texture = texture, OffsetX = box.X, OffsetY = box.Y, Width = width, Height = height };
        }

        // ===== 動き =====
        public void Reset()
        {
            state = new State[slotTypes.Length];
            progress = new double[slotTypes.Length];
        }

        public void Update(float dt, float gaugePercent, double measureSeconds)
        {
            if (state.Length != slotTypes.Length) Reset();
            double measures = Math.Max(0f, dt) / Math.Max(0.1, measureSeconds);
            for (int i = 0; i < state.Length; i++)
            {
                DancerType? type = slotTypes[i];
                if (type == null) continue;
                bool wanted = gaugePercent >= slotPercent[i];
                switch (state[i])
                {
                    case State.Hidden:
                        if (wanted) { state[i] = State.In; progress[i] = 0; }
                        break;
                    case State.In:
                        progress[i] += measures / type.InMeasures;
                        if (progress[i] >= 1) { state[i] = wanted ? State.Loop : State.Out; progress[i] = 0; }
                        break;
                    case State.Loop:
                        progress[i] += measures / type.LoopMeasures;
                        if (!wanted) { state[i] = State.Out; progress[i] = 0; }
                        break;
                    case State.Out:
                        progress[i] += measures / type.OutMeasures;
                        if (progress[i] >= 1) { state[i] = State.Hidden; progress[i] = 0; }
                        break;
                }
            }
        }

        public void Draw()
        {
            for (int i = 0; i < state.Length; i++)
            {
                DancerType? type = slotTypes[i];
                if (type == null || state[i] == State.Hidden) continue;
                switch (state[i])
                {
                    case State.In:
                        DrawSequence(type.In, progress[i], type.InBase, At(type.InX, i), At(type.InY, i));
                        break;
                    case State.Out:
                        if (type.OutReversesIn) DrawSequence(type.In, 1 - progress[i], type.OutBase, At(type.OutX, i), At(type.OutY, i));
                        else DrawSequence(type.Out, progress[i], type.OutBase, At(type.OutX, i), At(type.OutY, i));
                        break;
                    case State.Loop:
                        double phase = progress[i] - Math.Floor(progress[i]);
                        if (type.LoopClip is PlayBackground.Aup2Clip clip)
                            clip.DrawFrame((int)(phase * clip.FrameCount), At(type.LoopX, i), At(type.LoopY, i), 1f);
                        else
                            DrawSequence(type.Loop, phase, type.LoopBase, At(type.LoopX, i), At(type.LoopY, i));
                        break;
                }
            }
        }

        private static float At(float[] values, int index) => index < values.Length ? values[index] : 0f;

        // progress(0〜1)のコマを、基準点(0〜8：0 左上 … 4 中心 … 7 下のまん中 … 8 右下)に合わせて描く
        private static void DrawSequence(Frame?[] frames, double progress, int basePoint, float x, float y)
        {
            if (frames.Length == 0) return;
            int index = Math.Clamp((int)(progress * frames.Length), 0, frames.Length - 1);
            index -= index % 2; // 1コマおきに読んでいるので、偶数番目に寄せる
            Frame? frame = frames[index];
            if (frame == null) return;
            float ax = basePoint % 3 * 0.5f, ay = Math.Clamp(basePoint, 0, 8) / 3 * 0.5f;
            // 半端な位置だと、はしがにじんで線に見えるので、ピクセルにそろえる
            Vector2 at = new(MathF.Round(x - frame.Width * ax + frame.OffsetX), MathF.Round(y - frame.Height * ay + frame.OffsetY));
            Raylib.DrawTextureV(frame.Texture, at, Color.White);
        }

        // ===== JSON =====
        private static JsonElement ReadJson(string relative)
        {
            string path = PlayBackground.FullPath(relative);
            if (!File.Exists(path)) return default;
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.Clone();
        }

        private static int ReadInt(JsonElement e, string key, int fallback) =>
            e.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : fallback;

        private static float ReadFloat(JsonElement e, string key, float fallback) =>
            e.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? (float)v.GetDouble() : fallback;

        private static bool ReadBool(JsonElement e, string key, bool fallback) =>
            e.TryGetProperty(key, out JsonElement v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : fallback;

        private static int[] ReadInts(JsonElement e, string key) =>
            e.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.Array
                ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt32()).ToArray()
                : Array.Empty<int>();

        private static float[] ReadFloats(JsonElement e, string key) =>
            e.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.Array
                ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => (float)x.GetDouble()).ToArray()
                : Array.Empty<float>();
    }
}
