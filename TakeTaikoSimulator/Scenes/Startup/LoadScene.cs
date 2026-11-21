using System.Numerics;
using Raylib_cs;
using System.Diagnostics;
using TakeTaikoSimulator.Charts;
using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Graphics;

namespace TakeTaikoSimulator.Scenes
{
    // 起動したときの読み込み画面
    // スキン → 曲 の順に読んで、終わったらモード選択へ(叩くキーが決まっていなければ初回起動セットアップへ)
    public class LoadingScene : Scene
    {
        private enum Step
        {
            Skin,  // スキンを読む
            Songs, // 曲を読む(裏で)
            Done,  // 読み終わった(少しだけ「完了」を見せてから次へ)
            Leave, // 次の画面へ行った
        }

        private Step step = Step.Skin;
        private int skinFrames = 0;     // スキンを読む前に、画面を何回描いたか
        private bool skinDone = false;
        private IEnumerator<(int Done, int Total, string Stage)>? skinLoader;
        private int skinLoaded, skinTotal;
        private string skinStage = "スキンを準備しています";
        private float doneTimer = 0f;

        // 読み終わってから次へ進むまでの時間(秒)
        private const float DoneWait = 0.5f;  // ふつう(「完了」がチラッと見える)
        private const float ErrorWait = 2.0f; // 読めなかった曲があるとき(読めるように長め。Enter で飛ばせる)

        // ===== 配置(1920×1080) =====
        private const float Left = 160;
        private const float Right = 1760;

        private static readonly Color BackColor = UI.Gui.BackgroundColor;
        private static readonly Color SubColor = UI.Gui.SubTextColor;
        private static readonly Color BarColor = UI.Gui.AccentColor;

        // ===== まんなかのロゴ(11/12) =====
        //   たけらい「TakeTaikoSimulator の字になってるけど、これ(丸いアイコン)を真ん中において、周りを控えめなグローに」
        //   絵は Assets/Branding/Logo.png(640×640。exe に埋め込み。スキンを読む前でも出せる)。画面には 400×400 で出す。
        //   光：絵のうしろに、朱色の光。4秒かけて、ゆっくり明るくなったり暗くなったりする(±15%)
        //   11/22 たけらい「グローの広がり方が汚い」：前はうすい丸を10枚重ねていて、10段の輪っかが見えていた。
        //   → 光の画像を1枚つくって(外へなめらかに消える曲線＋細かいノイズで段を消す)、それを1回だけ描く
        private const float LogoSize = 400f, LogoCenterY = 370f;
        private const float LogoGlowInner = LogoSize * 0.43f;    // ここまでは同じ濃さ（絵の丸のふちのすこし内側）
        private const float LogoGlowSpread = 120f;               // ふちから外へ、この長さで消える
        private const float LogoGlowAlpha = 0.42f;               // いちばん濃いところ
        private const int LogoGlowTextureSize = 512;
        private static Texture2D logoGlow;
        private static Texture2D logo;
        private static bool logoTried;
        private static float logoClock;

        private static void DrawLogo(float cx)
        {
            if (!logoTried)
            {
                logoTried = true;
                using Stream? stream = typeof(LoadingScene).Assembly.GetManifestResourceStream("TakeTaikoSimulator.Logo.png");
                if (stream != null)
                {
                    using MemoryStream bytes = new();
                    stream.CopyTo(bytes);
                    Image image = Raylib.LoadImageFromMemory(".png", bytes.ToArray());
                    if (image.Width > 0)
                    {
                        logo = Raylib.LoadTextureFromImage(image);
                        Raylib.GenTextureMipmaps(ref logo);
                        Raylib.SetTextureFilter(logo, TextureFilter.Trilinear);
                        Raylib.UnloadImage(image);
                    }
                }
            }
            if (logo.Id == 0)
            {
                // 絵が読めなかったときは、前と同じ字
                Vector2 name = FontManager.Measure("TakeTaikoSimulator", 80);
                FontManager.Draw("TakeTaikoSimulator", cx - name.X / 2f, 330, 80, Color.White);
                return;
            }

            logoClock += Raylib.GetFrameTime();
            float pulse = 1f + 0.15f * MathF.Sin(logoClock * MathF.PI * 2f / 4f);
            Vector2 center = new Vector2(cx, LogoCenterY);
            if (logoGlow.Id == 0) logoGlow = MakeLogoGlow();
            float glowRadius = LogoGlowInner + LogoGlowSpread;
            Raylib.DrawTexturePro(logoGlow, new Rectangle(0, 0, logoGlow.Width, logoGlow.Height),
                new Rectangle(cx - glowRadius, LogoCenterY - glowRadius, glowRadius * 2f, glowRadius * 2f), Vector2.Zero, 0f,
                UI.Gui.WithAlpha(Color.White, Math.Clamp(pulse / 1.15f, 0f, 1f)));
            Raylib.DrawTexturePro(logo, new Rectangle(0, 0, logo.Width, logo.Height),
                new Rectangle(cx - LogoSize / 2f, LogoCenterY - LogoSize / 2f, LogoSize, LogoSize), Vector2.Zero, 0f, Color.White);
        }
        // ロゴのうしろの光の画像（1回だけ作る）。真ん中から LogoGlowInner までは同じ濃さ、そこから外へ
        //   (1 - t)^2.4 で消えていく(t = ふちからの距離 ÷ LogoGlowSpread)。消える所でカクッとしないように、最後は傾き 0 で 0 になる。
        //   8bit の濃さだと暗い背景で段が見えるので、±0.5段ぶんの細かいノイズを混ぜる(ディザ)
        private static unsafe Texture2D MakeLogoGlow()
        {
            const int size = LogoGlowTextureSize;
            Image image = Raylib.GenImageColor(size, size, Color.Blank);
            Raylib.ImageFormat(ref image, PixelFormat.UncompressedR8G8B8A8);
            byte* px = (byte*)image.Data;
            Color c = UI.Gui.AccentColor;
            float radius = LogoGlowInner + LogoGlowSpread;
            float peak = LogoGlowAlpha * 1.15f;   // ゆらぎのいちばん明るい時の濃さ（描くときに 1/1.15 倍まで下げる）
            uint seed = 12345;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float d = MathF.Sqrt(dx * dx + dy * dy) * radius;            // 画面の px での中心からの距離
                float t = Math.Clamp((d - LogoGlowInner) / LogoGlowSpread, 0f, 1f);
                float a = peak * MathF.Pow(1f - t, 2.4f);
                seed = seed * 1664525u + 1013904223u;
                float noise = ((seed >> 8) / 16777216f - 0.5f) / 255f;       // ±0.5段
                byte alpha = a <= 0f ? (byte)0 : (byte)Math.Clamp((int)MathF.Round((a + noise) * 255f), 0, 255);
                int i = (y * size + x) * 4;
                px[i] = c.R; px[i + 1] = c.G; px[i + 2] = c.B; px[i + 3] = alpha;
            }
            Texture2D texture = Raylib.LoadTextureFromImage(image);
            Raylib.UnloadImage(image);
            Raylib.SetTextureFilter(texture, TextureFilter.Bilinear);
            return texture;
        }

        private static readonly Color ErrorColor = new Color(255, 110, 100, 255);

        // ===== 見た目（Motion Prompt #089 ローディング画面。11/10） =====
        // Motion Prompt #089 · MIT License (c) 2026 Shohei Ohtaki · https://showheyohtaki.github.io/motion-prompt/#089
        //   暗い画面のまんなかに、ロゴ(11/12 から丸いアイコンの絵)・% ・細いバー。読み終わったら、（11/22 から）画面ごと透けて消えて（0.6秒・easeInOutCubic）、
        //   下から次の画面が明るくなって出てくる（0.6秒）。
        //   #089 は % が決まった時間で 0→100 に進むけど、TakeTaikoSimulator は本当の進み具合（スキン半分・曲半分）を、なめらかに追いかけて出す。
        //   スキン・曲の細かい進み具合は、前と同じ内容を小さい字で下に残す（cdc7b38 の進捗バー）
        private const float LiftSeconds = 0.6f;   // 幕が透けて消えるまで
        private const float BarWidth = 640f, BarHeight = 8f;
        private float shownProgress;   // 画面に出している進み具合（本当の値をなめらかに追いかける）

        // 透けて消えていく幕（次の画面に切りかわったあとも、SceneManager.DrawFade から描く）
        private static float liftTime = -1f;
        public static bool LiftActive => liftTime >= 0f;
        private static string liftSkin = "";

        /// <summary>透けて消えていく幕を描く（SceneManager.DrawFade から毎フレーム）</summary>
        // 11/22 たけらい「ロード終わったときはフェードインで」→ 上へ抜ける動き(#089)をやめて、クロスフェード（黒は挟まない）。
        //   幕は図形・文字・グローの重ね描きなので、そのまま半透明で描くと重なりがムラになる。
        //   だから、読み終わった瞬間の幕を1枚の絵に撮っておき、その絵を 0.6秒で透かしていく（easeInOutCubic）
        public static void DrawLift()
        {
            if (liftTime < 0f) return;
            liftTime += Math.Min(Raylib.GetFrameTime(), 1f / 30f);
            float k = Math.Clamp(liftTime / LiftSeconds, 0f, 1f);
            if (k >= 1f) { liftTime = -1f; FreeSnapshot(); return; }
            float e = k < 0.5f ? 4f * k * k * k : 1f - MathF.Pow(-2f * k + 2f, 3f) / 2f;
            Color tint = Raylib.ColorAlpha(Color.White, 1f - e);
            if (liftSnapshot.Id != 0)
                Raylib.DrawTexturePro(liftSnapshot, new Rectangle(0, 0, liftSnapshot.Width, liftSnapshot.Height),
                    new Rectangle(0, 0, Screen.Width, Screen.Height), Vector2.Zero, 0f, tint);
            else
                Raylib.DrawRectangle(0, 0, Screen.Width, Screen.Height, Raylib.ColorAlpha(BackColor, 1f - e));
        }

        private static Texture2D liftSnapshot;

        // 読み終わった瞬間の幕を 1920×1080 の絵に撮る（Update の中＝画面を描く前に呼ぶ）
        private static unsafe void TakeSnapshot()
        {
            FreeSnapshot();
            RenderTexture2D rt = Raylib.LoadRenderTexture(Screen.Width, Screen.Height);
            if (rt.Id == 0) return;
            Raylib.BeginTextureMode(rt);
            Raylib.ClearBackground(BackColor);
            DrawCurtain(1f, liftSkin, null);
            Raylib.EndTextureMode();
            // 半透明の重ね描きで、絵の透明度(α)が1より下がったところがあると、そこだけ早く消えて見える。全部 α=255 にそろえる
            Image img = Raylib.LoadImageFromTexture(rt.Texture);
            Raylib.UnloadRenderTexture(rt);
            Raylib.ImageFormat(ref img, PixelFormat.UncompressedR8G8B8A8);
            Raylib.ImageFlipVertical(ref img);   // RenderTexture は上下逆さま
            byte* px = (byte*)img.Data;
            int count = img.Width * img.Height;
            for (int i = 0; i < count; i++) px[i * 4 + 3] = 255;
            liftSnapshot = Raylib.LoadTextureFromImage(img);
            Raylib.UnloadImage(img);
            Raylib.SetTextureFilter(liftSnapshot, TextureFilter.Bilinear);
        }

        private static void FreeSnapshot()
        {
            if (liftSnapshot.Id != 0) Raylib.UnloadTexture(liftSnapshot);
            liftSnapshot = default;
        }

        // 幕の中身：背景・ロゴ・%・バー（detail は下の小さい字。null なら出さない）
        private static void DrawCurtain(float progress, string skinLine, Action? detail)
        {
            Raylib.DrawRectangle(0, 0, Screen.Width, Screen.Height, BackColor);
            float cx = Screen.Width / 2f;
            DrawLogo(cx);
            // ロゴの下に、% と細いバー(ロゴが主役なので、% は前より小さく)
            string percent = $"{(int)MathF.Round(progress * 100f)}%";
            Vector2 p = FontManager.Measure(percent, 44);
            FontManager.Draw(percent, cx - p.X / 2f, 630, 44, UI.Gui.TextColor);
            Rectangle bar = new(cx - BarWidth / 2f, 700, BarWidth, BarHeight);
            SmoothShapes.Rounded(bar, 1f, 8, UI.Gui.TrackColor);
            if (progress > 0f)
            {
                Rectangle fill = new Rectangle(bar.X, bar.Y, Math.Max(BarHeight, BarWidth * progress), BarHeight);
                UI.Gui.Glow(fill, BarHeight / 2f, UI.Gui.AccentColor, 0.5f);
                SmoothShapes.Rounded(fill, 1f, 8, BarColor);
            }
            if (skinLine.Length > 0)
            {
                Vector2 s = FontManager.Measure(skinLine, 26);
                FontManager.Draw(skinLine, cx - s.X / 2f, 740, 26, SubColor);
            }
            detail?.Invoke();
        }

        // 読み込み中は設定パネルを開けない(スキンを読む前なので、パネルの絵が出せない)
        public override bool CanOpenSettings => false;

        public override Platform.DiscordPresence.Info? Presence => new("起動中");

        public override void Enter()
        {
            fadeInTime = 0f;
        }

        // 11/22 たけらい「最初の起動の画面はフェードインで」：黒から 0.6秒でじわっと出す（easeOutCubic）。
        //   読み込みで1コマが長くなっても途中が飛ばないよう、1コマで進むのは 1/30秒まで
        private const float FadeInSeconds = 0.6f;
        private float fadeInTime;

        // 読み込み中は Esc を使わない
        public override void OnBack()
        {
        }

        public override void Update(float dt)
        {
            switch (step)
            {
                case Step.Skin:
                    // GPUへの転送はメインスレッドで少しずつ行い、合間に進捗を描く。
                    skinFrames++;
                    if (skinFrames < 2) return;

                    skinLoader ??= SkinManager.ReloadSteps().GetEnumerator();
                    // 1フレームの処理は約8msを目安に区切る。画像1枚やフォントなどの
                    // 単独工程に8ms以上かかる場合は、その工程が終わってから画面を描く。
                    long started = Stopwatch.GetTimestamp();
                    bool finished = false;
                    do
                    {
                        if (!skinLoader.MoveNext()) { finished = true; break; }
                        (skinLoaded, skinTotal, skinStage) = skinLoader.Current;
                        if (skinLoaded == skinTotal) break; // 100%の画面を1フレーム見せる
                    }
                    while (Stopwatch.GetElapsedTime(started).TotalMilliseconds < 8);
                    if (!finished) return;
                    skinLoader.Dispose();
                    skinLoader = null;
                    skinDone = true;

                    SongList.StartScan(); // ここから先は裏で読むので、画面は止まらない
                    step = Step.Songs;
                    break;

                case Step.Songs:
                    if (SongList.IsScanning) return; // 入れ替えは Game が毎フレーム SongList.Update() でやっている
                    step = Step.Done;
                    break;

                case Step.Done:
                    if (shownProgress < 0.995f) return;   // 100% を見せてから
                    doneTimer += dt;
                    float wait = SongList.ErrorCount > 0 ? ErrorWait : DoneWait;
                    bool skip = Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter);
                    if (doneTimer >= wait || skip) GoNext();
                    break;
            }
        }

        public override void Exit()
        {
            skinLoader?.Dispose();
            skinLoader = null;
        }

        // 次の画面へ(叩くキーが決まっていなければ初回起動セットアップ)
        private void GoNext()
        {
            step = Step.Leave;

            // この画面が透けて消えて、下から次の画面が出てくる（幕は SceneManager.DrawFade が描き続ける。黒は挟まない）
            liftSkin = SkinLine();
            TakeSnapshot();
            liftTime = 0f;
            Scene next = ConfigManager.Key.NeedsSetup() ? new SetupScene() : new EntryScene();
            SceneManager.FromBoot(next, 0f);
        }

        private string SkinLine()
        {
            if (!skinDone) return skinStage;
            string name = SkinManager.CurrentName;
            return $"スキン：{(string.IsNullOrEmpty(name) ? "（スキンがありません）" : name)}";
        }

        // ===== 描画 =====

        public override void Draw()
        {
            // 本当の進み具合：スキンで半分、曲で半分
            float skin = skinDone ? 1f : skinTotal > 0 ? (float)skinLoaded / skinTotal : 0f;
            float songs = step >= Step.Done ? 1f
                : step == Step.Skin || SongList.ScanTotal <= 0 ? 0f
                : Math.Clamp((float)SongList.ScanDone / SongList.ScanTotal, 0f, 1f);
            float actual = skin * 0.5f + songs * 0.5f;
            shownProgress += (actual - shownProgress) * (1f - MathF.Exp(-10f * Raylib.GetFrameTime()));
            if (actual - shownProgress < 0.002f) shownProgress = actual;

            DrawCurtain(shownProgress, SkinLine(), () =>
            {
                // 曲の行（小さい字で）
                string songText = step == Step.Skin ? "曲：スキンのあとで読みます"
                    : SongList.ScanTotal < 0 ? "曲：曲ファイルを探しています…"
                    : $"曲：{(step >= Step.Done ? SongList.ScanTotal : Math.Min(SongList.ScanDone, SongList.ScanTotal))} / {SongList.ScanTotal}";
                Vector2 m = FontManager.Measure(songText, 26);
                FontManager.Draw(songText, Screen.Width / 2f - m.X / 2f, 780, 26, SubColor);

                // 読めなかった曲があれば、数を出す
                if (step >= Step.Done && SongList.ErrorCount > 0)
                {
                    string error = $"読めなかった曲：{SongList.ErrorCount}曲（選曲画面に赤い字で出ます）";
                    Vector2 em = FontManager.Measure(error, 30);
                    FontManager.Draw(error, Screen.Width / 2f - em.X / 2f, 850, 30, ErrorColor);
                    FontManager.Draw("Enter：次へ", Left, 1000, 26, SubColor);
                }
            });

            if (fadeInTime < FadeInSeconds)
            {
                fadeInTime += Math.Min(Raylib.GetFrameTime(), 1f / 30f);
                float k = Math.Clamp(fadeInTime / FadeInSeconds, 0f, 1f);
                float shown = 1f - MathF.Pow(1f - k, 3f);
                Raylib.DrawRectangle(0, 0, Screen.Width, Screen.Height, Raylib.ColorAlpha(Color.Black, 1f - shown));
            }
        }
    }
}
