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
    //   左右2枚。白地に灰色の模様、まんなかに大きいオレンジのどん顔、合わせ目に小さい取っ手。
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

        // 扉がおおう場所（測った値）
        private const float Left = 498f, Right = 1920f, Top = 288f, Bottom = 476f;
        private const float Seam = 22f;                // 2枚の扉の合わせ目のすき間

        // 曲名の場所（測った値）
        private const float TitleCenterY = 388f, SubtitleCenterY = 447f;
        private const float TitleSize = 52f, SubtitleSize = 30f;

        private const string InSound = "Sound/DanGame/ScreenIn.ogg";
        private const string OutSound = "Sound/DanGame/ScreenOut.ogg";

        private Phase phase = Phase.Hidden;
        private float elapsed;
        private float textElapsed = -1f;
        private bool swapped;
        private string title = "", subtitle = "";
        private SoundEffect? inSound, outSound;

        /// <summary>閉じたまま0.8秒たったとき（ここで次の曲を読む）</summary>
        public event Action? Swap;

        /// <summary>扉が開ききったとき（ここから曲の時間を進める）</summary>
        public bool IsOpen => phase == Phase.Hidden;

        /// <summary>扉が少しでも出ているか（出ている間は、曲の終わりの処理を待つ）</summary>
        public bool IsBusy => phase != Phase.Hidden;

        /// <summary>曲の時間を止めておくか（閉じたまま・開いている途中。閉じていく間は前の曲の続きなので止めない）</summary>
        public bool HoldsTime => phase is Phase.Closed or Phase.Opening;

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
            Play(inSound);
        }

        public void Update(float dt)
        {
            if (textElapsed >= 0f)
            {
                textElapsed += dt;
                if (textElapsed >= TextSeconds) textElapsed = -1f;
            }
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
                    if (elapsed >= OpenSeconds) phase = Phase.Hidden;
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
            if (closed > 0f)
            {
                float half = (Right - Left) / 2f;
                float doorWidth = half - Seam / 2f;
                // 左の扉は左はしから、右の扉は右はしから出てくる
                float leftEdge = Left - doorWidth + doorWidth * closed;          // 左の扉の左はし
                float rightEdge = Right - doorWidth * closed;                     // 右の扉の左はし
                // 合わせ目のすき間（濃い灰色）は、閉じきったときだけ見える
                if (closed >= 0.999f)
                    Raylib.DrawRectangle((int)(Left + doorWidth), (int)Top, (int)Seam + 1, (int)(Bottom - Top), new Color(70, 70, 74, 255));
                DrawDoor(leftEdge, doorWidth, knobOnRight: true);
                DrawDoor(rightEdge, doorWidth, knobOnRight: false);
            }

            if (textElapsed >= 0f) DrawTitle();
        }

        private static void DrawDoor(float x, float width, bool knobOnRight)
        {
            float height = Bottom - Top;
            Rectangle door = new(x, Top, width, height);
            Rectangle inner = new(x + 6f, Top + 6f, width - 12f, height - 12f);
            // レーンの外（左のパネル・画面の外）にはみ出したところは描かない。
            //   Raylib のはみ出し切りは入れ子にできないので、描くものごとに切り直す
            if (!Clip(door)) return;
            Raylib.DrawRectangleRec(door, new Color(92, 92, 96, 255));                       // 枠
            Raylib.DrawRectangleRec(inner, new Color(244, 244, 244, 255));                   // 白地
            float knobX = knobOnRight ? inner.X + inner.Width - 16f : inner.X + 16f;
            Raylib.DrawCircleV(new Vector2(knobX, Top + height * 0.48f), 11f, new Color(60, 60, 64, 255));  // 取っ手（合わせ目のそば）

            // 灰色の模様（ななめの線を格子に）
            if (!Clip(inner)) { Raylib.EndScissorMode(); return; }
            Color line = new(222, 222, 224, 255);
            for (float d = -height; d < width + height; d += 34f)
            {
                Raylib.DrawLineEx(new Vector2(inner.X + d, inner.Y), new Vector2(inner.X + d + height, inner.Y + height), 6f, line);
                Raylib.DrawLineEx(new Vector2(inner.X + d + height, inner.Y), new Vector2(inner.X + d, inner.Y + height), 6f, line);
            }

            // どん顔（オレンジの大きい輪 → 白い輪 → オレンジの顔）
            Color orange = new(214, 74, 18, 255);
            Vector2 c = new(inner.X + inner.Width * 0.52f, inner.Y + inner.Height * 0.62f);
            Raylib.DrawCircleV(c, 250f, orange);
            Raylib.DrawCircleV(c, 222f, Color.White);
            Raylib.DrawCircleV(c, 200f, orange);
            // 目
            Raylib.DrawCircleV(new Vector2(c.X - 90f, c.Y - 85f), 37f, Color.White);
            Raylib.DrawCircleV(new Vector2(c.X + 90f, c.Y - 85f), 37f, Color.White);
            // 口（ω）
            DrawMouthArc(new Vector2(c.X - 34f, c.Y - 18f));
            DrawMouthArc(new Vector2(c.X + 34f, c.Y - 18f));
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

        // 口の半分（下に丸い太い線）
        private static void DrawMouthArc(Vector2 center)
        {
            const float radius = 30f, thick = 13f;
            Vector2 prev = default;
            for (int i = 0; i <= 16; i++)
            {
                float a = MathF.PI * i / 16f;   // 0〜180度（下半分）
                Vector2 p = new(center.X + MathF.Cos(a) * radius, center.Y + MathF.Sin(a) * radius * 0.75f);
                if (i > 0) Raylib.DrawLineEx(prev, p, thick, Color.White);
                Raylib.DrawCircleV(p, thick / 2f, Color.White);
                prev = p;
            }
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
        private const float CenterX = 1209f, CenterY = 381f;   // レーン(扉の場所)のまんなか

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
            (string text, Color fill, Color edge) = Result switch
            {
                Charts.DanResultType.Gold => ("金合格", new Color(255, 214, 60, 255), new Color(90, 50, 0, 255)),
                Charts.DanResultType.Red => ("合格", new Color(255, 80, 60, 255), new Color(70, 10, 10, 255)),
                _ => ("不合格", new Color(110, 210, 255, 255), new Color(16, 40, 110, 255)),
            };
            // ポンッと出る（1.3倍 → 1倍）
            float t = Math.Clamp(elapsed / PopSeconds, 0f, 1f);
            float scale = 1.3f - 0.3f * EaseOut(t);
            float size = 92f * scale;
            Outlined(text, new Rectangle(CenterX - 400f, CenterY - size, 800f, size * 2f), size, Fade(fill, t), Fade(edge, t));
        }
    }
}
