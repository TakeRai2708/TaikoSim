using System.Numerics;
using Raylib_cs;
using TaikoSim.Graphics;

namespace TaikoSim.Scenes
{
    // 演奏の結果(演奏後の演出の種類)
    //   Failed         … クリア失敗(魂ゲージがノルマに届かなかった)
    //   Clear          … クリア成功
    //   FullCombo      … フルコンボ(クリアして、不可・見逃しが0)
    //   DondaFullCombo … ドンダフルコンボ(クリアして、全部 良)
    public enum PlayResult { Failed, Clear, FullCombo, DondaFullCombo }

    // 演奏後の演出(レーンの上に出る「クリア成功」「ドンダフルコンボ!!」など)
    //   動き・位置は TaikøNauts の動画(1920×1080・60fps)を 30fps でコマ送りして測った値。
    //   表の数字は「動画の何コマ目か(30fps)」で書いてある。演出の始まり = 9.5コマ目。
    //   ドンダフルコンボ・クリア成功・クリア失敗は動画で測った。フルコンボだけ動画が無いので、クリア成功とドンダから作った仮の動き。
    public sealed class PlayEnd
    {
        private const string Dir = "Image/04.EnsoGame/End/";
        private const string TextImage = Dir + "Text.png";                         // 800×150 が縦2段(上:クリア失敗 下:クリア成功)
        private const string CharaImage = Dir + "Chara.png";                       // 200×250 が横6・縦3
        private const string FanImage = Dir + "Fan.png";                           // 350×220 が横2(左・右)・縦6(閉じている → 開いた)
        private const string FullComboImage = Dir + "FullCombo_Text.png";          // 800×200 が縦2段(上:色 下:真っ白)
        private const string DondaImage = Dir + "DondaFullCombo_Text.png";         // 1000×200 が縦2段(上:色 下:真っ白)
        private const string DondaBackImage = Dir + "DondaFullCombo_Background.png"; // 1422×240
        private const string ConfettiImage = Dir + "Confetti.png";                 // 520×368 が横10・縦5(50コマ。縦に読む)
        private const string StarImage = Dir + "Star.png";                         // 64×64

        // 演出が始まってから暗くなり始めるまでの時間(TaikøNauts の動画で 6.9秒。クリア失敗・クリア成功とも同じ)
        public const float Duration = 6.9f;

        // 動画のコマ(30fps)。演出の始まりは 9.5コマ目
        private const float Fps = 30f;
        private const float StartFrame = 9.5f;

        // ===== 位置(1920×1080) =====
        // 左右のキャラ・扇は、レーンの真ん中(x=1206)で左右対称。右の x = MirrorX - 左の x
        private const float CharaWidth = 200f, CharaHeight = 250f;
        private const float CharaMirrorX = 2212f;        // 2 × 1206 - 200
        private const float CharaY = 276f;                // キャラの画像の上端(ふだん)
        private const float TextX = 810f, TextY = 309f;   // 「クリア成功」「クリア失敗」の画像の左上
        private const float TextWidth = 800f, TextHeight = 150f;
        private const float DondaX = 702f, DondaY = 289f; // 「ドンダフルコンボ!!」の画像の左上
        private const float FullComboX = 804f, FullComboY = 288f; // 「フルコンボ!!」の画像の左上(文字の真ん中をドンダと合わせた。仮)
        private const float BigTextHeight = 200f;
        private const float BackX = 498f, BackY = 288f;   // ドンダフルコンボの背景(レーンの中を覆う)
        private const float FanLeftX = 556f, FanRightX = 1503f, FanY = 297f;
        private const float FanWidth = 350f, FanHeight = 220f;
        // 紙ふぶきのコマの左上(TaikøNauts の動画 10/29 A.mp4 を A-Style の画像と重ねて測った。右はレーンの真ん中 x=1206 で左右対称)
        private const float ConfettiLeftX = 560f, ConfettiRightX = 1332f, ConfettiY = 200f;
        private const float ConfettiWidth = 520f, ConfettiHeight = 368f;

        // ===== 動き(動画のコマ) =====
        // 出てきたキャラが左右に分かれるときの、左のキャラの x(14〜29コマ目)。右は左右対称
        private static readonly float[] SlideX = { 1061, 1019, 980, 943, 910, 879, 851, 827, 805, 786, 771, 757, 747, 740, 736, 735 };
        private const float AppearX = 1065f;   // 出てきたときの x(ふたりが真ん中で重なっている)

        // 踊り(30コマ目から、この順に1回)。(画像の列, 段, 左のキャラの x, y, 何コマ出すか)
        //   38コマ目からは、うちわを上げた絵(2列目・2段目)のまま止まる(クリア成功の動画で、暗くなるまでずっとこの絵だった)
        private static readonly (int Col, int Row, float X, float Y, int Frames)[] Dance =
        {
            (0, 1, 737, 276, 1), (0, 0, 717, 276, 3), (0, 2, 732, 236, 1), (2, 0, 732, 236, 2), (2, 1, 753, 276, 1),
        };
        private const float HoldX = 775f;   // 止まったときの左のキャラの x

        // クリア成功：40.5コマ目から文字が白く光る(45コマ目で真っ白、46.5〜51コマ目で戻る)
        private const float ClearFlashStart = 40.5f, ClearFlashRise = 4.5f, ClearFlashHold = 46.5f, ClearFlashFall = 4.5f;

        // ===== クリア失敗(動画で測った値。コマは上と同じ数え方) =====
        // 分かれるのが少し速い(13〜15コマ目)
        private static readonly float[] FailSlideX = { 1038, 988, 960 };
        // 16コマ目から、傾いた絵(4列目・0段目)で外側へ倒れながら持ち上がる(16〜30コマ目)。
        // 位置は「絵の真ん中」、角度は反時計回り(度)。右のキャラは左右対称で、角度は逆
        private const float FailTiltFrame = 16f;
        private static readonly float[] FailCenterX = { 983, 965, 948, 934, 922, 904, 893, 885, 880, 873, 870, 869, 869, 869, 869 };
        private static readonly float[] FailCenterY = { 402, 390, 380, 368, 361, 352, 345, 337, 334, 330, 328, 328, 334, 347, 367 };
        private static readonly float[] FailAngle = { 0, 6, 11, 15, 19, 22, 25, 27, 29, 30, 30, 30, 30, 30, 30 };
        private const float CenterMirrorX = 2412f;       // 2 × 1206(真ん中の x を左右対称にする)
        // 31コマ目に寝ている絵(4列目・1段目)になって、少し弾んで止まる
        private const float FailLandFrame = 31f;
        private const float FailLieX = 776f;
        private static readonly float[] FailLieY = { 262, 269, 267, 261, 258 };
        // 「クリア失敗」は左から出るのではなく、16.5〜27.5コマ目でだんだん濃くなる
        private const float FailTextStart = 16.5f, FailTextFrames = 11f;

        // ドンダフルコンボ：38〜46コマ目は少し沈む(y が増える)、47〜58コマ目は左上へ飛んでいく
        private static readonly float[] SinkY = { 0, 4, 8, 11, 13, 15, 15, 12, 4 };
        private static readonly float[] FlyX = { 763, 745, 728, 710, 693, 677, 660, 643, 627, 611, 596, 581 };
        private static readonly float[] FlyY = { 249, 212, 179, 150, 126, 106, 89, 78, 70, 66, 68, 71 };

        // ドンダフルコンボ：59コマ目に白く光って着物のキャラになり、68コマ目までに落ちてくる
        private static readonly float[] KimonoX = { 560, 550, 538, 524, 511, 499, 488, 478, 476, 475 };
        private static readonly float[] KimonoY = { 60, 80, 105, 132, 160, 187, 224, 259, 272, 276 };

        private const float TextWipeStart = 17.5f;  // 文字が左から順に出てくる(17.5〜25.5コマ目)
        private const float TextWipeFrames = 8f;
        private const float TextWipeSoft = 180f;    // ぼかしの幅(px)
        private const float TextGlyphLeft = 150f, TextGlyphRight = 647f; // 画像の中で文字がある範囲

        private const float BigTextFrame = 40.5f;   // フルコンボ・ドンダの文字が出る
        private const float WhiteFadeStart = 52.5f, WhiteFadeFrames = 6.5f; // 真っ白な文字が色の文字に変わる
        private const float BackFrame = 44f, BackFadeFrames = 6f;       // ドンダの背景
        private const float FanFrame = 42f;         // 扇が(閉じた形で)出て、44コマ目から1コマずつ開く
        private const float FanOpenFrame = 44f;
        private const float StarFrame = 45f;        // きらきら
        private const float ConfettiFrame = 67f;    // 紙ふぶき(大きい文字が出てから 0.85秒)
        private const float CharaLeaveFrame = 59f;  // ドンダ：飛んでいったキャラが着物のキャラに変わる

        public bool Started { get; private set; }
        public PlayResult Result { get; private set; }
        public float Elapsed { get; private set; }
        public bool IsFinished => Started && Elapsed >= Duration;

        // 今が動画の何コマ目か
        private float Frame => StartFrame + Elapsed * Fps;

        public void Reset()
        {
            Started = false;
            Elapsed = 0f;
        }

        public void Start(PlayResult result)
        {
            Started = true;
            Result = result;
            Elapsed = 0f;
        }

        public void Update(float dt)
        {
            if (Started) Elapsed += dt;
        }

        public void Draw()
        {
            if (!Started) return;
            float f = Frame;
            bool big = Result is PlayResult.FullCombo or PlayResult.DondaFullCombo;
            bool donda = Result == PlayResult.DondaFullCombo;

            if (donda) DrawImage(DondaBackImage, BackX, BackY, Fade(f, BackFrame, BackFadeFrames));
            // 紙ふぶきは、扇と文字の後ろ(TaikøNauts：扇より上に飛んだ紙だけが見える)
            if (donda && f >= ConfettiFrame) DrawConfetti(f);
            if (big) DrawFans(f);

            // 「クリア成功」「クリア失敗」(フルコンボ・ドンダのときは、大きい文字が出るときに消える)
            if (Result == PlayResult.Failed) DrawFailText(f);
            else
            {
                float textAlpha = big ? 1f - Fade(f, BigTextFrame - 0.5f, 2f) : 1f;
                if (textAlpha > 0f) DrawWipeText(1, f, textAlpha);
                if (Result == PlayResult.Clear) DrawClearFlash(f);
            }

            if (big) DrawBigText(donda ? DondaImage : FullComboImage, donda ? DondaX : FullComboX,
                donda ? DondaY : FullComboY, donda ? 1000f : 800f, f);
            if (Result != PlayResult.Failed && f >= StarFrame) DrawStars(f, big);

            DrawCharacters(f);
            if (donda && f >= CharaLeaveFrame) DrawKimono(f);
        }

        // ===== 中身 =====

        // start から frames コマで 0 → 1
        private static float Fade(float f, float start, float frames) => Math.Clamp((f - start) / frames, 0f, 1f);

        private static void DrawImage(string path, float x, float y, float alpha)
        {
            if (alpha <= 0f || SkinManager.GetTexture(path) is not Texture2D tex) return;
            Raylib.DrawTexture(tex, (int)x, (int)y, Raylib.ColorAlpha(Color.White, alpha));
        }

        private static void DrawCell(Texture2D tex, float sx, float sy, float w, float h, float x, float y, float alpha, bool flipX = false)
        {
            if (alpha <= 0f) return;
            Rectangle source = new Rectangle(sx, sy, flipX ? -w : w, h);
            Raylib.DrawTexturePro(tex, source, new Rectangle(x, y, w, h), Vector2.Zero, 0f, Raylib.ColorAlpha(Color.White, alpha));
        }

        // 文字を左から順に出す。細い縦の帯に分けて、右へ行くほど薄く描く
        private static void DrawWipeText(int row, float f, float alpha)
        {
            if (f < TextWipeStart || SkinManager.GetTexture(TextImage) is not Texture2D tex) return;
            float edge = TextGlyphLeft + (f - TextWipeStart) / TextWipeFrames * (TextGlyphRight - TextGlyphLeft + TextWipeSoft);
            const float strip = 8f;
            for (float x = 0; x < TextWidth; x += strip)
            {
                float a = Math.Clamp((edge - (x + strip / 2f)) / TextWipeSoft, 0f, 1f) * alpha;
                if (a <= 0f) break; // ここから右はまだ出ていない
                DrawCell(tex, x, row * TextHeight, strip, TextHeight, TextX + x, TextY, a);
            }
        }

        // 「クリア失敗」：その場でだんだん濃くなる
        private static void DrawFailText(float f)
        {
            if (SkinManager.GetTexture(TextImage) is not Texture2D tex) return;
            DrawCell(tex, 0, 0, TextWidth, TextHeight, TextX, TextY, Fade(f, FailTextStart, FailTextFrames));
        }

        // 「クリア成功」：踊り終わったところで、文字が一瞬白く光る
        private static void DrawClearFlash(float f)
        {
            float white;
            if (f < ClearFlashStart) return;
            if (f < ClearFlashStart + ClearFlashRise) white = (f - ClearFlashStart) / ClearFlashRise;
            else if (f < ClearFlashHold) white = 1f;
            else
            {
                float t = Math.Clamp((f - ClearFlashHold) / ClearFlashFall, 0f, 1f);
                white = (1f - t) * (1f - t); // 早く戻って、最後はゆっくり
            }
            if (white <= 0f || SkinManager.GetWhiteTexture(TextImage) is not Texture2D tex) return;
            DrawCell(tex, 0, TextHeight, TextWidth, TextHeight, TextX, TextY, white);
        }

        // 「フルコンボ!!」「ドンダフルコンボ!!」：真っ白な文字で出て、少しあとに色の文字に変わる
        private static void DrawBigText(string path, float x, float y, float width, float f)
        {
            if (SkinManager.GetTexture(path) is not Texture2D tex) return;
            float alpha = Fade(f, BigTextFrame, 2f);
            if (alpha <= 0f) return;
            DrawCell(tex, 0, 0, width, BigTextHeight, x, y, alpha);
            float white = 1f - Fade(f, WhiteFadeStart, WhiteFadeFrames);
            if (white > 0f) DrawCell(tex, 0, BigTextHeight, width, BigTextHeight, x, y, alpha * white);
        }

        private static void DrawFans(float f)
        {
            if (f < FanFrame || SkinManager.GetTexture(FanImage) is not Texture2D tex) return;
            int frame = Math.Clamp((int)(f - FanOpenFrame), 0, 5);
            float alpha = Fade(f, FanFrame, 2f);
            DrawCell(tex, 0, frame * FanHeight, FanWidth, FanHeight, FanLeftX, FanY, alpha);
            DrawCell(tex, FanWidth, frame * FanHeight, FanWidth, FanHeight, FanRightX, FanY, alpha);
        }

        // 紙ふぶき(50コマを30fpsでくり返す)。右は左右反転
        //   コマは「縦に読む」(0〜4 コマ目が1列目の上から下、5 コマ目から2列目)
        //   A-Style の画像で、上下のコマは形がつながっていて、左右のコマはつながっていなかった
        //   (前は横に読んでいたので、紙がバラバラに飛び回って、速く・多く見えていた)
        private static void DrawConfetti(float f)
        {
            if (SkinManager.GetTexture(ConfettiImage) is not Texture2D tex) return;
            int frame = (int)(f - ConfettiFrame) % 50;
            float sx = frame / 5 * ConfettiWidth, sy = frame % 5 * ConfettiHeight;
            float alpha = Fade(f, ConfettiFrame, 3f);
            DrawCell(tex, sx, sy, ConfettiWidth, ConfettiHeight, ConfettiLeftX, ConfettiY, alpha);
            DrawCell(tex, sx, sy, ConfettiWidth, ConfettiHeight, ConfettiRightX, ConfettiY, alpha, flipX: true);
        }

        // きらきら：ふくらんでしぼむ星を少しずつずらして出す(場所は決まった乱数)
        //   クリア成功は文字の下(動画で見えた範囲)、フルコンボ・ドンダはレーン全体(仮)
        private static void DrawStars(float f, bool wide)
        {
            if (SkinManager.GetTexture(StarImage) is not Texture2D tex) return;
            const int count = 10;
            const float period = 27f, life = 12f; // コマ
            for (int i = 0; i < count; i++)
            {
                float local = f - StarFrame - i * (period / count);
                if (local < 0f) continue;
                int cycle = (int)(local / period);
                float age = local - cycle * period;
                if (age >= life) continue;
                uint hash = (uint)(i * 73856093) ^ (uint)(cycle * 19349663);
                hash ^= hash >> 13; hash *= 0x5bd1e995u; hash ^= hash >> 15;
                float x = wide ? 620f + hash % 1170 : 900f + hash % 620;
                float y = wide ? 300f + (hash >> 12) % 210 : 440f + (hash >> 12) % 70;
                float scale = MathF.Sin(MathF.PI * age / life) * 1.1f;
                float size = 64f * scale;
                Raylib.DrawTexturePro(tex, new Rectangle(0, 0, 64, 64),
                    new Rectangle(x - size / 2f, y - size / 2f, size, size), Vector2.Zero, 0f, Color.White);
            }
        }

        // 左(紫)と右(緑)のキャラ
        private void DrawCharacters(float f)
        {
            if (SkinManager.GetTexture(CharaImage) is not Texture2D tex) return;
            if (Result == PlayResult.DondaFullCombo && f >= CharaLeaveFrame) return; // 着物のキャラに変わった

            float alpha = Fade(f, StartFrame, 4f);
            if (Result == PlayResult.Failed && f >= FailTiltFrame)
            {
                DrawFailCharacters(tex, f, alpha);
                return;
            }
            (int col, int row, float x, float y) = GetCharaPose(f);
            DrawCell(tex, col * CharaWidth, row * CharaHeight, CharaWidth, CharaHeight, x, y, alpha);
            DrawCell(tex, (col + 1) * CharaWidth, row * CharaHeight, CharaWidth, CharaHeight, CharaMirrorX - x, y, alpha);
        }

        // 左のキャラの絵(列・段)と位置。右のキャラは列+1、x は左右対称
        private (int Col, int Row, float X, float Y) GetCharaPose(float f)
        {
            if (Result == PlayResult.Failed && f >= 12.5f) return (0, 1, Table(FailSlideX, f - 13f), CharaY); // 速く分かれる
            if (f < 13.5f) return (0, 0, AppearX, CharaY);                    // 真ん中に出てくる
            if (f < 30f) return (0, 1, Table(SlideX, f - 14f), CharaY);        // 左右に分かれる

            if (Result == PlayResult.DondaFullCombo && f >= 38f)
            {
                if (f < 47f) return (2, 2, 775f, CharaY + Table(SinkY, f - 38f));
                return (2, 2, Table(FlyX, f - 47f), Table(FlyY, f - 47f));
            }

            // 踊って(1回)、うちわを上げた絵で止まる
            int step = (int)(f - 30f);
            foreach (var pose in Dance)
            {
                if (step < pose.Frames) return (pose.Col, pose.Row, pose.X, pose.Y);
                step -= pose.Frames;
            }
            return (2, 2, HoldX, CharaY);
        }

        // クリア失敗：傾いて外側へ倒れながら持ち上がって、落ちて寝る
        private static void DrawFailCharacters(Texture2D tex, float f, float alpha)
        {
            if (f >= FailLandFrame)
            {
                float y = Table(FailLieY, f - FailLandFrame);
                DrawCell(tex, 4 * CharaWidth, CharaHeight, CharaWidth, CharaHeight, FailLieX, y, alpha);
                DrawCell(tex, 5 * CharaWidth, CharaHeight, CharaWidth, CharaHeight, CharaMirrorX - FailLieX, y, alpha);
                return;
            }
            float i = f - FailTiltFrame;
            float cx = Table(FailCenterX, i), cy = Table(FailCenterY, i), angle = Table(FailAngle, i);
            Color color = Raylib.ColorAlpha(Color.White, alpha);
            Vector2 origin = new Vector2(CharaWidth / 2f, CharaHeight / 2f);
            // Raylib の角度は時計回りなので、反時計回りの角度はマイナスにする
            Raylib.DrawTexturePro(tex, new Rectangle(4 * CharaWidth, 0, CharaWidth, CharaHeight),
                new Rectangle(cx, cy, CharaWidth, CharaHeight), origin, -angle, color);
            Raylib.DrawTexturePro(tex, new Rectangle(5 * CharaWidth, 0, CharaWidth, CharaHeight),
                new Rectangle(CenterMirrorX - cx, cy, CharaWidth, CharaHeight), origin, angle, color);
        }

        // ドンダフルコンボ：白く光って出てきて、上から落ちてくる着物のキャラ
        private static void DrawKimono(float f)
        {
            if (SkinManager.GetTexture(CharaImage) is not Texture2D tex) return;
            float x = Table(KimonoX, f - CharaLeaveFrame);
            float y = Table(KimonoY, f - CharaLeaveFrame);
            const float sy = 2 * CharaHeight;
            DrawCell(tex, 4 * CharaWidth, sy, CharaWidth, CharaHeight, x, y, 1f);
            DrawCell(tex, 5 * CharaWidth, sy, CharaWidth, CharaHeight, CharaMirrorX - x, y, 1f);

            // 出てきた瞬間は真っ白(61コマ目から2コマで元の色へ)
            float white = 1f - Fade(f, CharaLeaveFrame + 2f, 2f);
            if (white > 0f && SkinManager.GetWhiteTexture(CharaImage) is Texture2D whiteTex)
            {
                DrawCell(whiteTex, 4 * CharaWidth, sy, CharaWidth, CharaHeight, x, y, white);
                DrawCell(whiteTex, 5 * CharaWidth, sy, CharaWidth, CharaHeight, CharaMirrorX - x, y, white);
            }
        }

        // 1コマごとの表を、コマの間はなめらかにつないで読む(表より先は最後の値)
        private static float Table(float[] table, float index)
        {
            if (index <= 0f) return table[0];
            if (index >= table.Length - 1) return table[^1];
            int i = (int)index;
            float t = index - i;
            return table[i] + (table[i + 1] - table[i]) * t;
        }
    }
}