using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Raylib_cs;

namespace TaikoSim.Graphics
{
    // TaikoNautsスキンの背景。AviUtl2の座標・レイヤー・時間制御を読んで再生する。
    // パースとフレームの準備はスキン読込時だけ。演奏中は準備済みの画像を描く。
    public sealed class PlayBackground
    {
        private const string Root = "Image/04.EnsoGame/";
        private static BackgroundSet skin = new();
        private double elapsed, clearElapsed;
        private bool wasClear;
        private int generation = -1;

        public void Reset()
        {
            elapsed = clearElapsed = 0;
            wasClear = false;
            generation = SkinManager.Generation;
        }

        public void Update(float dt, bool isClear)
        {
            if (generation != SkinManager.Generation) Reset();
            elapsed += Math.Max(0f, dt);
            if (isClear != wasClear) clearElapsed = 0;
            else if (isClear) clearElapsed += Math.Max(0f, dt);
            wasClear = isClear;
        }

        public void Draw()
        {
            // 通常のAlpha合成は描画先のAにもSRC_ALPHAを掛け、半透明の重ね描きで
            // キャンバス自体が透けてしまう。RGBは通常のまま、Aだけ正しく積み重ねる。
            // これでウィンドウへ転送するときの二重の暗化を防ぎ、背景の明かりは残す。
            Rlgl.SetBlendFactorsSeparate(Rlgl.SRC_ALPHA, Rlgl.ONE_MINUS_SRC_ALPHA,
                Rlgl.ONE, Rlgl.ONE_MINUS_SRC_ALPHA, Rlgl.FUNC_ADD, Rlgl.FUNC_ADD);
            Raylib.BeginBlendMode(BlendMode.CustomSeparate);

            // キャンバスの外へ出るアニメ素材が、レーンや別の背景を覆わないようにする。
            Raylib.BeginScissorMode(0, 0, 1920, 276);
            DrawParts(skin.UpperNormal, elapsed, 0);
            if (wasClear) DrawParts(skin.UpperClear, elapsed, 0);
            Raylib.EndScissorMode();

            Raylib.BeginScissorMode(0, 540, 1920, 540);
            DrawParts(skin.LowerNormal, elapsed, 540);
            if (wasClear) DrawParts(skin.LowerClear, clearElapsed, 540);
            Raylib.EndScissorMode();

            bool useFooter = wasClear && skin.LowerClear.Length > 0
                ? skin.ClearUseFooter : skin.NormalUseFooter;
            if (useFooter && skin.Footer is Texture2D footer)
                Raylib.DrawTextureEx(footer, new Vector2(skin.FooterX, 1080 - footer.Height + skin.FooterY),
                    0f, 1f, Color.White);
            Raylib.EndBlendMode();
        }

        private static void DrawParts(Aup2Clip[] parts, double seconds, float y)
        {
            foreach (Aup2Clip part in parts) part.Draw(seconds, y);
        }

        internal static void UnloadSkin() => skin = new BackgroundSet();

        internal static void ReloadSkin()
        {
            skin = new BackgroundSet();
            if (SkinManager.CurrentPath == null) return;
            try
            {
                // 初回の比較は参考録画と同じ組み合わせを優先。無い場合は最初のdefaultを使う。
                string? upper = FindPreset(Root + "Background/Upper", "0");
                string? normal = FindPreset(Root + "Background/Lower/Normal", "2");
                string? clear = FindPreset(Root + "Background/Lower/Clear", "0");
                skin.UpperNormal = LoadParts(upper, "Normal_1P");
                skin.UpperClear = LoadParts(upper, "Clear_1P");
                if (skin.UpperClear.Length == 0) skin.UpperClear = LoadParts(upper, "Clear");
                skin.LowerNormal = LoadParts(normal);
                skin.LowerClear = LoadParts(clear);
                skin.NormalUseFooter = ReadBool(normal, "useFooter", true);
                skin.ClearUseFooter = ReadBool(clear, "useFooter", true);

                string? footer = FindPreset(Root + "Footer", "1");
                if (footer != null)
                {
                    skin.FooterX = ReadInt(footer, "x", 0);
                    skin.FooterY = ReadInt(footer, "y", 0);
                    // 提供スキンのreferencePoint=0（左上基準）。別形式は誤った位置に描かない。
                    if (ReadInt(footer, "referencePoint", 0) == 0)
                        skin.Footer = SkinManager.GetTexture(footer + "/0.png");
                }
                Console.WriteLine($"[Background] 上:{upper ?? "なし"} 下:{normal ?? "なし"} クリア:{clear ?? "なし"}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                Console.WriteLine($"[Background] 背景の準備に失敗しました: {ex.Message}");
            }
        }

        private static string FullPath(string relative) =>
            Path.Combine(SkinManager.CurrentPath!, relative.Replace('/', Path.DirectorySeparatorChar));

        // リザルト画面からも使う(スキンの中で、使うフォルダを探す)
        internal static string? FindPreset(string relative, string preferred)
        {
            string full = FullPath(relative);
            if (!Directory.Exists(full)) return null;
            return Directory.EnumerateDirectories(full)
                .OrderBy(path => Path.GetFileName(path) == preferred ? 0 : 1)
                .ThenBy(path => int.TryParse(Path.GetFileName(path), out int n) ? n : int.MaxValue)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => relative + "/" + Path.GetFileName(path))
                .FirstOrDefault(path => ReadString(path, "songGenreID", "default") == "default");
        }

        // loopKey … Config.json の「ここからくり返す」の名前(演奏画面は bgInEndFrame、リザルトのクリア背景は clearBgInEndFrame)
        internal static Aup2Clip[] LoadParts(string? preset, string child = "", string loopKey = "bgInEndFrame")
        {
            if (preset == null) return Array.Empty<Aup2Clip>();
            string relative = child.Length == 0 ? preset : preset + "/" + child;
            string full = FullPath(relative);
            if (!Directory.Exists(full)) return Array.Empty<Aup2Clip>();
            int loopStart = Math.Max(0, ReadInt(preset, loopKey, 0));
            List<Aup2Clip> parts = new();
            foreach (string folder in Directory.EnumerateDirectories(full)
                .OrderBy(path => int.TryParse(Path.GetFileName(path), out int n) ? n : int.MaxValue)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                string file = Path.Combine(folder, "Anime.aup2");
                if (!File.Exists(file)) continue;
                string project = relative + "/" + Path.GetFileName(folder) + "/Anime.aup2";
                try { parts.Add(Aup2Clip.Load(project, loopStart)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                    or FormatException or NotSupportedException or OverflowException)
                {
                    Console.WriteLine($"[Background] {project}: {ex.Message}");
                }
            }
            return parts.ToArray();
        }

        private static JsonElement ReadConfig(string? relative)
        {
            if (relative == null) return default;
            string path = FullPath(relative + "/Config.json");
            if (!File.Exists(path)) return default;
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.Clone();
        }

        private static int ReadInt(string? folder, string key, int fallback)
        {
            JsonElement config = ReadConfig(folder);
            return config.ValueKind == JsonValueKind.Object && config.TryGetProperty(key, out JsonElement value)
                && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : fallback;
        }

        private static bool ReadBool(string? folder, string key, bool fallback)
        {
            JsonElement config = ReadConfig(folder);
            return config.ValueKind == JsonValueKind.Object && config.TryGetProperty(key, out JsonElement value)
                && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;
        }

        private static string ReadString(string? folder, string key, string fallback)
        {
            JsonElement config = ReadConfig(folder);
            return config.ValueKind == JsonValueKind.Object && config.TryGetProperty(key, out JsonElement value)
                && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
        }

        private sealed class BackgroundSet
        {
            public Aup2Clip[] UpperNormal = Array.Empty<Aup2Clip>(), UpperClear = Array.Empty<Aup2Clip>();
            public Aup2Clip[] LowerNormal = Array.Empty<Aup2Clip>(), LowerClear = Array.Empty<Aup2Clip>();
            public bool NormalUseFooter = true, ClearUseFooter = true;
            public Texture2D? Footer;
            public int FooterX, FooterY;
        }

        // AviUtl2 のアニメ1つ分(リザルト画面からも使う)
        internal sealed class Aup2Clip
        {
            private readonly Command[][] frames;
            private readonly float fps;
            private readonly int loopStart;

            private Aup2Clip(Command[][] frames, float fps, int loopStart)
            {
                this.frames = frames;
                this.fps = fps;
                this.loopStart = Math.Clamp(loopStart, 0, frames.Length - 1);
            }

            public void Draw(double seconds, float y) => Draw(seconds, y, 1f);

            // alpha … 全体の濃さ(0〜1)。背景を切り替えるときにだんだん出す用
            public void Draw(double seconds, float y, float alpha)
            {
                if (alpha <= 0f) return;
                double position = Math.Floor(Math.Max(0, seconds) * fps);
                int frame = position < frames.Length ? (int)position
                    : loopStart + (int)((position - loopStart) % (frames.Length - loopStart));
                foreach (Command command in frames[frame])
                {
                    Rectangle destination = command.Destination;
                    destination.Y += y;
                    Color color = alpha >= 1f ? command.Color : Raylib.ColorAlpha(command.Color, command.Color.A / 255f * alpha);
                    Raylib.DrawTexturePro(command.Texture, command.Source, destination,
                        command.Origin, command.Rotation, color);
                }
            }

            public static Aup2Clip Load(string relative, int loopStart)
            {
                var sections = ReadSections(File.ReadAllLines(FullPath(relative)));
                var scene = sections.GetValueOrDefault("scene.0") ?? throw new FormatException("scene.0 がありません");
                float width = Number(scene, "video.width", 1920), height = Number(scene, "video.height", 276);
                float rateScale = Number(scene, "video.scale", 1);
                float fps = rateScale > 0 ? Number(scene, "video.rate", 60) / rateScale : 0;
                if (width <= 0 || height <= 0 || fps <= 0 || fps > 240)
                    throw new FormatException("キャンバスまたはフレームレートが不正です");
                List<TimelineObject> objects = new();
                foreach (var (name, values) in sections)
                {
                    if (!int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int id)) continue;
                    if (!values.TryGetValue("frame", out string? range)) continue;
                    string[] ends = range.Split(',');
                    if (ends.Length != 2) throw new FormatException("フレーム範囲が不正です");
                    int first = int.Parse(ends[0], CultureInfo.InvariantCulture);
                    int last = int.Parse(ends[1], CultureInfo.InvariantCulture);
                    if (first < 0 || last < first || last >= 36000)
                        throw new FormatException("フレーム範囲が大きすぎるか不正です");
                    var effects = sections.Where(pair => pair.Key.StartsWith(name + ".", StringComparison.Ordinal))
                        .OrderBy(pair => int.Parse(pair.Key[(name.Length + 1)..], CultureInfo.InvariantCulture))
                        .Select(pair => pair.Value).ToArray();
                    TimelineObject item = TimelineObject.Parse(id, (int)Number(values, "layer", 0), first, last,
                        effects, relative[..relative.LastIndexOf('/')]);
                    objects.Add(item);
                }
                int length = objects.Count == 0 ? 1 : objects.Max(item => item.Last) + 1;
                objects.Sort((a, b) => a.Layer != b.Layer ? a.Layer.CompareTo(b.Layer) : a.Id.CompareTo(b.Id));
                Command[][] frames = new Command[length][];
                for (int frame = 0; frame < length; frame++)
                {
                    List<Command> commands = new();
                    foreach (TimelineObject item in objects)
                    {
                        if (!item.Active(frame) || item.Texture is not Texture2D texture) continue;
                        Matrix3x2 transform = item.Transform(frame);
                        // 下の番号の制御から外側に重ねる。対象レイヤー数=0は、その下の全レイヤー。
                        for (int i = objects.Count - 1; i >= 0; i--)
                        {
                            TimelineObject group = objects[i];
                            if (group.IsGroup && group.Active(frame) && group.Layer < item.Layer
                                && (group.TargetLayers == 0 || item.Layer <= group.Layer + group.TargetLayers))
                                transform *= group.Transform(frame);
                        }
                        float opacity = Math.Clamp(1f - item.Transparency.At(item.Progress(frame)) / 100f, 0f, 1f);
                        if (opacity <= 0) continue;
                        Vector2 center = Vector2.Transform(Vector2.Zero, transform) + new Vector2(width / 2, height / 2);
                        float scaleX = new Vector2(transform.M11, transform.M12).Length();
                        float scaleY = new Vector2(transform.M21, transform.M22).Length();
                        if (scaleX < 0.0001f || scaleY < 0.0001f) continue;
                        Rectangle source = item.Source(frame, texture);
                        if (source.Width <= 0 || source.Height <= 0) continue;
                        float angle = MathF.Atan2(transform.M12, transform.M11) * 180f / MathF.PI;
                        float w = source.Width * scaleX, h = source.Height * scaleY;
                        // クリッピング後も画像の元の中心を保ち、残った部分だけを描く。
                        Vector2 origin = new((texture.Width / 2f - source.X) * scaleX,
                            (texture.Height / 2f - source.Y) * scaleY);
                        commands.Add(new Command(texture, source, new Rectangle(center.X, center.Y, w, h),
                            origin, angle, Raylib.ColorAlpha(Color.White, opacity)));
                    }
                    frames[frame] = commands.ToArray();
                }
                return new Aup2Clip(frames, fps, loopStart);
            }

            private readonly record struct Command(Texture2D Texture, Rectangle Source, Rectangle Destination,
                Vector2 Origin, float Rotation, Color Color);

            private sealed class TimelineObject
            {
                public int Id, Layer, First, Last, TargetLayers;
                public bool IsGroup;
                public Texture2D? Texture;
                public Curve X, Y, Rotation, Transparency, Scale = new(100), Aspect;
                public Curve ExtraScale = new(100), ExtraX = new(100), ExtraY = new(100);
                public Curve CropTop, CropBottom, CropLeft, CropRight;
                public bool Active(int frame) => frame >= First && frame <= Last;
                public float Progress(int frame) => Last == First ? 0f : (float)(frame - First) / (Last - First);

                public Matrix3x2 Transform(int frame)
                {
                    float t = Progress(frame);
                    float scale = Scale.At(t) * ExtraScale.At(t) / 10000f;
                    float aspect = Math.Clamp(Aspect.At(t) / 100f, -1f, 1f);
                    float sx = scale * ExtraX.At(t) / 100f * (aspect < 0 ? 1f + aspect : 1f);
                    float sy = scale * ExtraY.At(t) / 100f * (aspect > 0 ? 1f - aspect : 1f);
                    return Matrix3x2.CreateScale(sx, sy)
                        * Matrix3x2.CreateRotation(Rotation.At(t) * MathF.PI / 180f)
                        * Matrix3x2.CreateTranslation(X.At(t), Y.At(t));
                }

                public Rectangle Source(int frame, Texture2D texture)
                {
                    float t = Progress(frame);
                    float left = Math.Clamp(CropLeft.At(t), 0, texture.Width);
                    float top = Math.Clamp(CropTop.At(t), 0, texture.Height);
                    float right = Math.Clamp(CropRight.At(t), 0, texture.Width - left);
                    float bottom = Math.Clamp(CropBottom.At(t), 0, texture.Height - top);
                    return new Rectangle(left, top, texture.Width - left - right, texture.Height - top - bottom);
                }

                public static TimelineObject Parse(int id, int layer, int first, int last,
                    Dictionary<string, string>[] effects, string directory)
                {
                    TimelineObject result = new() { Id = id, Layer = layer, First = first, Last = last };
                    foreach (var effect in effects)
                    {
                        switch (effect.GetValueOrDefault("effect.name"))
                        {
                            case "画像ファイル":
                                string file = effect.GetValueOrDefault("ファイル", "").Replace('\\', '/');
                                // 作者のPCの絶対パスは使わず、プロジェクトと同じフォルダの画像を使う。
                                string name = file[(file.LastIndexOf('/') + 1)..];
                                if (name.Length > 0) result.Texture = SkinManager.GetTexture(directory + "/" + name);
                                break;
                            case "グループ制御":
                                result.IsGroup = true;
                                result.TargetLayers = (int)Number(effect, "対象レイヤー数", 0);
                                ReadTransform(result, effect);
                                break;
                            case "標準描画":
                                if (effect.GetValueOrDefault("合成モード", "通常") != "通常")
                                    throw new NotSupportedException("通常以外の合成モードは未対応です");
                                ReadTransform(result, effect);
                                result.Transparency = Track(effect, "透明度", 0);
                                result.Aspect = Track(effect, "縦横比", 0);
                                break;
                            case "拡大率":
                                result.ExtraScale = Track(effect, "拡大率", 100);
                                result.ExtraX = Track(effect, "X", 100);
                                result.ExtraY = Track(effect, "Y", 100);
                                break;
                            case "クリッピング":
                                result.CropTop = Track(effect, "上", 0);
                                result.CropBottom = Track(effect, "下", 0);
                                result.CropLeft = Track(effect, "左", 0);
                                result.CropRight = Track(effect, "右", 0);
                                break;
                            default:
                                throw new NotSupportedException($"未対応の背景効果: {effect.GetValueOrDefault("effect.name")}");
                        }
                    }
                    return result;
                }

                private static void ReadTransform(TimelineObject target, Dictionary<string, string> effect)
                {
                    if (!Track(effect, "X軸回転", 0).IsZero || !Track(effect, "Y軸回転", 0).IsZero)
                        throw new NotSupportedException("3D回転は未対応です");
                    target.X = Track(effect, "X", 0);
                    target.Y = Track(effect, "Y", 0);
                    target.Rotation = Track(effect, "Z軸回転", 0);
                    target.Scale = Track(effect, "拡大率", 100);
                }
            }

            private readonly struct Curve
            {
                private readonly float from, to, x1, y1, x2, y2;
                private readonly bool ease;
                public bool IsZero => from == 0 && to == 0;
                public Curve(float value) { from = to = value; x1 = y1 = 0; x2 = y2 = 1; ease = false; }
                public Curve(string value)
                {
                    string[] parts = value.Split('|');
                    string[] values = parts[0].Split(',');
                    from = Parse(values[0]);
                    to = values.Length > 1 ? Parse(values[1]) : from;
                    x1 = y1 = 0; x2 = y2 = 1; ease = false;
                    if (values.Length > 2 && values[2] is not ("直線移動" or "直線移動(時間制御)" or "補間移動(時間制御)"))
                        throw new NotSupportedException($"未対応の補間: {values[2]}");
                    if (parts.Length > 1)
                    {
                        string[] handles = parts[1].Split(',');
                        if (handles.Length != 4) throw new FormatException("時間制御のハンドルが不正です");
                        // AviUtl2は両端からのハンドル量を記録する。終点側を(1-x,1-y)へ直す。
                        x1 = Parse(handles[0]); y1 = Parse(handles[1]);
                        x2 = 1f - Parse(handles[2]); y2 = 1f - Parse(handles[3]);
                        ease = true;
                    }
                }
                public float At(float t)
                {
                    if (from == to) return from;
                    t = Math.Clamp(t, 0f, 1f);
                    if (ease)
                    {
                        float low = 0, high = 1;
                        for (int i = 0; i < 14; i++)
                        {
                            float middle = (low + high) * 0.5f;
                            if (Bezier(middle, x1, x2) < t) low = middle;
                            else high = middle;
                        }
                        t = Bezier((low + high) * 0.5f, y1, y2);
                    }
                    return from + (to - from) * t;
                }
                private static float Bezier(float t, float a, float b) =>
                    3f * (1f - t) * (1f - t) * t * a + 3f * (1f - t) * t * t * b + t * t * t;
            }

            private static Curve Track(Dictionary<string, string> values, string key, float fallback) =>
                values.TryGetValue(key, out string? value) ? new Curve(value) : new Curve(fallback);
            private static float Parse(string value)
            {
                float result = float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
                if (!float.IsFinite(result)) throw new FormatException("有限でない数値です");
                return result;
            }
            private static float Number(Dictionary<string, string> values, string key, float fallback) =>
                values.TryGetValue(key, out string? value) ? Parse(value) : fallback;

            private static Dictionary<string, Dictionary<string, string>> ReadSections(string[] lines)
            {
                Dictionary<string, Dictionary<string, string>> sections = new(StringComparer.Ordinal);
                Dictionary<string, string>? current = null;
                foreach (string raw in lines)
                {
                    string line = raw.Trim().TrimStart('\uFEFF');
                    if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
                    if (line.StartsWith('[') && line.EndsWith(']'))
                    {
                        current = new Dictionary<string, string>(StringComparer.Ordinal);
                        sections[line[1..^1]] = current;
                    }
                    else if (current != null)
                    {
                        int equals = line.IndexOf('=');
                        if (equals >= 0) current[line[..equals].Trim()] = line[(equals + 1)..].Trim();
                    }
                }
                return sections;
            }
        }
    }
}