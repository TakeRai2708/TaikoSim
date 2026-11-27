using System.Numerics;
using Raylib_cs;

using TakeTaikoSimulator.Graphics;
namespace TakeTaikoSimulator.UI
{
    // モード選択・選曲画面の「斜めに並んだ板」の動き(TaikøNauts の選曲画面を測った値)
    //   ・板は斜めに並ぶ(1つ下がるごとに右へ40px、下へ135px。選んでいる板の上下だけ120px空ける)
    //   ・カーソルは0.15秒で1つ動く(一定の速さ)。動いている間は板は小さいまま
    //   ・止まってから0.43秒たつと、選んでいる板が0.1秒で上下に開く
    //   ・決定すると、黄色い枠が点滅する(0.53秒)
    // 板と枠は、スキンの画像(Folder の Bar_Base.png など)があればそれで、無ければプログラムで描く
    public sealed class BarList
    {
        // スキンの画像のフォルダ(MenuSkin.ModeFolder・MenuSkin.SongFolder)
        public string Folder { get; }

        public BarList(string folder) => Folder = folder;

        // 絵の無い板のふちを、モード選択の A-Style の板（01.Entry/Bar/Bar/1.png）と同じグラデで描く
        public bool EntryRim { get; init; }

        // ===== 配置(1920×1080) =====
        public const float BarX = 495, BarY = 478;            // 選んでいる板の左上
        public const float BarWidth = 932, BarHeight = 124;
        public const float StepX = 40, StepY = 135;           // 1つ離れるごとにずらす量
        public const float SelGapX = 10, SelGapY = 120;       // 選んでいる板の上下だけ、すき間を空ける(開いたときの場所)
        public const float OpenTop = 385, OpenBottom = 695;   // 開いた板の上と下
        public const float FrameWidth = 20;                   // 開いた板のまわりの黄色い枠

        // ===== 時間 =====
        public const float MoveSeconds = 0.15f;               // カーソルが1つ動く時間
        public const float FrameDelay = 0.33f;                // 止まってから黄色い枠が出るまで(板が開く0.1秒前。TaikøNauts の動画を1/30秒ずつ見た)
        public const float ExpandDelay = 0.43f;               // 止まってから板が開き始めるまで
        public const float ExpandSeconds = 0.1f;              // 板が開ききるまで
        public const float BlinkEnd = 0.53f;                  // 決定してから、ここまで黄色い枠が点滅
        private const float BlinkCycle = 0.15f, BlinkOn = 0.11f;

        public static readonly Color InkColor = new Color(24, 24, 32, 255);       // 板の縁
        public static readonly Color CursorColor = new Color(253, 253, 0, 255);   // 黄色い枠(TaikøNauts の動画の色。こい黄色)
        private const float WhiteOnYellow = 0.565f;                                // 黄色の上に白をこれだけ重ねると、淡い黄色 (253,253,144) になる(決定の点滅の明るいとき)

        public float Shown { get; private set; }        // 表示上のカーソルの位置
        public float Expand { get; private set; }       // 選んでいる板の開き具合(0→1)
        public float DecideTime { get; private set; } = -1f; // 決定してからの時間(決定していないときは -1)
        private float idle;                              // カーソルが止まってからの時間

        public bool Deciding => DecideTime >= 0f;

        // 板が開き始めてからの時間(開いていないときは 0)
        public float OpenSeconds => Math.Max(0f, idle - ExpandDelay);

        // 黄色い枠が出ているか(止まってから0.33秒。板が開くより0.1秒早い)
        public bool FrameShown => idle >= frameDelay;

        // 黄色い枠が出てからの時間(出ていないときは 0)
        public float FrameSeconds => Math.Max(0f, idle - frameDelay);

        // 黄色い枠が出るまで(ふつうは FrameDelay。難易度から戻ったときだけ、板が開くのとほぼ同時)
        private float frameDelay = FrameDelay;

        // 難易度の板から戻ったとき（11/8 の TaikøNauts の録画 2026-11-08 03-14-18.mp4 の 135.48秒〜を1/60秒ずつ見た）
        //   戻ると、難易度の板は一瞬で消えて、曲の一覧（選んでいた曲の板は閉じた形）になる。フェードはなし
        //   → 0.43秒で黄色い枠 → 0.45秒から板が開く（ふつうにカーソルが止まったときより 0.1秒おそく、枠と開くのがほぼ同時）
        public const float BackFrameSeconds = 0.43f, BackExpandSeconds = 0.45f;

        // pos に置き直す(動きのアニメなし。板は閉じる)
        public void Reset(int pos)
        {
            Shown = pos;
            Collapse();
            DecideTime = -1f;
        }

        // 板を閉じて、止まってからの時間を数え直す
        public void Collapse()
        {
            idle = 0f;
            Expand = 0f;
            frameDelay = FrameDelay;
        }

        // 難易度の板から戻ったときの閉じ方(上の BackFrameSeconds・BackExpandSeconds で枠が出て開く)
        public void CollapseAfterDifficulty()
        {
            Collapse();
            idle = ExpandDelay - BackExpandSeconds;
            frameDelay = idle + BackFrameSeconds;
        }

        // 板を開いた状態にする(難易度の板から戻ったときなど)
        public void OpenNow()
        {
            idle = ExpandDelay;
            Expand = 1f;
            frameDelay = FrameDelay;
        }

        // 板は開いた形のまま、黄色い枠だけ 0.33秒(FrameDelay)あとに出す(ジャンルを決めて、曲の一覧に入れ替わったとき)
        public void OpenNowFrameLater()
        {
            idle = 0f;
            Expand = 1f;
            frameDelay = FrameDelay;
        }

        // 決定した(枠の点滅を始める。まだ開いていなかったら、すぐ開く)
        public void StartDecide()
        {
            DecideTime = 0f;
            idle = Math.Max(idle, ExpandDelay);
        }

        public void EndDecide() => DecideTime = -1f;

        // 毎フレーム。target は本当のカーソルの位置
        public void Update(float dt, int target)
        {
            if (Deciding) DecideTime += dt;

            float distance = target - Shown;
            if (distance != 0f)
            {
                // 1つなら0.15秒。何回も押してたまったときは、まとめて0.15秒で追いつく
                float speed = Math.Max(1f, Math.Abs(distance)) / MoveSeconds;
                float step = speed * dt;
                Shown = Math.Abs(distance) <= step ? target : Shown + Math.Sign(distance) * step;
                Collapse();
                return;
            }
            idle += dt;
            if (idle >= ExpandDelay) Expand = Math.Min(1f, Expand + dt / ExpandSeconds);
        }

        // 一覧の r 番目(選んでいる板が0。小数もOK)の板の場所
        public static Rectangle BarArea(float r)
        {
            float side = Math.Clamp(r, -1f, 1f);
            return new Rectangle(BarX + StepX * r + SelGapX * side, BarY + StepY * r + SelGapY * side, BarWidth, BarHeight);
        }

        // 選んでいる板の、開いたときの場所
        public Rectangle OpenArea(Rectangle area)
        {
            float e = MenuDraw.EaseOut(Expand);
            float top = area.Y + (OpenTop - BarY) * e;
            float bottom = area.Y + BarHeight + (OpenBottom - (BarY + BarHeight)) * e;
            return new Rectangle(area.X, top, area.Width, bottom - top);
        }

        // 開いた板のまわりの黄色い枠
        //   TaikøNauts の動画(10/29 の2本を1/60秒ずつ)：
        //   ・止まっている間は、こい黄色 (253,253,0)
        //   ・決定すると 0.53秒、明るい色(淡い黄色〜白)と黄色を行ったり来たりする → そのあと白
        //   (10/27 に「淡い黄色」にしたのは、「全○曲」の吹き出しの色をまちがえて測っていた)
        public void DrawFrame(Rectangle area, float alpha, string? selectedImage = null)
        {
            if (!FrameShown) return;
            bool decided = Deciding && DecideTime >= BlinkEnd;                       // 点滅が終わった → 白
            bool light = Deciding && !decided && DecideTime % BlinkCycle < BlinkOn;  // 点滅の明るいとき
            Color color = decided ? Color.White : light ? MenuDraw.Tint(CursorColor, WhiteOnYellow) : CursorColor;

            Rectangle frame = new Rectangle(area.X - FrameWidth, area.Y - FrameWidth, area.Width + FrameWidth * 2, area.Height + FrameWidth * 2);
            float a = alpha; // 枠は、出るときにいきなり出る(動画では1コマで出ていた)
            // TaikøNauts のスキン(Bar/Selected.png：縦に 黄色・黄色の光・白 の3段。1段 1008×210、塗った形は 18,22 から 972×166・角の丸み 約48px)
            //   形ぴったりで切り取る(前は 7 から切っていて、左右に 11px の透明が入り、横だけ枠が細くなっていた)
            //   点滅の明るいときは、黄色の上に白を 56.5% 重ねる(淡い黄色 (253,253,144) になる)
            Rectangle Row(int row) => new Rectangle(18, 22 + 210 * row, 972, 166);
            const int corner = 50; // 角の丸み(48px)より大きく切る(丸みが伸ばす場所に入って、ゆがまないように)
            string path = selectedImage ?? Folder + "Bar/Selected.png";
            // 光(2段目)：枠の外までぼやけて広がっているので、1段まるごと(1008×210)を、枠の形が合う場所へ置く
            if (!Deciding && SkinManager.GetTexture(path) != null)
            {
                Rectangle glow = new Rectangle(frame.X - 18, frame.Y - 22, frame.Width + 36, frame.Height + 44);
                MenuSkin.NPatchPart(path, new Rectangle(0, 210, 1008, 210), glow, 70, MenuSkin.White(a * MenuDraw.CursorPulse));
            }
            if (MenuSkin.NPatchPart(path, Row(decided ? 2 : 0), frame, corner, MenuSkin.White(a)))
            {
                if (light) MenuSkin.NPatchPart(path, Row(2), frame, corner, MenuSkin.White(a * WhiteOnYellow));
                return;
            }
            // Simple スキン：Bar_Cursor.png(黄色)と Bar_Cursor_Decide.png(白)
            if (MenuSkin.NPatch(Folder + (decided ? "Bar_Cursor_Decide.png" : "Bar_Cursor.png"), frame, 40, MenuSkin.White(a)))
            {
                if (light) MenuSkin.NPatch(Folder + "Bar_Cursor_Decide.png", frame, 40, MenuSkin.White(a * WhiteOnYellow));
                return;
            }
            SmoothShapes.Rounded(frame, MenuDraw.Roundness(frame, 30), 12, MenuDraw.Fade(color, a));
        }

        // 開いたジャンルの板の、中の明るい板(TaikøNauts の動画：ジャンル名の帯より下だけ。板の上から113px〜下から22px)
        //   枠が出ると、閉じた板の中の板はいったん消える。板が開くと、ジャンル名の帯の下から下へ広がる
        //   (板が閉じているときは高さが0より小さくなるので、描かない)
        public const float OpenPlateTop = 113, OpenPlateBottom = 22;

        public static Rectangle OpenPlateArea(Rectangle area) =>
            new Rectangle(area.X + 70, area.Y + OpenPlateTop, area.Width - 140, area.Height - OpenPlateTop - OpenPlateBottom);

        // 板の土台(縁 → 色 → 中の明るい板)を描いて、中の板の場所を返す。plain なら中の明るい板は描かない
        //   スキンに Bar_Base.png(白い土台。色をかける)と Bar_Overlay.png / Bar_Overlay_Plain.png(縁と中の板)があれば、それで描く
        //   plate を渡すと、中の明るい板をその場所に描く(開いたジャンルの板。高さが0以下なら描かない)
        public Rectangle DrawBody(Rectangle area, Color color, float alpha, bool plain, Rectangle? plate = null)
        {
            Rectangle inner = new Rectangle(area.X + 70, area.Y + 20, area.Width - 140, area.Height - 40);
            bool movedPlate = !plain && plate != null;
            string overlay = Folder + (plain || movedPlate ? "Bar_Overlay_Plain.png" : "Bar_Overlay.png");
            if (MenuSkin.Has(Folder + "Bar_Base.png", overlay))
            {
                DrawShadow(area, alpha);
                // ふちの内側に、板の色を少し暗くした帯を入れる(A-Style の板と同じ「黒いふち → 暗い色 → 明るい色」の3段)
                MenuSkin.NPatch(Folder + "Bar_Base.png", area, 30, MenuDraw.Fade(Raylib.ColorBrightness(color, RimDarken), alpha));
                MenuSkin.NPatch(Folder + "Bar_Base.png", RimInset(area), 30, MenuDraw.Fade(color, alpha));
                MenuSkin.NPatch(overlay, area, 30, MenuSkin.White(alpha));
                // Bar_Overlay.png の中の板(70,20 から 792×84)だけを切り取って、plate に伸ばす
                if (movedPlate && plate!.Value.Height > 0)
                    MenuSkin.NPatchPart(Folder + "Bar_Overlay.png", new Rectangle(70, 20, 792, 84), plate.Value, 8, MenuSkin.White(alpha));
                // ハイライトは描かない（絵にもう描いてある。11/13 たけらい「ボックスの右側がまた黒い線」→ 絵と重ねていたのをやめた）
                return inner;
            }

            if (EntryRim) DrawEntryRimBody(area, color, alpha);
            else DrawCodeBody(area, color, alpha);
            Rectangle innerPlate = movedPlate ? plate!.Value : inner;
            if (!plain && innerPlate.Height > 0)
                SmoothShapes.Rounded(innerPlate, MenuDraw.Roundness(innerPlate, 8), 8, MenuDraw.Fade(MenuDraw.Tint(color, 0.6f), alpha));
            return inner;
        }

        // ===== 板のまわりの影と、ふちの内側の暗い帯(スキンの板の絵を使わないとき＝シンプルスキン) =====
        //   11/12 たけらい「(A-Style の板の)ふちのグローがシンプルスキンには無いから、すごい違和感」
        //   A-Style の板は「やわらかい影 → 黒いふち → 板の色を暗くした帯 → 明るい色」になっている。
        //   シンプルスキンは 黒いふち → 色 だけだったので、影と暗い帯を足した。
        //   影は、うすい黒を少しずつ大きく重ねたもの(下に 5px ずらす)。ひかえめにしてある
        private const float RimDarken = -0.28f, RimWidth = 12f;

        // ===== 絵が無い板を、A-Style の板の絵（02.SongSelect/Bar/1.png 938×133）のまねで描く =====
        //   11/13 たけらい「もともと素材がないやつのボックスの glow が汚い。その輪郭も細い。まねして」
        //   絵を1pxずつ見た：黒いふち 7px → 明るい帯 4〜5px（上・左・右の上半分）→ 板の色。
        //   下と右の下半分だけ、明るい帯のかわりに少し暗い帯（4px）。角の丸みは外側で約30px
        private const float CodeInk = 7f, CodeBand = 4.5f, CodeOuterRadius = 30f;
        private static void DrawCodeBody(Rectangle area, Color color, float alpha)
        {
            SmoothShapes.Rounded(area, MenuDraw.Roundness(area, CodeOuterRadius), 12, MenuDraw.Fade(InkColor, alpha));
            Rectangle r = new Rectangle(area.X + CodeInk, area.Y + CodeInk, area.Width - CodeInk * 2, area.Height - CodeInk * 2);
            float rad = CodeOuterRadius - CodeInk;
            Color dark = Raylib.ColorBrightness(color, -0.22f);
            Color light = MenuDraw.Tint(color, 0.62f);
            // 暗い帯（いちばん下に全部ぬる）→ 明るい帯（下の帯ぶん短く）→ 右の下半分を暗くもどす → 板の色
            SmoothShapes.Rounded(r, MenuDraw.Roundness(r, rad), 12, MenuDraw.Fade(dark, alpha));
            Rectangle lightArea = new Rectangle(r.X, r.Y, r.Width, r.Height - CodeBand);
            SmoothShapes.Rounded(lightArea, MenuDraw.Roundness(lightArea, rad), 12, MenuDraw.Fade(light, alpha));
            // 右の下半分：右下の角まで同じ丸みで暗い帯をつなげる（前は四角で角の手前で切れて、黒い線に見えていた）
            float midY = r.Y + r.Height / 2f;
            Rectangle lowerRight = new Rectangle(r.X + r.Width / 2f, midY, r.Width / 2f, r.Y + r.Height - midY);
            SmoothShapes.Rounded(lowerRight, MenuDraw.Roundness(lowerRight, rad), 12, MenuDraw.Fade(dark, alpha));
            Rectangle body = new Rectangle(r.X + CodeBand, r.Y + CodeBand, r.Width - CodeBand * 2, r.Height - CodeBand * 2);
            SmoothShapes.Rounded(body, MenuDraw.Roundness(body, rad - CodeBand), 12, MenuDraw.Fade(color, alpha));
        }

        // ===== 絵が無い板（モード選択）を、A-Style の 01.Entry/Bar/Bar/1.png（段位道場の青い板）のまねで描く =====
        //   11/27 たけらい「（段位道場の板の写真）これをそのまま真似すればいいのに？」
        //   絵を1pxずつ見た（閉じた板 160px と、開いて伸びた板 1920×1080 のスクショの両方）：
        //     黒いふち → 色の帯（左右 6px・上 4px・下 6px）→ 板の色
        //     左の帯：上は白に近い → 上から 1/3 くらいで一気に水色っぽく → 下 1/4 は暗い色。開いても割合は同じ
        //     右の帯：上は少し明るい色 → 下の暗い色へまっすぐ
        //     上の帯は左の白と右の明るい色のあいだ、下の帯は暗い色
        //   色は板の色から作る（白に混ぜる割合・暗さは、青(60,100,182)と緑の板の絵で合わせた）
        private const float EntryBandSide = 6f, EntryBandTop = 4f, EntryBandBottom = 6f;
        private static readonly Vector3 EntryRimStops = new(0.12f, 0.35f, 0.75f);
        private static void DrawEntryRimBody(Rectangle area, Color color, float alpha)
        {
            SmoothShapes.Rounded(area, MenuDraw.Roundness(area, CodeOuterRadius), 12, MenuDraw.Fade(InkColor, alpha));
            Rectangle r = new Rectangle(area.X + CodeInk, area.Y + CodeInk, area.Width - CodeInk * 2, area.Height - CodeInk * 2);
            float rad = CodeOuterRadius - CodeInk;
            Color dark = RimDark(color);
            Color[] left = { Whiten(color, 0.92f), Whiten(color, 0.85f), Whiten(color, 0.2f), dark };
            SmoothShapes.RoundedGradient(r, rad, left, EntryRimStops, Whiten(color, 0.28f), dark, alpha);
            Rectangle body = new Rectangle(r.X + EntryBandSide, r.Y + EntryBandTop, r.Width - EntryBandSide * 2, r.Height - EntryBandTop - EntryBandBottom);
            SmoothShapes.Rounded(body, MenuDraw.Roundness(body, rad - EntryBandSide), 12, MenuDraw.Fade(color, alpha));
        }

        // 白に amount だけ近づける
        private static Color Whiten(Color c, float amount) => new Color(
            (byte)(c.R + (255 - c.R) * amount), (byte)(c.G + (255 - c.G) * amount), (byte)(c.B + (255 - c.B) * amount), (byte)255);

        // 下と右下の暗い帯の色：絵では板の色にかかわらず明るさが同じくらい（青も緑も 明るさ 85 前後）
        private static Color RimDark(Color c)
        {
            float lum = 0.3f * c.R + 0.59f * c.G + 0.11f * c.B;
            float k = Math.Clamp(85f / Math.Max(lum, 1f), 0.45f, 0.85f);
            float r = c.R * k, g = c.G * k, b = c.B * k;
            float gray = 0.3f * r + 0.59f * g + 0.11f * b;
            return new Color((byte)(r + (gray - r) * 0.15f), (byte)(g + (gray - g) * 0.15f), (byte)(b + (gray - b) * 0.15f), (byte)255);
        }

        private const float ShadowDrop = 5f;

        private static Rectangle RimInset(Rectangle area) =>
            new Rectangle(area.X + RimWidth, area.Y + RimWidth, area.Width - RimWidth * 2, area.Height - RimWidth * 2);

        // 影（シンプルスキンの板だけ）：うすい黒をなめらかな形で2枚だけ重ねる（前の5枚重ねは、段がついて汚く見えていた）
        //   絵の無い板（A-Style で素材が無いジャンルなど）は、となりの絵の板に影が無いので、影を描かない
        private static void DrawShadow(Rectangle area, float alpha)
        {
            if (alpha <= 0.01f) return;
            for (int i = 2; i >= 1; i--)
            {
                float e = 4f * i;
                Rectangle s = new Rectangle(area.X - e, area.Y - e + ShadowDrop, area.Width + e * 2, area.Height + e * 2);
                SmoothShapes.Rounded(s, MenuDraw.Roundness(s, CodeOuterRadius + e), 12, new Color(0, 0, 0, (int)(255 * 0.10f * alpha)));
            }
        }

        // 一覧に何個描くか(短い一覧のときは、同じ項目が2回出ないように)
        public static (int Before, int After) VisibleRange(int count)
        {
            int before = Math.Min(4, (count - 1) / 2);
            int after = Math.Min(4, count - 1 - before);
            return (before, after);
        }
    }
}
