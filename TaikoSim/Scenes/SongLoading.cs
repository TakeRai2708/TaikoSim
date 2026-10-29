using System.Numerics;
using Raylib_cs;
using TaikoSim.Graphics;
using TaikoSim.UI;

namespace TaikoSim.Scenes
{
    // 難易度を決めてから演奏が始まるまでの「曲の読み込み画面」(スキンの Image/03.SongLoading/)
    //
    // TaikøNauts の動画(10/29 の A.mp4。82.4〜87.9秒)を 1/60 秒ずつ測った値：
    //   ① 決めてから 0.4秒は選曲画面のまま(押しても反応しない)
    //   ② 虹色の幕(Background_Fade.png を左右半分に分けたもの)が、両側から 0.48秒で閉じる(一定の速さ)
    //   ③ 閉じたら Background.png にかわる。そこから
    //        ・ヒントとキラキラ(Background_Light.png)…… 0.15秒で出てくる
    //        ・ドンとカツ(Chara.png の左半分・右半分)…… 外側の上から飛びこんでくる(0.3秒。横は一定の速さ、縦は落ちるように速くなる。0.25秒で見えきる)
    //        ・曲名の板(Plate.png)…… 0.02秒後から 0.28秒で、真ん中から横に広がる(一定の速さ。広がるのといっしょに濃くなる)
    //        ・曲名とサブタイトル …… 板が広がりきった瞬間に、いきなり出る
    //   ④ ここで曲を読み込む(読み込みが早く終わっても、幕が閉じてから 3.83秒は見せる)
    //   ⑤ 消える(0.29秒)
    //        ・曲名 …… 板がつぶれ始めると同時に消える
    //        ・曲名の板 …… 0.03秒後から 0.09秒で、上下につぶれて消える
    //        ・ヒントとキラキラ …… 0.03秒後から 0.12秒で消える
    //        ・ドンとカツ …… 0.28秒で、上へ飛んでいきながら消える(だんだん速く。消えるのは後半が速い)
    //   ⑥ 幕が両側へ 0.48秒で開いて(一定の速さ)、演奏画面になる。演奏画面は幕が開き始めたときから動く
    //
    // 画像が無いとき(SystemSkin など)は、プログラムで虹色の幕と白い板を描く(キャラは出さない)
    public static class SongLoading
    {
        public const string Folder = "Image/03.SongLoading/";
        private static readonly string[] Images = { "Background.png", "Background_Fade.png", "Background_Light.png", "Background_Light..png", "Chara.png", "Plate.png" };

        // ===== TaikøNauts の動画を測った秒数 =====
        private const float WaitSeconds = 0.40f;        // 決めてから幕が動き出すまで
        private const float CurtainSeconds = 0.48f;     // 幕が閉じる・開く時間(一定の速さ)
        private const float LightInSeconds = 0.15f;     // ヒントとキラキラが出てくる時間
        private const float CharaInSeconds = 0.30f;     // ドンとカツが飛びこむ時間
        private const float CharaFadeInSeconds = 0.25f; //   見えきるまで
        private const float CharaInX = 280f;            //   はじめは外側へ 280px
        private const float CharaInY = 125f;            //   はじめは上へ 125px
        private const float PlateInDelay = 0.02f;       // 板が広がり始めるまで
        private const float PlateInSeconds = 0.28f;     // 板が広がりきるまで(このとき曲名が出る)
        private const float ShowSeconds = 3.83f;        // 幕が閉じてから消え始めるまで(いちばん短いとき)
        private const float AfterLoadSeconds = 0.3f;    // 読み込みが長かったときも、読み終わってからこれだけは見せる
        private const float OutSeconds = 0.29f;         // 消える時間(このあと幕が開く)
        private const float PlateOutDelay = 0.03f, PlateOutSeconds = 0.09f;
        private const float LightOutDelay = 0.03f, LightOutSeconds = 0.12f;
        private const float CharaOutSeconds = 0.28f;
        private const float CharaOutY = 680f;           // 消えるまでに上へ 680px(動画：0.1秒で140px・0.2秒で420px)

        // ===== 置き場所(1920×1080。TaikøNauts の動画を測った) =====
        // 板：Plate.png(1600×270)を 0.9倍で、真ん中 (960, 400) に(画面では 240〜1680 × 278〜521)
        private const float PlateScale = 0.9f, PlateCenterY = 400f;
        private const float TitleCenterY = 390f, TitleSize = 62f, TitleOutline = 6f, TitleMaxWidth = 1300f;
        private const float SubtitleCenterY = 478f, SubtitleSize = 34f;
        private static readonly Color SubtitleColor = new Color(34, 34, 34, 255);

        private enum Phase { None, Wait, Close, In, Show, Out, Open }
        private static Phase phase = Phase.None;
        private static float timer;        // 今の段階に入ってからの時間(アニメ用。1回に進める時間をおさえる)
        private static float shownTime;    // 幕が閉じてからの本当の時間(読み込みの時間も入る)
        private static float afterLoad;    // 読み込みが終わってからの時間
        private static Scene? target;      // 演奏画面
        private static bool requested;     // 画面の切り替えを頼んだか
        private static string title = "", subtitle = "";

        // 選曲画面を操作させない・演奏画面を止めておく間(幕が開き始めたら、演奏画面は動く)
        public static bool IsBlocking => phase is Phase.Wait or Phase.Close or Phase.In or Phase.Show or Phase.Out;
        public static bool IsActive => phase != Phase.None;

        // 先に画像を読んでおく(選曲画面に入ったとき。決めた瞬間に止まらないように)
        public static void Preload() => MenuSkin.Preload(Folder, Images);

        public static void Start(Scene scene, string songTitle, string songSubtitle)
        {
            target = scene;
            title = songTitle;
            subtitle = songSubtitle;
            requested = false;
            timer = shownTime = afterLoad = 0f;
            phase = Phase.Wait;
            Preload();
            FontManager.PreloadSkinMain(new[] { title, subtitle }); // 曲名の字を先に作っておく(出た瞬間に止まらないように)
        }

        public static void Update(float dt)
        {
            if (phase == Phase.None) return;
            timer += Math.Min(dt, 1f / 30f); // 読み込みでフレームが長くなっても、アニメが飛ばないように
            switch (phase)
            {
                case Phase.Wait:
                    if (timer >= WaitSeconds) Next(Phase.Close);
                    break;
                case Phase.Close:
                    if (timer >= CurtainSeconds) Next(Phase.In);
                    break;
                case Phase.In:
                    shownTime += dt;
                    // 曲名まで出て止まった絵になってから読み込む(読み込み中に画面が止まっても、見た目が変わらないように)
                    if (timer >= PlateInDelay + PlateInSeconds + 1f / 60f) { Next(Phase.Show); RequestChange(); }
                    break;
                case Phase.Show:
                    shownTime += dt;
                    if (SceneManager.Current != target) break; // まだ読み込んでいない
                    afterLoad += dt;
                    if (shownTime >= ShowSeconds && afterLoad >= AfterLoadSeconds) Next(Phase.Out);
                    break;
                case Phase.Out:
                    if (timer >= OutSeconds) Next(Phase.Open);
                    break;
                case Phase.Open:
                    if (timer >= CurtainSeconds) { phase = Phase.None; target = null; }
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
            SceneManager.ChangeScene(target); // 次のフレームの最初に Enter(読み込み)が走る
        }

        // ===== 描く(Game が画面を描いたあと、SceneManager.DrawFade から) =====
        public static void Draw()
        {
            switch (phase)
            {
                case Phase.Close: DrawCurtain(timer / CurtainSeconds); break;
                case Phase.Open: DrawCurtain(1f - timer / CurtainSeconds); break;
                case Phase.In:
                case Phase.Show:
                case Phase.Out:
                    DrawScreen();
                    break;
            }
        }

        // 幕(closed：0 で開いている → 1 で閉じた)
        private static void DrawCurtain(float closed)
        {
            closed = Math.Clamp(closed, 0f, 1f);
            if (closed <= 0f) return;
            float shift = 960f * (1f - closed); // 左半分は左へ、右半分は右へずれている
            string fade = Folder + "Background_Fade.png";
            if (SkinManager.GetTexture(fade) is Texture2D tex)
            {
                float hw = tex.Width / 2f;
                Raylib.DrawTexturePro(tex, new Rectangle(0, 0, hw, tex.Height), new Rectangle(-shift, 0, 960, 1080), Vector2.Zero, 0f, Color.White);
                Raylib.DrawTexturePro(tex, new Rectangle(hw, 0, hw, tex.Height), new Rectangle(960 + shift, 0, 960, 1080), Vector2.Zero, 0f, Color.White);
                return;
            }
            // 画像が無いとき：8色のしま(片側4本ずつ)
            for (int i = 0; i < 8; i++)
            {
                float x = i * 240f + (i < 4 ? -shift : shift);
                Raylib.DrawRectangleRec(new Rectangle(x, 0, 240, 1080), FallbackStripe(i));
            }
        }

        private static Color FallbackStripe(int i) => i switch
        {
            0 => new Color(140, 214, 96, 255),
            1 => new Color(246, 216, 52, 255),
            2 => new Color(240, 150, 40, 255),
            3 => new Color(232, 70, 40, 255),
            4 => new Color(230, 70, 150, 255),
            5 => new Color(180, 90, 190, 255),
            6 => new Color(100, 110, 210, 255),
            _ => new Color(130, 220, 210, 255),
        };

        // 幕が閉じている間の絵
        private static void DrawScreen()
        {
            // 背景(閉じた幕)
            if (!MenuSkin.Whole(Folder + "Background.png", new Rectangle(0, 0, 1920, 1080), Color.White))
                DrawCurtain(1f);

            float tIn = phase == Phase.In ? timer : 99f;          // 出てくるアニメの時間(出きったら大きい数)
            float tOut = phase == Phase.Out ? timer : -1f;        // 消えるアニメの時間(まだなら -1)

            // ヒントとキラキラ
            float light = Math.Clamp(tIn / LightInSeconds, 0f, 1f);
            if (tOut >= 0f) light = 1f - Math.Clamp((tOut - LightOutDelay) / LightOutSeconds, 0f, 1f);
            if (light > 0f && !MenuSkin.Whole(Folder + "Background_Light.png", new Rectangle(0, 0, 1920, 1080), MenuSkin.White(light)))
                MenuSkin.Whole(Folder + "Background_Light..png", new Rectangle(0, 0, 1920, 1080), MenuSkin.White(light)); // TaikøNauts の名前(点が2つ)

            DrawChara(tIn, tOut);
            DrawPlate(tIn, tOut);
        }

        // ドンとカツ(Chara.png の左半分と右半分を、左右反対向きに動かす)
        private static void DrawChara(float tIn, float tOut)
        {
            if (SkinManager.GetTexture(Folder + "Chara.png") is not Texture2D tex) return;
            float dx, dy, alpha;
            if (tOut >= 0f)
            {
                float u = Math.Clamp(tOut / CharaOutSeconds, 0f, 1f);
                dx = 0f;
                dy = -CharaOutY * MathF.Pow(u, 1.6f); // だんだん速く
                alpha = 1f - MathF.Pow(u, 1.5f);      // はじめはゆっくり消える
            }
            else
            {
                float u = Math.Clamp(tIn / CharaInSeconds, 0f, 1f);
                dx = -CharaInX * (1f - u);               // 横は一定の速さ
                dy = -CharaInY * (1f - u * u);           // 縦は落ちるように、だんだん速く
                alpha = Math.Clamp(tIn / CharaFadeInSeconds, 0f, 1f);
            }
            if (alpha <= 0f) return;
            float hw = tex.Width / 2f;
            Color tint = MenuSkin.White(alpha);
            Raylib.DrawTexturePro(tex, new Rectangle(0, 0, hw, tex.Height), new Rectangle(dx, dy, 960, 1080), Vector2.Zero, 0f, tint);
            Raylib.DrawTexturePro(tex, new Rectangle(hw, 0, hw, tex.Height), new Rectangle(960 - dx, dy, 960, 1080), Vector2.Zero, 0f, tint);
        }

        // 曲名の板と曲名
        private static void DrawPlate(float tIn, float tOut)
        {
            float sx = Math.Clamp((tIn - PlateInDelay) / PlateInSeconds, 0f, 1f); // 横の広がり(＝濃さ)
            float sy = 1f, alpha = sx;
            bool text = sx >= 1f;
            if (tOut >= 0f)
            {
                float u = Math.Clamp((tOut - PlateOutDelay) / PlateOutSeconds, 0f, 1f);
                sy = MathF.Pow(1f - u, 1.7f); // はじめに速くつぶれる
                alpha = 1f - u;
                text = tOut < PlateOutDelay;
            }
            if (sx <= 0f || sy <= 0f || alpha <= 0f) return;

            Texture2D? tex = SkinManager.GetTexture(Folder + "Plate.png");
            float w = (tex?.Width ?? 1600) * PlateScale * sx;
            float h = (tex?.Height ?? 270) * PlateScale * sy;
            Rectangle dest = new Rectangle(960f - w / 2f, PlateCenterY - h / 2f, w, h);
            if (tex is Texture2D t)
                Raylib.DrawTexturePro(t, new Rectangle(0, 0, t.Width, t.Height), dest, Vector2.Zero, 0f, MenuSkin.White(alpha));
            else
                Raylib.DrawRectangleRec(dest, MenuDraw.Fade(new Color(248, 240, 244, 255), alpha * 0.8f));

            if (!text) return;
            // 曲名(白い字に黒い縁。長いときは縮める)
            float size = TitleSize;
            Vector2 m = FontManager.MeasureSkinMain(title, size);
            if (m.X > TitleMaxWidth) { size *= TitleMaxWidth / m.X; m = FontManager.MeasureSkinMain(title, size); }
            float outline = Math.Min(TitleOutline * size / TitleSize, DynamicFont.MaxOutline(size));
            FontManager.DrawSkinMainOutlined(title, 960f - m.X / 2f, TitleCenterY - m.Y / 2f, size, Color.White, Color.Black, outline);

            if (subtitle.Length == 0) return;
            float subSize = SubtitleSize;
            Vector2 sm = FontManager.MeasureSkinMain(subtitle, subSize);
            if (sm.X > TitleMaxWidth) { subSize *= TitleMaxWidth / sm.X; sm = FontManager.MeasureSkinMain(subtitle, subSize); }
            FontManager.DrawSkinMain(subtitle, 960f - sm.X / 2f, SubtitleCenterY - sm.Y / 2f, subSize, SubtitleColor);
        }
    }
}
