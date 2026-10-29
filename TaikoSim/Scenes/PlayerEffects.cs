using System.Numerics;
using Raylib_cs;
using TaikoSim.Configs;
using TaikoSim.Graphics;
using TaikoSim.Charts;

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
    //   ・描く順番：レーン → 顔 → 音符 → 左パネル → 判定花火 → ミニ太鼓 → 飛ぶ音符 → ポップアップ
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
        private const string GoGoFireImage = Dir + "Lane/GoGoSoul.png";                   // ゴーゴー中の判定枠の炎(横に8コマ。1コマ 480×600)
        private const string MiniTaikoImage = Dir + "MiniTaiko/MiniTaiko.png";            // ミニ太鼓(縦に5コマ)
        private const string ComboNumberImage = Dir + "MiniTaiko/Combo_Number.png";       // コンボの数字(縦に 白・銀・金)
        private const string ComboTextImage = Dir + "MiniTaiko/Combo_MiniText.png";       // 「コンボ」
        private const string ComboSparkleImage = Dir + "MiniTaiko/Effect.png";             // コンボの数字のキラキラ(小さい星)
        private const string NoteFlushImage = Dir + "Effects/Note_Flush.png";
        private const string FireWorksImage = Dir + "Effects/FireWorks.png";

        // ===== 位置(1920×1080) =====
        private readonly float judgeX;                        // 判定枠の真ん中(TestPlayScene からもらう)
        private readonly float judgeY;

        // ===== 判定枠の顔（Hit_Effects.png は 810×195px） =====
        // 画像は横4等分ではない。各絵の透明でない領域を切り取る。
        // 画像の並び: 小・良（黄）/ 小・可（白）/ 大・良（黄）/ 大・可（白）
        // ここでの「良・可」は判定結果で、ドン・カッの違いではない。
        // CropX/Y: 切り取り範囲の左上、CropWidth/Height: 幅と高さ（元画像のpx）
        // OffsetX/Y: 判定枠中央からの補正（＋Xは右、＋Yは下、1920×1080基準）
        // Scale: 表示倍率（1f=切り取ったままの大きさ、0.9f=90％）
        // 小・良（黄色）
        private const float FaceSmallGoodCropX = 32f, FaceSmallGoodCropY = 30f;
        private const float FaceSmallGoodCropWidth = 137f, FaceSmallGoodCropHeight = 137f;
        private const float FaceSmallGoodOffsetX = 0f, FaceSmallGoodOffsetY = 0f, FaceSmallGoodScale = 1f;
        // 小・可（白色）
        private const float FaceSmallOkCropX = 227f, FaceSmallOkCropY = 30f;
        private const float FaceSmallOkCropWidth = 137f, FaceSmallOkCropHeight = 137f;
        private const float FaceSmallOkOffsetX = 0f, FaceSmallOkOffsetY = 0f, FaceSmallOkScale = 1f;
        // 大・良（黄色）
        private const float FaceBigGoodCropX = 396f, FaceBigGoodCropY = 3f;
        private const float FaceBigGoodCropWidth = 188f, FaceBigGoodCropHeight = 188f;
        private const float FaceBigGoodOffsetX = 0f, FaceBigGoodOffsetY = 0f, FaceBigGoodScale = 1f;
        // 大・可（白色）
        private const float FaceBigOkCropX = 593f, FaceBigOkCropY = 5f;
        private const float FaceBigOkCropWidth = 184f, FaceBigOkCropHeight = 184f;
        private const float FaceBigOkOffsetX = 0f, FaceBigOkOffsetY = 0f, FaceBigOkScale = 1f;

        private const float LaneX = 494, LaneY = 276;         // PlayScene のレーン背景と左上をそろえる
        private const float MiniTaikoX = 310, MiniTaikoY = 308; // 比較動画のミニ太鼓の左上

        // 2・3けたは調整済み。4けたは横だけ0.8倍、縦は元の高さで下端410px。
        private const float ComboX = 400;
        private const float ComboBaseBottomY = 410;
        private static readonly float[] ComboPadding = { 51, 52, 40 };
        private static readonly float[] ComboScale = { 1f, 1f, 0.8f };
        private const float ComboTextX = 405, ComboTextY = 448;
        private const int MinShownCombo = 10;                 // これ以上で出す(OpenTaiko の初期値)
        // コンボの数字のキラキラ(OpenTaiko の showComboEffect と同じ。TaikøNauts の動画でも50コンボの銀の数字から光っていた)
        //   0〜35 の36段(1段16ms)で1周。まんなか(0〜12段)→ 右(8〜22段)→ 左(17〜31段)の順に、少しずつ上がりながら光る
        private const int ComboSparkleFrom = 50;
        private const float ComboSparkleStep = 0.016f;
        private const int ComboSparkleSteps = 36;
        //   星の画像はそのままの色で描く(OpenTaiko と同じ。A-Style の Effect.png は「真ん中が白・十字の腕が黄色」の 30×30)
        //   銀・金に分かれて見えるのは数字の方(Combo_Number.png の2段目・3段目)。星は銀でも金でも同じ
        //   (10/27 に白くして色をかけたら、まわりのうすい光まで白くなってボヤけたので、やめた)

        // ゴーゴー開始の火花の位置(下の真ん中)と傾き。OpenTaiko の Game_Effect_GoGoSplash_X/Y
        private static readonly float[] SplashX = { 180, 450, 780, 1140, 1470, 1740 };
        private static readonly float[] SplashY = { 1110, 1095, 1080, 1080, 1095, 1110 };
        private static readonly float[] SplashAngle = { -16f, -8f, 0f, 0f, 8f, 16f }; // 度(OpenTaiko は ±0.279, ±0.140 ラジアン)

        // ===== 時間(OpenTaiko と同じ) =====
        // レーンの光：元の16%から13%に少し暗くする。時間は変更しない
        // ※ 13%はスクショ確認用の仮調整値。実機で比べて最終調整する
        private const float LaneFlashSeconds = 0.150f;
        private const float LaneFlashHoldSeconds = 0.080f;
        private const float LaneFlashStrength = 0.13f;
        // ゴーゴーのレーン：TaikoNauts の動画で測った値(加算合成で約50%の強さ)
        private const float GoGoLaneStrength = 0.5f;
        // 判定枠の中の顔：TaikoNauts の動画で測った値(150ms そのまま → 300ms で消える)
        private const float FaceSeconds = 0.300f;
        private const float FaceHoldSeconds = 0.150f;
        private const float ExplosionFrameSeconds = 0.025f; // 光の輪1コマ(25ms)
        // 大音符：TaikøNauts の動画(60fps)で測った値
        //   点々の輪は小さい音符よりゆっくり(1コマ40ms × 5コマ ＝ 約0.2秒)
        //   光の線は、点々の輪が終わるころ(叩いて0.17秒後)から出て、0.16秒かけて外に広がりながら消える
        private const float BigExplosionFrameSeconds = 0.040f;
        private const float BigRaysStart = 0.17f, BigRaysSeconds = 0.16f;
        private const float BigRaysScaleFrom = 0.85f, BigRaysScaleTo = 1.10f;
        // 光の線の濃さ：TaikøNauts は細く薄く見えたので、最大60%。だんだん濃くなって(前半)、だんだん消える(後半)
        private const float BigRaysMaxAlpha = 0.6f;
        private const float MiniTaikoStepSeconds = 0.020f; // ミニ太鼓の明るさが1段下がる時間(20ms。8段)
        private const int MiniTaikoMaxBright = 8;
        private const float ComboBumpStep = 0.012f;     // コンボの数字が伸び縮みする1段(12ms)
        private const float GoGoStepSeconds = 0.018f;   // ゴーゴーが始まってからの動き1段(18ms × 18段)
        // ゴーゴーの炎(TaikøNauts の「愛をこめて花束を」の動画を測った値)
        //   ・炎の玉の中心(1コマの中の 243,297)を判定枠の中心に合わせて、0.9倍で描く(玉の半径が約83px)
        //   ・加算合成(レーンの上で明るいオレンジになる。動画の色と合った)
        //   ・TaikøNauts はレーンの外(魂ゲージの上)を切っていたが、本家と同じく切らずに描く
        //   ・始まったときは大きく出て、小さくなる(OpenTaiko の倍率 × 18段)
        //   ・コマを進める速さは動画では測れなかったので、OpenTaiko と同じ 50ms
        private const float FireScale = 0.9f;
        private const float FireCenterX = 243f / 480f, FireCenterY = 297f / 600f; // 1コマの中の、炎の玉の中心(割合)
        private const float FireFrameSeconds = 0.05f;
        private static readonly float[] FirePop = { 0.8f, 1.2f, 1.7f, 2.5f, 2.3f, 2.2f, 2.0f, 1.8f, 1.7f, 1.6f, 1.6f, 1.5f, 1.5f, 1.4f, 1.3f, 1.2f, 1.1f, 1.0f };
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
        private int faceFrame = 0;    // 0:小・良 1:小・可 2:大・良 3:大・可

        // ミニ太鼓の明るさ(左縁・左面・右面・右縁。DrumPart と同じ並び)
        private readonly float[] drumLight = new float[4];

        private int combo = 0;
        private float comboBumpTime = 99f;
        private float comboSparkleTime = 0f;

        private bool goGo = false;
        private float goGoTime = 0f;    // ゴーゴーが始まってからの時間
        private float splashTime = -1f; // 火花が出てからの時間(-1 なら出ていない)

        // 魂に飛んでいく音符と、着いたときの演出：TaikøNauts の動画(60fps)で測った値
        //   (2回目の比べ：全体が1〜2コマ早かったので、少しずつ長くした)
        //   飛ぶ時間：叩いてから約0.5秒で魂に着く
        //   花びらの輪：着いた瞬間に白で出て、黄色(0.075秒)→オレンジ→赤(0.22秒)と色が変わりながら外に広がる。
        //               0.22秒から薄くなって、0.33秒で消える
        //   音符：白い光(Note_Flush)が0.17秒かけて重なって真っ白になり、そのあと0.15秒で消える
        private const float FlightSeconds = 0.50f;
        private const float SoulX = 1833f, SoulY = 246f, FlightHeight = 330f;
        private const float ArrivalSeconds = 0.33f;
        private const float SoulRingYellowAt = 0.075f, SoulRingRedAt = 0.22f;
        private const float SoulRingFadeStart = 0.22f, SoulRingFadeEnd = 0.33f;
        private const float SoulRingRadiusFrom = 0.775f, SoulRingRadiusTo = 1.09f;
        private const float SoulFlushPeak = 0.17f, SoulFlushEnd = 0.32f;
        private const float SoulDotScale = 0.775f;
        private struct FlyingNote
        {
            public float Age;
            public bool Don, Big;
            public int LastTrailTick;
        }
        private readonly List<FlyingNote> flyingNotes = new(128);
        private readonly List<FlyingNote> arrivals = new(128);
        private struct Firework
        {
            public Vector2 Position;
            public float Age;
        }
        private readonly List<Firework> fireworks = new(128);
        private sealed class RollHeat
        {
            public float Level, Hold;
        }
        private readonly Dictionary<Note, RollHeat> rollHeat = new();
        public event Action? SoulArrived;

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
            flyingNotes.Clear();
            arrivals.Clear();
            fireworks.Clear();
            rollHeat.Clear();
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

        // 通常音符と連打は1打ごと、風船は割れたときに呼ぶ。色・大きさをそのまま運ぶ。
        public void StartFlyingNote(bool don, bool big, float age = 0f)
        {
            FlyingNote note = new FlyingNote { Don = don, Big = big, Age = Math.Max(0f, age), LastTrailTick = (int)(age / 0.04f) };
            if (note.Age >= FlightSeconds)
            {
                note.Age -= FlightSeconds;
                AddArrival(note);
                return;
            }
            if (flyingNotes.Count >= 128) flyingNotes.RemoveAt(0);
            flyingNotes.Add(note);
        }

        public void OnRollHit(Note note)
        {
            if (!note.IsRoll) return;
            if (!rollHeat.TryGetValue(note, out RollHeat? heat))
            {
                heat = new RollHeat();
                rollHeat.Add(note, heat);
            }
            heat.Level = Math.Min(100f, heat.Level + 10f);
            heat.Hold = heat.Level >= 100f ? 1.5f : 0.15f;
        }

        public float GetRollHeat(Note note) => rollHeat.TryGetValue(note, out RollHeat? heat) ? heat.Level / 100f : 0f;

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
            UpdateFlyingNotes(dt);
            foreach (RollHeat heat in rollHeat.Values)
            {
                float decay = Math.Max(0f, dt - Math.Max(0f, heat.Hold));
                heat.Hold = Math.Max(0f, heat.Hold - dt);
                heat.Level = Math.Max(0f, heat.Level - decay * 100f);
            }
            if (redFlash >= 0f) redFlash = Advance(redFlash, dt, LaneFlashSeconds);
            if (blueFlash >= 0f) blueFlash = Advance(blueFlash, dt, LaneFlashSeconds);
            if (yellowFlash >= 0f) yellowFlash = Advance(yellowFlash, dt, LaneFlashSeconds);

            for (int i = explosions.Count - 1; i >= 0; i--)
            {
                Explosion e = explosions[i];
                e.Time += dt;
                float life = e.Big
                    ? Math.Max(ExplosionFrameCount() * BigExplosionFrameSeconds, BigRaysStart + BigRaysSeconds)
                    : ExplosionFrameCount() * ExplosionFrameSeconds;
                if (e.Time >= life) explosions.RemoveAt(i);
                else explosions[i] = e;
            }

            if (faceTime >= 0f) faceTime = Advance(faceTime, dt, FaceSeconds);

            for (int i = 0; i < drumLight.Length; i++)
            {
                if (drumLight[i] > 0f) drumLight[i] = Math.Max(0f, drumLight[i] - dt / MiniTaikoStepSeconds);
            }

            comboBumpTime += dt;
            comboSparkleTime = (comboSparkleTime + dt) % (ComboSparkleStep * ComboSparkleSteps);
            if (goGo) goGoTime += dt;

            if (splashTime >= 0f)
            {
                splashTime += dt;
                if (splashTime >= SplashColumns * SplashRows * SplashFrameSeconds) splashTime = -1f;
            }
        }

        private void UpdateFlyingNotes(float dt)
        {
            for (int i = fireworks.Count - 1; i >= 0; i--)
            {
                Firework effect = fireworks[i];
                effect.Age += dt;
                if (effect.Age >= 0.26f) fireworks.RemoveAt(i);
                else fireworks[i] = effect;
            }
            for (int i = arrivals.Count - 1; i >= 0; i--)
            {
                FlyingNote note = arrivals[i];
                note.Age += dt;
                if (note.Age >= ArrivalSeconds) arrivals.RemoveAt(i);
                else arrivals[i] = note;
            }
            for (int i = flyingNotes.Count - 1; i >= 0; i--)
            {
                FlyingNote note = flyingNotes[i];
                note.Age += dt;
                if (note.Big)
                {
                    // 大音符は軌道に花火を残す。フレーム数ではなく経過時間で発生させる。
                    int first = Math.Max(2, note.LastTrailTick + 1);
                    int last = (int)(Math.Min(note.Age, FlightSeconds) / 0.04f);
                    for (int tick = first; tick <= last; tick++)
                    {
                        float age = note.Age - tick * 0.04f;
                        if (age >= 0.26f) continue;
                        if (fireworks.Count >= 128) fireworks.RemoveAt(0);
                        fireworks.Add(new Firework { Position = FlightPosition(tick * 0.04f), Age = age });
                    }
                    note.LastTrailTick = last;
                }
                if (note.Age >= FlightSeconds)
                {
                    flyingNotes.RemoveAt(i);
                    note.Age -= FlightSeconds;
                    AddArrival(note);
                }
                else flyingNotes[i] = note;
            }
        }

        private void AddArrival(FlyingNote note)
        {
            if (note.Age >= ArrivalSeconds) return;
            if (arrivals.Count >= 128) arrivals.RemoveAt(0);
            arrivals.Add(note);
            SoulArrived?.Invoke();
        }

        private Vector2 FlightPosition(float age)
        {
            float progress = Math.Clamp(age / FlightSeconds, 0f, 1f);
            return new Vector2(judgeX + (SoulX - judgeX) * progress,
                judgeY + (SoulY - judgeY) * progress - MathF.Sin(progress * MathF.PI) * FlightHeight);
        }

        // 通常音符も連打も同じ軌道。魂の到着演出は飛翔が終わった瞬間に開始する。
        public void DrawFlyingNotes()
        {
            if (SkinManager.GetTexture(FireWorksImage) is Texture2D firework)
            {
                float size = firework.Height;
                int frames = Math.Max(1, (int)(firework.Width / size));
                Raylib.BeginBlendMode(BlendMode.Additive);
                foreach (Firework effect in fireworks)
                {
                    int frame = Math.Min(frames - 1, (int)(effect.Age / 0.02f));
                    DrawCentered(firework, new Rectangle(frame * size, 0, size, size), effect.Position, 1f, Color.White);
                }
                Raylib.EndBlendMode();
            }
            foreach (FlyingNote note in flyingNotes) DrawMovingNote(note, FlightPosition(note.Age));
            if (arrivals.Count == 0) return;
            // 輪と中央の光をフェードしても、キャンバス自体のAを下げない。
            Rlgl.SetBlendFactorsSeparate(Rlgl.SRC_ALPHA, Rlgl.ONE_MINUS_SRC_ALPHA,
                Rlgl.ONE, Rlgl.ONE_MINUS_SRC_ALPHA, Rlgl.FUNC_ADD, Rlgl.FUNC_ADD);
            Raylib.BeginBlendMode(BlendMode.CustomSeparate);
            foreach (FlyingNote note in arrivals)
            {
                Vector2 center = new Vector2(SoulX, SoulY);
                float ringProgress = Math.Clamp(note.Age / ArrivalSeconds, 0f, 1f);
                float ringAlpha = 1f - Math.Clamp((note.Age - SoulRingFadeStart)
                    / (SoulRingFadeEnd - SoulRingFadeStart), 0f, 1f);
                if (ringAlpha > 0f && SkinManager.GetSoulExplosionDot() is Texture2D dot)
                {
                    float radius = SkinManager.SoulExplosionRadius
                        * (SoulRingRadiusFrom + (SoulRingRadiusTo - SoulRingRadiusFrom) * ringProgress);
                    float rotation = (-22f + 64f * ringProgress) * MathF.PI / 180f;
                    // 色：白 → 黄色(青を減らす) → 赤(緑を減らす)
                    float blue = 1f - Math.Clamp(note.Age / SoulRingYellowAt, 0f, 1f);
                    float green = 1f - Math.Clamp((note.Age - SoulRingYellowAt) / (SoulRingRedAt - SoulRingYellowAt), 0f, 1f);
                    Color tint = new Color(255, (int)(255f * green), (int)(255f * blue), (int)(255f * ringAlpha));
                    Rectangle source = new Rectangle(0, 0, dot.Width, dot.Height);
                    for (int i = 0; i < SkinManager.SoulExplosionDotCount; i++)
                    {
                        float angle = -MathF.PI / 2f + rotation + MathF.Tau * i / SkinManager.SoulExplosionDotCount;
                        Vector2 position = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                        DrawCentered(dot, source, position, SoulDotScale, tint);
                    }
                }
                // 音符は真っ白になるまで描く(そのあとは白い光だけが消えていく)
                if (note.Age < SoulFlushPeak) DrawMovingNote(note, center);
                if (SkinManager.GetTexture(NoteFlushImage) is Texture2D flush)
                {
                    float size = flush.Height;
                    float alpha = note.Age < SoulFlushPeak
                        ? note.Age / SoulFlushPeak
                        : 1f - (note.Age - SoulFlushPeak) / (SoulFlushEnd - SoulFlushPeak);
                    DrawCentered(flush, new Rectangle(note.Big ? size : 0, 0, size, size), center, 1f,
                        Raylib.ColorAlpha(Color.White, Math.Clamp(alpha, 0f, 1f)));
                }
            }
            Raylib.EndBlendMode();
        }

        private static void DrawMovingNote(FlyingNote note, Vector2 center)
        {
            string file = (note.Big ? note.Don ? "2.png" : "3.png" : note.Don ? "0.png" : "1.png");
            Texture2D? image = SkinManager.GetTexture(Dir + "Notes/Normal/1/" + file)
                ?? SkinManager.GetTexture(Dir + "Notes/Normal/0/" + file);
            if (image is Texture2D texture)
                DrawCentered(texture, new Rectangle(0, 0, texture.Width, texture.Height), center, 1f, Color.White);
            else Raylib.DrawCircleV(center, note.Big ? 72 : 48, note.Don ? Color.Red : Color.SkyBlue);
        }

        private static void DrawCentered(Texture2D texture, Rectangle source, Vector2 center, float scale, Color tint,
            float rotation = 0f)
        {
            float w = source.Width * scale, h = source.Height * scale;
            Raylib.DrawTexturePro(texture, source, new Rectangle(center.X, center.Y, w, h),
                new Vector2(w / 2f, h / 2f), rotation, tint);
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
            DrawGoGoFire(); // 魂ゲージより後に描くので、ゲージにかかるところも隠れない
            DrawLaneFlash(LaneRedImage, redFlash);
            DrawLaneFlash(LaneBlueImage, blueFlash);
            DrawLaneFlash(LaneYellowImage, yellowFlash);
            Raylib.EndBlendMode();
        }

        // 判定枠の顔だけを音符より先に描く。
        public void DrawBehindNotes() => DrawFace();

        // 音符と左パネルを描いた後で、花火全体を一度だけ描く。境界で切らない。
        public void DrawHitEffects()
        {
            Raylib.BeginBlendMode(BlendMode.Additive);
            foreach (Explosion e in explosions)
            {
                DrawExplosion(e);
            }
            Raylib.EndBlendMode();
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

        // ----- 判定枠の中の顔（良・可 × 小・大の4種類を個別に切り抜く） -----
        private void DrawFace()
        {
            if (faceTime < 0f || SkinManager.GetTexture(HitFaceImage) is not Texture2D texture) return;

            float fade = faceTime <= FaceHoldSeconds ? 1f
                : 1f - (faceTime - FaceHoldSeconds) / (FaceSeconds - FaceHoldSeconds);

            // 4種類の画像は横幅も高さも違うため、それぞれ専用の切り取り範囲を使う
            Rectangle source;
            float offsetX, offsetY, scale;
            switch (faceFrame)
            {
                case 0: // 小・良（黄色）
                    source = new Rectangle(FaceSmallGoodCropX, FaceSmallGoodCropY, FaceSmallGoodCropWidth, FaceSmallGoodCropHeight);
                    offsetX = FaceSmallGoodOffsetX;
                    offsetY = FaceSmallGoodOffsetY;
                    scale = FaceSmallGoodScale;
                    break;
                case 1: // 小・可（白色）
                    source = new Rectangle(FaceSmallOkCropX, FaceSmallOkCropY, FaceSmallOkCropWidth, FaceSmallOkCropHeight);
                    offsetX = FaceSmallOkOffsetX;
                    offsetY = FaceSmallOkOffsetY;
                    scale = FaceSmallOkScale;
                    break;
                case 2: // 大・良（黄色）
                    source = new Rectangle(FaceBigGoodCropX, FaceBigGoodCropY, FaceBigGoodCropWidth, FaceBigGoodCropHeight);
                    offsetX = FaceBigGoodOffsetX;
                    offsetY = FaceBigGoodOffsetY;
                    scale = FaceBigGoodScale;
                    break;
                case 3: // 大・可（白色）
                    source = new Rectangle(FaceBigOkCropX, FaceBigOkCropY, FaceBigOkCropWidth, FaceBigOkCropHeight);
                    offsetX = FaceBigOkOffsetX;
                    offsetY = FaceBigOkOffsetY;
                    scale = FaceBigOkScale;
                    break;
                default:
                    return;
            }

            // 切り取り幅・高さと表示倍率は正の数を指定する
            if (source.Width <= 0f || source.Height <= 0f || scale <= 0f) return;

            // 切り取った顔の絵そのものを判定枠の中央に置き、種類ごとの補正を足す
            float drawWidth = source.Width * scale;
            float drawHeight = source.Height * scale;
            Rectangle dest = new Rectangle(
                judgeX - drawWidth / 2f + offsetX,
                judgeY - drawHeight / 2f + offsetY,
                drawWidth, drawHeight);
            Raylib.DrawTexturePro(texture, source, dest, Vector2.Zero, 0f,
                Raylib.ColorAlpha(Color.White, Math.Clamp(fade, 0f, 1f)));
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
                int frame = (int)(e.Time / (e.Big ? BigExplosionFrameSeconds : ExplosionFrameSeconds));
                if (frame < frames)
                {
                    float size = texture.Height / 4f;
                    int row = (e.Big ? 2 : 0) + (e.Good ? 0 : 1);
                    Rectangle source = new Rectangle(frame * size, row * size, size, size);
                    Rectangle dest = new Rectangle(judgeX - size / 2f, judgeY - size / 2f, size, size);
                    Raylib.DrawTexturePro(texture, source, dest, Vector2.Zero, 0f, Color.White);
                }
            }

            // 大音符の光の線(縦に 良・可)。点々の輪が終わるころから、広がりながら消える
            if (e.Big && SkinManager.GetTexture(HitExplosionBigImage) is Texture2D big)
            {
                float t = (e.Time - BigRaysStart) / BigRaysSeconds; // 0 → 1
                if (t >= 0f && t < 1f)
                {
                    float scale = BigRaysScaleFrom + (BigRaysScaleTo - BigRaysScaleFrom) * t;
                    float alpha = BigRaysMaxAlpha * (t < 0.5f ? t / 0.5f : (1f - t) / 0.5f);
                    float frameHeight = big.Height / 2f;
                    Rectangle source = new Rectangle(0, e.Good ? 0 : frameHeight, big.Width, frameHeight);
                    float w = big.Width * scale, h = frameHeight * scale;
                    Rectangle dest = new Rectangle(judgeX - w / 2f, judgeY - h / 2f, w, h);
                    Raylib.DrawTexturePro(big, source, dest, Vector2.Zero, 0f,
                        Raylib.ColorAlpha(Color.White, Math.Clamp(alpha, 0f, 1f)));
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

            // けた数で、大きさ・間隔を変える(0:2けたまで 1:3けた 2:4けた以上)
            int size = count <= 2 ? 0 : count == 3 ? 1 : 2;
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
                float verticalScale = count >= 4 ? 1f : scale;
                float drawH = h * (verticalScale + bump); // 叩いたときは下端を固定して上に伸びる
                // 拡大率が変わっても、伸びていないときの数字の中心を2けた表示と同じ高さにする
                float bottomY = ComboBaseBottomY - h * (1f - verticalScale) / 2f;

                // 右の数字から順に(i = 0 が一の位)
                for (int i = 0; i < count; i++)
                {
                    int d = digits[count - 1 - i] - '0';
                    float centerX = rightX - padding * i;
                    Rectangle source = new Rectangle(d * w, row * h, w, h);
                    Rectangle dest = new Rectangle(centerX - drawW / 2f, bottomY - drawH, drawW, drawH);
                    Raylib.DrawTexturePro(numbers, source, dest, Vector2.Zero, 0f, Color.White);
                }
                if (combo >= ComboSparkleFrom) DrawComboSparkles(rightX, padding, count, w * scale, h * verticalScale, bottomY);
            }
            else
            {
                float width = FontManager.Measure(digits, 64).X;
                FontManager.Draw(digits, ComboX - width / 2f, ComboBaseBottomY - 72, 64, Color.White);
            }

            // 「コンボ」
            if (SkinManager.GetTexture(ComboTextImage) is Texture2D text)
            {
                Raylib.DrawTexture(text, (int)(ComboTextX - text.Width / 2f), (int)(ComboTextY - text.Height), Color.White);
            }
        }

        // コンボの数字のキラキラ(けたごとに、まんなか・右・左の3つ)
        //   OpenTaiko：b = 数字の幅 / 4、c = 数字の高さ / 4、d = 数字の下端。星は「下の真ん中」を合わせて描く
        private void DrawComboSparkles(float rightX, float padding, int count, float digitWidth, float digitHeight, float bottomY)
        {
            if (SkinManager.GetTexture(ComboSparkleImage) is not Texture2D star) return;
            Color tint = Color.White;
            int v = (int)(comboSparkleTime / ComboSparkleStep) % ComboSparkleSteps;
            float b = digitWidth / 4f, c = digitHeight / 4f, d = bottomY;
            for (int i = 0; i < count; i++)
            {
                float a = rightX - padding * i;
                if (v < 13) DrawSparkle(star, a, d - c - (int)(1.05f * v) - 13, tint);                  // まんなか
                if (v >= 8 && v < 23) DrawSparkle(star, a + b, d - c - (int)(1.05f * (v - 10)) - 3, tint); // 右
                if (v >= 17 && v < 32) DrawSparkle(star, a - b, d - c - (int)(1.05f * v - 20) - 8, tint);  // 左
            }
        }

        private static void DrawSparkle(Texture2D star, float x, float bottom, Color tint) =>
            Raylib.DrawTexture(star, (int)(x - star.Width / 2f), (int)(bottom - star.Height), tint);

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

        // ゴーゴー中の判定枠の炎(横に並んだコマを、くり返す)
        private void DrawGoGoFire()
        {
            if (!goGo || SkinManager.GetTexture(GoGoFireImage) is not Texture2D texture) return;

            // 1コマは 縦:横 = 5:4(480×600)。コマの数は画像の横幅から決める
            int frames = Math.Max(1, (int)MathF.Round(texture.Width / (texture.Height * 0.8f)));
            float w = texture.Width / (float)frames, h = texture.Height;
            int frame = (int)(goGoTime / FireFrameSeconds) % frames;
            float scale = FireScale * FirePop[GoGoStep()];

            Rectangle source = new Rectangle(frame * w, 0, w, h);
            Rectangle dest = new Rectangle(judgeX - w * FireCenterX * scale, judgeY - h * FireCenterY * scale, w * scale, h * scale);
            Raylib.DrawTexturePro(texture, source, dest, Vector2.Zero, 0f, Color.White);
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