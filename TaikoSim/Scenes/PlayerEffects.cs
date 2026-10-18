using System.Numerics;
using Raylib_cs;
using TaikoSim.Configs;
using TaikoSim.Graphics;

namespace TaikoSim.Scenes
{
    // 演奏画面の演出(光る・ミニ太鼓・コンボ数・ゴーゴー)をまとめた係
    // 動きは OpenTaiko が基本。ただし同じスキン(A-Style)を使っている TaikoNauts の動画を 1/60秒ずつ測って、
    // 見た目が違うところ(光の強さ・顔のマーク・コンボの数字の大きさ)は TaikoNauts に合わせている
    // 位置は OpenTaiko の初期スキン(1280×720)の値を 1.5倍したもの(TaikoNauts の動画で測って直したところもある)
    //
    // 使い方(PlayScene から)
    //   ・毎フレーム Update(dt) と SetGoGo(ゴーゴー中か)
    //   ・叩いたら OnDrumHit、音符を良・可で叩けたら OnNoteHit、コンボが変わったら SetCombo
    //   ・描く順番：(レーン) → DrawLaneLayer → (判定枠) → DrawBehindNotes → (音符) → (左のパネル) → DrawPanel → DrawFront
    public class PlayEffects
    {
        // ===== 画像(スキンの Image/04.EnsoGame/) =====
        private const string Dir = "Image/04.EnsoGame/";
        private const string LaneRedImage = Dir + "Lane/Red.png";          // ドンを叩いたとき
        private const string LaneBlueImage = Dir + "Lane/Blue.png";        // カッを叩いたとき
        private const string LaneYellowImage = Dir + "Lane/Yellow.png";    // 音符を良・可で叩けたとき
        private const string LaneGoGoImage = Dir + "Lane/GoGo.png";        // ゴーゴー中のレーン
        private const string HitExplosionImage = Dir + "Effects/Hit_Explosion.png";       // 光の輪(横にコマ、縦に 良・可・大良・大可)
        private const string HitExplosionBigImage = Dir + "Effects/Hit_Explosion_Big.png"; // 大音符の光の線(縦に 良・可)
        private const string HitFaceImage = Dir + "Effects/Hit_Effects.png";             // 判定枠の中の顔(横に 良・可・大良・大可)
        private const string GoGoSplashImage = Dir + "Effects/GoGoSplash.png";            // ゴーゴー開始の火花(横10×縦3コマ)
        private const string MiniTaikoImage = Dir + "MiniTaiko/MiniTaiko.png";            // ミニ太鼓(縦に5コマ)
        private const string ComboNumberImage = Dir + "MiniTaiko/Combo_Number.png";       // コンボの数字(縦に 白・銀・金)
        private const string ComboTextImage = Dir + "MiniTaiko/Combo_MiniText.png";       // 「コンボ」

        // ===== 位置(1920×1080) =====
        private readonly float judgeX;                        // 判定枠の真ん中(TestPlayScene からもらう)
        private readonly float judgeY;
        private const float LaneX = 500, LaneY = 288;         // レーンの光の左上(OpenTaiko 333, 192)
        private const float MiniTaikoX = 307, MiniTaikoY = 309; // ミニ太鼓の左上(OpenTaiko 205, 206)

        // コンボの数字(下の真ん中を合わせる)
        //   2けた：OpenTaiko の Game_Taiko_Combo(267, 272)×1.5。TaikoNauts の動画とも合っていた
        //   3けた：TaikoNauts の動画で測った値(数字を 0.72倍に小さくして、間隔をつめて、少し上に出していた)
        //   4けた以上：測れていないので、3けたより少し小さくしたもの(推測)
        private const float ComboX = 400;
        private const float ComboY = 408;
        private static readonly float[] ComboBottom = { 408, 385, 385 };  // 2けたまで・3けた・4けた以上
        private static readonly float[] ComboPadding = { 51, 48, 40 };    // 数字の間隔
        private static readonly float[] ComboScale = { 1f, 0.72f, 0.6f }; // 数字の大きさ
        private const float ComboTextX = 402, ComboTextY = 447; // 「コンボ」の下の真ん中(OpenTaiko 268, 298)
        private const int MinShownCombo = 10;                 // これ以上で出す(OpenTaiko の初期値)

        // ゴーゴー開始の火花の位置(下の真ん中)と傾き。OpenTaiko の Game_Effect_GoGoSplash_X/Y
        private static readonly float[] SplashX = { 180, 450, 780, 1140, 1470, 1740 };
        private static readonly float[] SplashY = { 1110, 1095, 1080, 1080, 1095, 1110 };
        private static readonly float[] SplashAngle = { -16f, -8f, 0f, 0f, 8f, 16f }; // 度(OpenTaiko は ±0.279, ±0.140 ラジアン)

        // ===== 時間(OpenTaiko と同じ) =====
        // レーンの光：TaikoNauts の動画で測った値(加算合成で約16%の強さ、80ms そのまま → 150ms で消える)
        // ※ OpenTaiko は不透明で 100ms。そのままだと TaikoNauts よりずっと明るくなる
        private const float LaneFlashSeconds = 0.150f;
        private const float LaneFlashHoldSeconds = 0.080f;
        private const float LaneFlashStrength = 0.16f;
        // ゴーゴーのレーン：TaikoNauts の動画で測った値(加算合成で約50%の強さ)
        private const float GoGoLaneStrength = 0.5f;
        // 判定枠の中の顔：TaikoNauts の動画で測った値(150ms そのまま → 300ms で消える)
        private const float FaceSeconds = 0.300f;
        private const float FaceHoldSeconds = 0.150f;
        private const float ExplosionFrameSeconds = 0.025f; // 光の輪1コマ(25ms)
        private const float BigFrameSeconds = 0.020f;   // 大音符の光の線1コマ(20ms × 10)
        private const int BigFrames = 10;
        private const float MiniTaikoStepSeconds = 0.020f; // ミニ太鼓の明るさが1段下がる時間(20ms。8段)
        private const int MiniTaikoMaxBright = 8;
        private const float ComboBumpStep = 0.012f;     // コンボの数字が伸び縮みする1段(12ms)
        private const float GoGoStepSeconds = 0.018f;   // ゴーゴーが始まってからの動き1段(18ms × 18段)
        private const int GoGoSteps = 18;
        private const float SplashFrameSeconds = 0.018f; // 火花1コマ(18ms × 30コマ)
        private const int SplashColumns = 10, SplashRows = 3;

        // コンボが増えたときに数字を縦に伸ばす量(OpenTaiko の ComboScale)
        private static readonly float[] ComboBump = { 0.000f, 0.042f, 0.120f, 0.160f, 0.180f, 0.120f, 0.110f, 0.095f, 0.086f, 0.044f, 0.032f, 0.011f, 0.000f };

        // ===== 状態 =====
        private float redFlash = -1f, blueFlash = -1f, yellowFlash = -1f; // 光ってからの時間(-1 なら光っていない)

        // 光の輪(同時にいくつも出る)
        private struct Explosion
        {
            public float Time;  // 出てからの時間
            public bool Good;   // 良なら true、可なら false
            public bool Big;    // 大音符か
        }
        private readonly List<Explosion> explosions = new();

        // 判定枠の中の顔(最後に良・可で叩いたもの)
        private float faceTime = -1f; // 出てからの時間(-1 なら出ていない)
        private int faceFrame = 0;    // 0:良 1:可 2:大良 3:大可

        // ミニ太鼓の明るさ(左縁・左面・右面・右縁。DrumPart と同じ並び)
        private readonly float[] drumLight = new float[4];

        private int combo = 0;
        private float comboBumpTime = 99f;

        private bool goGo = false;
        private float goGoTime = 0f;    // ゴーゴーが始まってからの時間
        private float splashTime = -1f; // 火花が出てからの時間(-1 なら出ていない)

        public PlayEffects(float judgeX, float judgeY)
        {
            this.judgeX = judgeX;
            this.judgeY = judgeY;
        }

        // 最初から(クイックリスタート)
        public void Reset()
        {
            redFlash = blueFlash = yellowFlash = -1f;
            explosions.Clear();
            faceTime = -1f;
            Array.Clear(drumLight);
            combo = 0;
            comboBumpTime = 99f;
            goGo = false;
            goGoTime = 0f;
            splashTime = -1f;
        }

        // ===== 知らせてもらう =====

        // 叩いた(当たっても空振りでも)：ミニ太鼓と、レーンの赤・青の光
        public void OnDrumHit(DrumPart part)
        {
            drumLight[(int)part] = MiniTaikoMaxBright;
            if (KeyConfig.IsDon(part)) redFlash = 0f;
            else blueFlash = 0f;
        }

        // 音符を良・可で叩けた：光の輪・大音符の光の線・顔のマーク・レーンの黄色の光
        public void OnNoteHit(bool good, bool big)
        {
            explosions.Add(new Explosion { Time = 0f, Good = good, Big = big });
            if (explosions.Count > 12) explosions.RemoveAt(0); // OpenTaiko も同時に12個まで
            faceTime = 0f;
            faceFrame = (big ? 2 : 0) + (good ? 0 : 1);
            yellowFlash = 0f;
        }

        // コンボの数が変わった(増えたときだけ数字がぴょこっと伸びる)
        public void SetCombo(int value)
        {
            if (value > combo) comboBumpTime = 0f;
            combo = value;
        }

        // ゴーゴー中か(毎フレーム)。始まった瞬間に火花を出す
        public void SetGoGo(bool active)
        {
            if (active && !goGo)
            {
                goGoTime = 0f;
                splashTime = 0f;
            }
            goGo = active;
        }

        // ===== 毎フレーム =====

        public void Update(float dt)
        {
            if (redFlash >= 0f) redFlash = Advance(redFlash, dt, LaneFlashSeconds);
            if (blueFlash >= 0f) blueFlash = Advance(blueFlash, dt, LaneFlashSeconds);
            if (yellowFlash >= 0f) yellowFlash = Advance(yellowFlash, dt, LaneFlashSeconds);

            for (int i = explosions.Count - 1; i >= 0; i--)
            {
                Explosion e = explosions[i];
                e.Time += dt;
                float life = Math.Max(ExplosionFrameCount() * ExplosionFrameSeconds, e.Big ? BigFrames * BigFrameSeconds : 0f);
                if (e.Time >= life) explosions.RemoveAt(i);
                else explosions[i] = e;
            }

            if (faceTime >= 0f) faceTime = Advance(faceTime, dt, FaceSeconds);

            for (int i = 0; i < drumLight.Length; i++)
            {
                if (drumLight[i] > 0f) drumLight[i] = Math.Max(0f, drumLight[i] - dt / MiniTaikoStepSeconds);
            }

            comboBumpTime += dt;
            if (goGo) goGoTime += dt;

            if (splashTime >= 0f)
            {
                splashTime += dt;
                if (splashTime >= SplashColumns * SplashRows * SplashFrameSeconds) splashTime = -1f;
            }
        }

        // 時間を進めて、終わったら -1
        private static float Advance(float time, float dt, float length)
        {
            time += dt;
            return time >= length ? -1f : time;
        }

        // ===== 描く =====

        // レーンの上(判定枠より下)：ゴーゴーのレーン・レーンの光
        // ※ 判定枠より先に描くので、光っていても判定枠は見える
        public void DrawLaneLayer()
        {
            // 光は重ねると明るくなる(加算合成)。TaikoNauts の見え方もこれと同じだった
            Raylib.BeginBlendMode(BlendMode.Additive);
            DrawGoGoLane();
            DrawLaneFlash(LaneRedImage, redFlash);
            DrawLaneFlash(LaneBlueImage, blueFlash);
            DrawLaneFlash(LaneYellowImage, yellowFlash);
            Raylib.EndBlendMode();
        }

        // 判定枠より上・音符より下：光の輪・大音符の光の線・顔のマーク
        public void DrawBehindNotes()
        {
            // 光の輪は黒い背景の画像なので、加算合成で黒いところを消す(OpenTaiko の AddBlend と同じ)
            Raylib.BeginBlendMode(BlendMode.Additive);
            foreach (Explosion e in explosions)
            {
                DrawExplosion(e);
            }
            Raylib.EndBlendMode();

            DrawFace();
        }

        // 左のパネルの上：ミニ太鼓とコンボ数
        public void DrawPanel()
        {
            DrawMiniTaiko();
            DrawCombo();
        }

        // 一番上：ゴーゴー開始の火花
        public void DrawFront()
        {
            DrawGoGoSplash();
        }

        // ----- レーンの光(80ms そのまま、150ms で消える。加算合成で薄く) -----
        private void DrawLaneFlash(string image, float time)
        {
            if (time < 0f || SkinManager.GetTexture(image) is not Texture2D texture) return;

            float fade = time <= LaneFlashHoldSeconds ? 1f
                : 1f - (time - LaneFlashHoldSeconds) / (LaneFlashSeconds - LaneFlashHoldSeconds);
            float strength = LaneFlashStrength * Math.Clamp(fade, 0f, 1f);
            Raylib.DrawTexture(texture, (int)LaneX, (int)LaneY, Raylib.ColorAlpha(Color.White, strength));
        }

        // ----- 判定枠の中の顔(横に4コマ：良・可・大良・大可) -----
        private void DrawFace()
        {
            if (faceTime < 0f || SkinManager.GetTexture(HitFaceImage) is not Texture2D texture) return;

            float fade = faceTime <= FaceHoldSeconds ? 1f
                : 1f - (faceTime - FaceHoldSeconds) / (FaceSeconds - FaceHoldSeconds);
            float frameWidth = texture.Width / 4f;
            Rectangle source = new Rectangle(faceFrame * frameWidth, 0, frameWidth, texture.Height);
            Rectangle dest = new Rectangle(judgeX - frameWidth / 2f, judgeY - texture.Height / 2f, frameWidth, texture.Height);
            Raylib.DrawTexturePro(texture, source, dest, Vector2.Zero, 0f, Raylib.ColorAlpha(Color.White, Math.Clamp(fade, 0f, 1f)));
        }

        // ----- 光の輪 -----
        private int ExplosionFrameCount()
        {
            // 横のコマ数 = 横幅 ÷ 1コマの大きさ(1コマは正方形。縦に4段)
            if (SkinManager.GetTexture(HitExplosionImage) is not Texture2D texture) return 0;
            float size = texture.Height / 4f;
            return Math.Max(1, (int)(texture.Width / size));
        }

        private void DrawExplosion(Explosion e)
        {
            // 光の輪(縦に 良・可・大良・大可)
            if (SkinManager.GetTexture(HitExplosionImage) is Texture2D texture)
            {
                int frames = ExplosionFrameCount();
                int frame = (int)(e.Time / ExplosionFrameSeconds);
                if (frame < frames)
                {
                    float size = texture.Height / 4f;
                    int row = (e.Big ? 2 : 0) + (e.Good ? 0 : 1);
                    Rectangle source = new Rectangle(frame * size, row * size, size, size);
                    Rectangle dest = new Rectangle(judgeX - size / 2f, judgeY - size / 2f, size, size);
                    Raylib.DrawTexturePro(texture, source, dest, Vector2.Zero, 0f, Color.White);
                }
            }

            // 大音符の光の線(縦に 良・可。0.5倍 → 0.95倍に広がる)
            if (e.Big && SkinManager.GetTexture(HitExplosionBigImage) is Texture2D big)
            {
                int step = (int)(e.Time / BigFrameSeconds);
                if (step < BigFrames)
                {
                    float scale = 0.5f + step * 0.5f / 10f;
                    float frameHeight = big.Height / 2f;
                    Rectangle source = new Rectangle(0, e.Good ? 0 : frameHeight, big.Width, frameHeight);
                    float w = big.Width * scale, h = frameHeight * scale;
                    Rectangle dest = new Rectangle(judgeX - w / 2f, judgeY - h / 2f, w, h);
                    Raylib.DrawTexturePro(big, source, dest, Vector2.Zero, 0f, Color.White);
                }
            }
        }

        // ----- ミニ太鼓(縦に5コマ：太鼓・右面・左面・右縁・左縁) -----
        private void DrawMiniTaiko()
        {
            if (SkinManager.GetTexture(MiniTaikoImage) is not Texture2D texture) return;

            float frameHeight = texture.Height / 5f;
            DrawMiniTaikoFrame(texture, 0, frameHeight, 1f); // 太鼓

            // DrumPart(左縁・左面・右面・右縁) → コマの番号
            int[] frameOf = { 4, 2, 1, 3 };
            for (int part = 0; part < 4; part++)
            {
                if (drumLight[part] <= 0f) continue;
                // OpenTaiko：不透明度 = 明るさ × 73(8段 → 3段目までは真っ白、そこから消えていく)
                float alpha = Math.Clamp(MathF.Ceiling(drumLight[part]) * 73f / 255f, 0f, 1f);
                DrawMiniTaikoFrame(texture, frameOf[part], frameHeight, alpha);
            }
        }

        private static void DrawMiniTaikoFrame(Texture2D texture, int frame, float frameHeight, float alpha)
        {
            Rectangle source = new Rectangle(0, frame * frameHeight, texture.Width, frameHeight);
            Rectangle dest = new Rectangle(MiniTaikoX, MiniTaikoY, texture.Width, frameHeight);
            Raylib.DrawTexturePro(texture, source, dest, Vector2.Zero, 0f, Raylib.ColorAlpha(Color.White, alpha));
        }

        // ----- コンボ数(ミニ太鼓の中) -----
        private void DrawCombo()
        {
            if (combo < MinShownCombo) return;

            string digits = combo.ToString();
            int count = digits.Length;

            // 色：50 まで白、100 まで銀、それ以上は金(OpenTaiko は 50・100・300 で画像が変わる)
            int row = combo < 50 ? 0 : combo < 100 ? 1 : 2;

            // けた数で、大きさ・間隔・高さを変える(0:2けたまで 1:3けた 2:4けた以上)
            int size = count <= 2 ? 0 : count == 3 ? 1 : 2;
            float y = ComboBottom[size];
            float padding = ComboPadding[size];
            float scale = ComboScale[size];

            // 一番右の数字の位置(全体が ComboX を真ん中にして並ぶように)
            float rightX = ComboX + padding * (count - 1) / 2f;

            int bumpIndex = (int)(comboBumpTime / ComboBumpStep);
            float bump = bumpIndex < ComboBump.Length ? ComboBump[bumpIndex] : 0f;

            if (SkinManager.GetTexture(ComboNumberImage) is Texture2D numbers)
            {
                float w = numbers.Width / 10f;
                float h = numbers.Height / 3f;
                float drawW = w * scale;
                float drawH = h * (scale + bump); // 下はそのままで上に伸びる

                // 右の数字から順に(i = 0 が一の位)
                for (int i = 0; i < count; i++)
                {
                    int d = digits[count - 1 - i] - '0';
                    float centerX = rightX - padding * i;
                    Rectangle source = new Rectangle(d * w, row * h, w, h);
                    Rectangle dest = new Rectangle(centerX - drawW / 2f, y - drawH, drawW, drawH);
                    Raylib.DrawTexturePro(numbers, source, dest, Vector2.Zero, 0f, Color.White);
                }
            }
            else
            {
                float width = FontManager.Measure(digits, 64).X;
                FontManager.Draw(digits, ComboX - width / 2f, y - 72, 64, Color.White);
            }

            // 「コンボ」
            if (SkinManager.GetTexture(ComboTextImage) is Texture2D text)
            {
                Raylib.DrawTexture(text, (int)(ComboTextX - text.Width / 2f), (int)(ComboTextY - text.Height), Color.White);
            }
        }

        // ----- ゴーゴー -----

        // ゴーゴーが始まってから何段目か(0〜17)
        private int GoGoStep() => Math.Min(GoGoSteps - 1, (int)(goGoTime / GoGoStepSeconds));

        // レーンが赤〜ピンクに光る(始まった直後は、真ん中から上下に広がる。加算合成で50%の強さ)
        private void DrawGoGoLane()
        {
            if (!goGo || SkinManager.GetTexture(LaneGoGoImage) is not Texture2D texture) return;

            // OpenTaiko：段 0〜4 → 高さ0.2倍(下に54)、5 → 0.4倍(40)、6 → 0.6倍(26)、7〜8 → 0.8倍(13)、それ以降 → 1倍
            int step = GoGoStep();
            (float scaleY, float offset) = step switch
            {
                <= 4 => (0.2f, 81f),
                5 => (0.4f, 60f),
                6 => (0.6f, 39f),
                <= 8 => (0.8f, 19.5f),
                _ => (1f, 0f),
            };

            Rectangle source = new Rectangle(0, 0, texture.Width, texture.Height);
            Rectangle dest = new Rectangle(LaneX, LaneY + offset, texture.Width, texture.Height * scaleY);
            Raylib.DrawTexturePro(texture, source, dest, Vector2.Zero, 0f, Raylib.ColorAlpha(Color.White, GoGoLaneStrength));
        }

        // ゴーゴー開始の火花(画面の下から6か所、30コマ)
        private void DrawGoGoSplash()
        {
            if (splashTime < 0f || SkinManager.GetTexture(GoGoSplashImage) is not Texture2D texture) return;

            int frame = (int)(splashTime / SplashFrameSeconds);
            if (frame >= SplashColumns * SplashRows) return;

            float w = texture.Width / (float)SplashColumns;
            float h = texture.Height / (float)SplashRows;
            Rectangle source = new Rectangle(frame % SplashColumns * w, frame / SplashColumns * h, w, h);

            Raylib.BeginBlendMode(BlendMode.Additive);
            for (int i = 0; i < SplashX.Length; i++)
            {
                // 下の真ん中を合わせて、絵の真ん中を中心に傾ける
                Vector2 center = new Vector2(SplashX[i], SplashY[i] - h / 2f);
                Rectangle dest = new Rectangle(center.X, center.Y, w, h);
                Raylib.DrawTexturePro(texture, source, dest, new Vector2(w / 2f, h / 2f), SplashAngle[i], Color.White);
            }
            Raylib.EndBlendMode();
        }
    }
}