using System.Numerics;
using Raylib_cs;
using TaikoSim.Charts;
using TaikoSim.Graphics;
using TaikoSim.UI;
using static TaikoSim.UI.MenuDraw;

namespace TaikoSim.Scenes
{
    // 段位の「挑戦する」を決めてから演奏が始まるまでの、段位の読み込み画面（スキンの Image/07.DanLoading/）
    //
    // TaikøNauts の動画（11/5 の六段 2026-11-05_09-32-24.mp4 の 17.8〜23.6秒）を 1/60 秒ずつ測った値：
    //   ① 段位選択が 0.47秒で暗くなる → 0.6秒 真っ黒
    //   ② 道場の絵が 0.47秒で明るくなる → 3.2秒 見せる → 0.53秒で暗くなる
    //   ③ そのまま演奏画面が 0.5秒で明るくなる（扉は閉まったまま。ここから DanShutter が動く）
    //   ・絵（Background.png は 1920×1300）は、明るくなり始めてから 1秒に 73.5px ずつ上へ流れて、220px（下のはし）で止まる
    //   ・右上に段位のプレート（danPlatePath）。だいたい x1638〜1783・上が y162
    // 画像が無いスキン（シンプル）は、暗い背景に段位の名前とプレートだけ（オリジナルなので本家の絵はまねしない）
    public static class DanLoading
    {
        private const string BackgroundImage = "Image/07.DanLoading/Background.png";

        private const float FadeOutSeconds = 0.47f, BlackSeconds = 0.6f, InSeconds = 0.47f;
        private const float ShowSeconds = 3.2f, OutSeconds = 0.53f, GameFadeInSeconds = 0.5f;
        private const float ScrollSpeed = 73.5f, ScrollMax = 220f;
        private const float PlateX = 1636f, PlateY = 162f, PlateWidth = 150f, PlateHeight = 320f;
        private const float AfterLoadSeconds = 0.3f;   // 読み込みが長かったときも、読み終わってからこれだけは見せる

        private enum Phase { None, FadeOut, Black, In, Show, Out, GameIn }
        private static Phase phase = Phase.None;
        private static float timer;      // 今の段階に入ってからの時間（1回に進める時間はおさえる）
        private static float shown;      // 絵が明るくなり始めてからの時間（流れる絵に使う）
        private static float afterLoad;
        private static Scene? target;
        private static bool requested;
        private static DanCourse? course;
        private static Texture2D plate;

        /// <summary>前の画面・演奏画面を止めておく間（演奏画面が明るくなり始めたら動かす）</summary>
        public static bool IsBlocking => phase is Phase.FadeOut or Phase.Black or Phase.In or Phase.Show or Phase.Out;

        public static void Start(Scene scene, DanCourse danCourse)
        {
            target = scene;
            course = danCourse;
            requested = false;
            timer = shown = afterLoad = 0f;
            phase = Phase.FadeOut;
            MenuSkin.Preload("Image/07.DanLoading/", new[] { "Background.png" });
            FontManager.PreloadSkinMain(new[] { danCourse.Title, "段位道場" });
            LoadPlate(danCourse);
        }

        private static void LoadPlate(DanCourse danCourse)
        {
            if (plate.Id != 0) Raylib.UnloadTexture(plate);
            plate = default;
            plate = DanPlates.Load(danCourse);   // スキンのプレート → 無ければ段位フォルダ
        }

        public static void Update(float dt)
        {
            if (phase == Phase.None) return;
            float step = Math.Min(dt, 1f / 30f);   // 読み込みでフレームが長くなっても、アニメが飛ばないように
            timer += step;
            if (phase is Phase.In or Phase.Show or Phase.Out) shown += step;
            switch (phase)
            {
                case Phase.FadeOut:
                    if (timer >= FadeOutSeconds) Next(Phase.Black);
                    break;
                case Phase.Black:
                    if (timer >= BlackSeconds) Next(Phase.In);
                    break;
                case Phase.In:
                    if (timer >= InSeconds) { Next(Phase.Show); RequestChange(); }   // 明るくなりきってから読み込む
                    break;
                case Phase.Show:
                    if (SceneManager.Current != target) break;   // まだ読み込んでいない
                    afterLoad += dt;
                    if (timer >= ShowSeconds && afterLoad >= AfterLoadSeconds) Next(Phase.Out);
                    break;
                case Phase.Out:
                    if (timer >= OutSeconds) Next(Phase.GameIn);
                    break;
                case Phase.GameIn:
                    if (timer >= GameFadeInSeconds) Finish();
                    break;
            }
        }

        private static void Next(Phase p)
        {
            phase = p;
            timer = 0f;
        }

        private static void RequestChange()
        {
            if (requested || target == null) return;
            requested = true;
            SceneManager.ChangeScene(target);   // 次のフレームの最初に Enter（読み込み）が走る
        }

        private static void Finish()
        {
            phase = Phase.None;
            target = null;
            course = null;
            if (plate.Id != 0) Raylib.UnloadTexture(plate);
            plate = default;
        }

        // ===== 描く（SceneManager.DrawFade から。画面のいちばん上） =====
        public static void Draw()
        {
            switch (phase)
            {
                case Phase.FadeOut: Black(timer / FadeOutSeconds); break;
                case Phase.Black: Black(1f); break;
                case Phase.In:
                    DrawScreen();
                    Black(1f - timer / InSeconds);
                    break;
                case Phase.Show: DrawScreen(); break;
                case Phase.Out:
                    DrawScreen();
                    Black(timer / OutSeconds);
                    break;
                case Phase.GameIn: Black(1f - timer / GameFadeInSeconds); break;
            }
        }

        private static void Black(float amount)
        {
            amount = Math.Clamp(amount, 0f, 1f);
            if (amount > 0f) Raylib.DrawRectangle(0, 0, Screen.Width, Screen.Height, Raylib.ColorAlpha(Color.Black, amount));
        }

        private static void DrawScreen()
        {
            if (SkinManager.GetTexture(BackgroundImage) is Texture2D bg)
            {
                // 上へ流れる（下のはしまで来たら止まる）
                float scroll = Math.Min(Math.Min(ScrollSpeed * shown, ScrollMax), Math.Max(0f, bg.Height - 1080f));
                Raylib.DrawTexturePro(bg, new Rectangle(0, scroll, bg.Width, 1080f), new Rectangle(0, 0, 1920, 1080), Vector2.Zero, 0f, Color.White);
            }
            else DrawSimple();

            if (plate.Id != 0)
                Raylib.DrawTexturePro(plate, new Rectangle(0, 0, plate.Width, plate.Height),
                    DanPanel.FitPlate(plate, PlateX, PlateY, PlateWidth, PlateHeight), Vector2.Zero, 0f, Color.White);
        }

        // シンプル：暗い背景に、うすい横線と段位の名前だけ
        private static void DrawSimple()
        {
            using OutlineRatioScope outlineScope = UseOutlineRatio(DanOutlineRatio);   // 段位の画面は文字の縁を太く
            Raylib.DrawRectangleGradientV(0, 0, 1920, 1080, new Color(22, 24, 34, 255), new Color(46, 48, 58, 255));
            Raylib.DrawRectangle(0, 538, 1920, 2, new Color(255, 255, 255, 40));
            Centered("段位道場", new Rectangle(0, 400, 1920, 60), 40f, new Color(200, 200, 210, 255));
            if (course != null)
                Outlined(course.Title, new Rectangle(0, 560, 1920, 120), 96f, Color.White, new Color(10, 10, 14, 255));
        }
    }
}
