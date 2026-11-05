using System.Numerics;
using Raylib_cs;
using TaikoSim.Audio;
using TaikoSim.Configs;
using TaikoSim.Graphics;
using static TaikoSim.UI.MenuDraw;

namespace TaikoSim.Scenes
{
    // 段位道場：課題曲と課題曲のあいだに、レーンを閉じるシャッター（扉）。
    //   本家（TaikøNauts）は曲が変わるとき演奏画面のまま、レーンだけ扉で閉じて、開くと次の曲名が出る。
    //   時間は OpenTaiko の Dan_Cert.cs と同じ：
    //     閉じる   1.0秒（はじめは速く、だんだんゆっくり。最後の0.15秒は一定の速さ）
    //     閉じたまま 2.3秒（0.8秒のところで次の曲に入れかえる）
    //     開く     0.27秒（sin で開く）
    //     曲名     開きはじめから 2.9秒。2.0秒から0.5秒でうすくなって消える
    //   1曲目は「閉じたまま」から始まる（OpenTaiko の Start(0) と同じ）。
    //   10/31 の九段の動画でも、1曲目の終わり → 約2秒で閉じる → 約3秒閉じたまま → 開いて曲名、を確かめた。
    //
    //   見た目（同じ動画を測った）：扉はレーンの x498〜1920・y288〜476 をおおう（下の灰色の帯「ドン・カッ」は出したまま）。
    //   左右2枚。シンプルは自作のふすま（木の縁・和紙・合わせ目のそばに丸い引き手）。
    //   A-Style の画像は使わない（他人のスキンなので）。コードで描く。
    public sealed class DanShutter
    {
        private enum Phase { Hidden, Closing, Closed, Opening }

        private const float CloseSeconds = 1.0f;
        private const float ClosedSeconds = 2.3f;
        private const float SwapAt = 0.8f;            // 閉じたままの何秒目で次の曲に入れかえるか
        private const float OpenSeconds = 0.27f;
        private const float TextSeconds = 2.9f;
        private const float TextFadeFrom = 2.0f;
        private const float TextFadeSeconds = 0.51f;
        // 扉が開ききってから、さらに曲の時間を止めておく秒数（固定）。
        //   11/5 の六段の動画（TaikøNauts）と TaikoSim を比べて、開ききってから最初の音符が来るまでが
        //   1曲目は 1.84秒、2曲目からは 3.13秒 本家の方が長かった。その分をそのまま足す。
        //   （OpenTaiko は「前の曲の #END から 6.2秒＋2.5秒」で次の曲。本家のゲーセンは段位ごとに計算しているらしいけど、
        //     たけらいと相談して固定にした）
        private const float FirstWaitSeconds = 1.84f, NextWaitSeconds = 3.13f;

        // 扉がおおう場所（測った値）
        private const float Left = 498f, Right = 1920f, Top = 288f, Bottom = 476f;
        private const float Seam = 6f;                 // 2枚のふすまの合わせ目（木の色）

        // 曲名の場所（測った値）
        private const float TitleCenterY = 388f, SubtitleCenterY = 447f;
        private const float TitleSize = 52f, SubtitleSize = 30f;

        private const string ScreenImage = "Image/08.DanGame/Screen.png";
        private const string InSound = "Sound/DanGame/ScreenIn.ogg";
        private const string OutSound = "Sound/DanGame/ScreenOut.ogg";

        private Phase phase = Phase.Hidden;
        private float elapsed;
        private float textElapsed = -1f;
        private bool swapped;
        private bool first;
        private float waitLeft;   // 開ききったあと、まだ時間を止めておく残り
        private string title = "", subtitle = "";
        private SoundEffect? inSound, outSound;

        /// <summary>閉じたまま0.8秒たったとき（ここで次の曲を読む）</summary>
        public event Action? Swap;

        /// <summary>扉が開ききったとき（ここから曲の時間を進める）</summary>
        public bool IsOpen => phase == Phase.Hidden;

        /// <summary>扉が少しでも出ているか（出ている間は、曲の終わりの処理を待つ）</summary>
        public bool IsBusy => phase != Phase.Hidden;

        /// <summary>曲の時間を止めておくか（閉じたまま・開いている途中。閉じていく間は前の曲の続きなので止めない）</summary>
        public bool HoldsTime => phase is Phase.Closed or Phase.Opening || waitLeft > 0f;

        public void LoadSounds()
        {
            string? skin = SkinManager.CurrentPath;
            if (skin == null) return;
            inSound ??= SoundEffect.Load(Path.Combine(skin, InSound), out _, 1);
            outSound ??= SoundEffect.Load(Path.Combine(skin, OutSound), out _, 1);
        }

        public void Unload()
        {
            inSound?.Dispose();
            inSound = null;
            outSound?.Dispose();
            outSound = null;
        }

        /// <summary>1曲目：閉じた状態から始めて、開くと曲名を出す</summary>
        public void StartFirst(string songTitle, string songSubtitle)
        {
            title = songTitle;
            subtitle = songSubtitle;
            phase = Phase.Closed;
            elapsed = 0f;
            swapped = true;   // 1曲目はもう読んであるので入れかえない
            textElapsed = -1f;
            first = true;
            waitLeft = 0f;
        }

        /// <summary>次の曲へ：扉を閉じる。閉じたまま0.8秒で Swap が呼ばれる</summary>
        public void StartNext(string songTitle, string songSubtitle)
        {
            title = songTitle;
            subtitle = songSubtitle;
            phase = Phase.Closing;
            elapsed = 0f;
            swapped = false;
            textElapsed = -1f;
            first = false;
            waitLeft = 0f;
            Play(inSound);
        }

        public void Update(float dt)
        {
            if (textElapsed >= 0f)
            {
                textElapsed += dt;
                if (textElapsed >= TextSeconds) textElapsed = -1f;
            }
            if (waitLeft > 0f) waitLeft = Math.Max(0f, waitLeft - dt);
            if (phase == Phase.Hidden) return;

            elapsed += dt;
            switch (phase)
            {
                case Phase.Closing:
                    if (elapsed >= CloseSeconds) { phase = Phase.Closed; elapsed = 0f; }
                    break;
                case Phase.Closed:
                    if (!swapped && elapsed >= SwapAt)
                    {
                        swapped = true;
                        Swap?.Invoke();
                    }
                    if (elapsed >= ClosedSeconds)
                    {
                        phase = Phase.Opening;
                        elapsed = 0f;
                        textElapsed = 0f;
                        Play(outSound);
                    }
                    break;
                case Phase.Opening:
                    if (elapsed >= OpenSeconds)
                    {
                        phase = Phase.Hidden;
                        waitLeft = first ? FirstWaitSeconds : NextWaitSeconds;
                    }
                    break;
            }
        }

        private static void Play(SoundEffect? sound)
        {
            if (sound == null) return;
            SoundConfig config = ConfigManager.Sound;
            sound.Play((float)config.GetFinalVolume(config.SeVolume));
        }

        // 扉の閉じ具合（0＝開いている、1＝閉じている）
        private float Closedness()
        {
            switch (phase)
            {
                case Phase.Closing:
                {
                    // OpenTaiko：1 - (1 - 1/180)^ms。最後の150msは残りを一定の速さで埋める
                    const double smooth = 1 - 1 / 180.0;
                    const int linear = 150;
                    double ms = elapsed * 1000.0;
                    int linearFrom = (int)(CloseSeconds * 1000) - linear;
                    double ratio = 1 - Math.Pow(smooth, Math.Min(linearFrom, ms));
                    if (ms > linearFrom) ratio += (1 - ratio) * (ms - linearFrom) / linear;
                    return (float)Math.Clamp(ratio, 0, 1);
                }
                case Phase.Closed: return 1f;
                case Phase.Opening:
                    // OpenTaiko：開いた量 = sin(ms/3 度) × 半分の幅
                    return 1f - MathF.Sin(Math.Clamp(elapsed / OpenSeconds, 0f, 1f) * MathF.PI / 2f);
                default: return 0f;
            }
        }

        /// <summary>レーンの上に、扉と曲名を描く</summary>
        public void Draw()
        {
            float closed = Closedness();
            if (closed > 0f && SkinManager.GetTexture(ScreenImage) is Texture2D screen)
            {
                // A-Style の扉（08.DanGame/Screen.png 1422×195）。閉じきると (498, 288) にぴったり（九段の動画で誤差4.5）。
                //   左半分は左から、右半分は右から出てくる（OpenTaiko の DanC_Screen と同じ描き方）
                float halfWidth = screen.Width / 2f;
                float shift = halfWidth * (1f - closed);
                Raylib.BeginScissorMode((int)Left, (int)Top, (int)(Right - Left), screen.Height);
                Raylib.DrawTexturePro(screen, new Rectangle(0, 0, halfWidth, screen.Height),
                    new Rectangle(Left - shift, Top, halfWidth, screen.Height), Vector2.Zero, 0f, Color.White);
                Raylib.DrawTexturePro(screen, new Rectangle(halfWidth, 0, halfWidth, screen.Height),
                    new Rectangle(Left + halfWidth + shift, Top, halfWidth, screen.Height), Vector2.Zero, 0f, Color.White);
                Raylib.EndScissorMode();
            }
            else if (closed > 0f)
            {
                float half = (Right - Left) / 2f;
                float doorWidth = half - Seam / 2f;
                // 左の扉は左はしから、右の扉は右はしから出てくる
                float leftEdge = Left - doorWidth + doorWidth * closed;          // 左の扉の左はし
                float rightEdge = Right - doorWidth * closed;                     // 右の扉の左はし
                // 合わせ目のすき間（木の色）は、閉じきったときだけ見える
                if (closed >= 0.999f)
                    Raylib.DrawRectangle((int)(Left + doorWidth), (int)Top, (int)Seam + 1, (int)(Bottom - Top), Wood);
                DrawDoor(leftEdge, doorWidth, knobOnRight: true);
                DrawDoor(rightEdge, doorWidth, knobOnRight: false);
            }

            if (textElapsed >= 0f) DrawTitle();
        }

        // ふすま（11/5 たけらい「どんちゃんみたいなのは入れなくていい。もう少しふすまっぽく」）
        //   木の縁（框）→ 和紙の面（上がほんの少し明るい）→ 横の細い桟の影 → 合わせ目のそばに丸い引き手
        private static readonly Color Wood = new(78, 52, 34, 255), WoodLight = new(112, 78, 50, 255);
        private static readonly Color Paper = new(240, 233, 216, 255), PaperShade = new(226, 216, 194, 255);

        private static void DrawDoor(float x, float width, bool knobOnRight)
        {
            float height = Bottom - Top;
            Rectangle door = new(x, Top, width, height);
            const float frame = 9f;
            Rectangle paper = new(x + frame, Top + frame, width - frame * 2f, height - frame * 2f);
            // レーンの外（左のパネル・画面の外）にはみ出したところは描かない。
            //   Raylib のはみ出し切りは入れ子にできないので、描くものごとに切り直す
            if (!Clip(door)) return;
            Raylib.DrawRectangleRec(door, Wood);                                                      // 木の縁
            Raylib.DrawRectangleLinesEx(new Rectangle(x + 2f, Top + 2f, width - 4f, height - 4f), 2f, WoodLight); // 縁のつや
            Raylib.DrawRectangleGradientV((int)paper.X, (int)paper.Y, (int)MathF.Ceiling(paper.Width), (int)paper.Height, Paper, PaperShade); // 和紙
            // 和紙の横のすじ（ごくうすく）と、上と下に細い桟
            Color fiber = new(214, 202, 178, 90);
            for (float y = paper.Y + 13f; y < paper.Y + paper.Height - 4f; y += 17f)
                Raylib.DrawRectangle((int)paper.X, (int)y, (int)MathF.Ceiling(paper.Width), 1, fiber);
            Color rail = new(196, 182, 156, 255);
            Raylib.DrawRectangle((int)paper.X, (int)(paper.Y + paper.Height * 0.2f), (int)MathF.Ceiling(paper.Width), 2, rail);
            Raylib.DrawRectangle((int)paper.X, (int)(paper.Y + paper.Height * 0.8f), (int)MathF.Ceiling(paper.Width), 2, rail);

            // 引き手（合わせ目のそば。金色の輪 → くぼみ）
            float knobX = knobOnRight ? paper.X + paper.Width - 34f : paper.X + 34f;
            Vector2 k = new(knobX, Top + height * 0.5f);
            Raylib.DrawCircleV(k, 17f, new Color(150, 118, 62, 255));
            Raylib.DrawCircleV(k, 14f, new Color(196, 162, 92, 255));
            Raylib.DrawCircleV(k, 10f, new Color(58, 40, 26, 255));
            Raylib.EndScissorMode();
        }

        // area と扉の場所の重なりだけ描けるようにする。重なりが無ければ false
        private static bool Clip(Rectangle area)
        {
            float x0 = Math.Max(area.X, Left), y0 = Math.Max(area.Y, Top);
            float x1 = Math.Min(area.X + area.Width, Right), y1 = Math.Min(area.Y + area.Height, Bottom);
            if (x1 - x0 < 1f || y1 - y0 < 1f) return false;
            Raylib.BeginScissorMode((int)x0, (int)y0, (int)MathF.Ceiling(x1 - x0), (int)MathF.Ceiling(y1 - y0));
            return true;
        }

        private void DrawTitle()
        {
            float alpha = textElapsed < TextFadeFrom ? 1f : 1f - (textElapsed - TextFadeFrom) / TextFadeSeconds;
            if (alpha <= 0f) return;
            Color fill = Fade(Color.White, alpha), edge = Fade(new Color(20, 20, 24, 255), alpha);
            float centerX = (Left + Right) / 2f;
            float width = Right - Left - 200f;
            if (subtitle.Length == 0)
            {
                Outlined(title, new Rectangle(centerX - width / 2f, (TitleCenterY + SubtitleCenterY) / 2f - 40f, width, 80f), TitleSize, fill, edge);
                return;
            }
            Outlined(title, new Rectangle(centerX - width / 2f, TitleCenterY - 40f, width, 80f), TitleSize, fill, edge);
            Outlined(subtitle, new Rectangle(centerX - width / 2f, SubtitleCenterY - 25f, width, 50f), SubtitleSize, fill, edge);
        }
    }

    // 段位道場：最後の曲（または打ち切り）の終わりに、レーンに出す「不合格」「合格」。
    //   10/31 の九段の動画：不合格のときは、レーンのまんなかに水色の大きい字で「不合格」（両側に小さいキャラ）。
    //   合格のときの見た目はまだ見ていない（推測で「合格」を赤・「金合格」を金の字にしている）。
    public sealed class DanEndBanner
    {
        private const float PopSeconds = 0.2f;
        private const float CenterX = 1209f, CenterY = 381f;
        private const string EndText = "Image/08.DanGame/End/Text.png";   // レーン(扉の場所)のまんなか

        private float elapsed = -1f;
        public Charts.DanResultType Result { get; private set; }
        public bool Started => elapsed >= 0f;
        public float Elapsed => Math.Max(0f, elapsed);

        public void Start(Charts.DanResultType result)
        {
            Result = result;
            elapsed = 0f;
        }

        public void Reset() => elapsed = -1f;

        public void Update(float dt)
        {
            if (elapsed >= 0f) elapsed += dt;
        }

        public void Draw()
        {
            if (elapsed < 0f) return;
            float t = Math.Clamp(elapsed / PopSeconds, 0f, 1f);
            float scale = 1.3f - 0.3f * EaseOut(t);

            // A-Style の字（08.DanGame/End/Text.png 800×150 が3つ：金の合格・赤の合格・不合格）。
            //   九段の動画で「不合格」は原寸で (809, 309)。推測なんだけど、1つめが金・2つめが赤
            if (SkinManager.GetTexture(EndText) is Texture2D image)
            {
                int row = Result switch { Charts.DanResultType.Gold => 0, Charts.DanResultType.Red => 1, _ => 2 };
                float w = 800f * scale, h = 150f * scale;
                Raylib.DrawTexturePro(image, new Rectangle(0, row * 150f, 800f, 150f),
                    new Rectangle(809f + 400f - w / 2f, 309f + 75f - h / 2f, w, h), Vector2.Zero, 0f, Fade(Color.White, t));
                return;
            }

            (string text, Color fill, Color edge) = Result switch
            {
                Charts.DanResultType.Gold => ("金合格", new Color(255, 214, 60, 255), new Color(90, 50, 0, 255)),
                Charts.DanResultType.Red => ("合格", new Color(255, 80, 60, 255), new Color(70, 10, 10, 255)),
                _ => ("不合格", new Color(110, 210, 255, 255), new Color(16, 40, 110, 255)),
            };
            // ポンッと出る（1.3倍 → 1倍）
            float size = 92f * scale;
            Outlined(text, new Rectangle(CenterX - 400f, CenterY - size, 800f, size * 2f), size, Fade(fill, t), Fade(edge, t));
        }
    }
}
