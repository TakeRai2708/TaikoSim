using System.Numerics;
using Raylib_cs;

namespace TaikoSim.UI
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

        // ===== 配置(1920×1080) =====
        public const float BarX = 495, BarY = 478;            // 選んでいる板の左上
        public const float BarWidth = 932, BarHeight = 124;
        public const float StepX = 40, StepY = 135;           // 1つ離れるごとにずらす量
        public const float SelGapX = 10, SelGapY = 120;       // 選んでいる板の上下だけ、すき間を空ける(開いたときの場所)
        public const float OpenTop = 385, OpenBottom = 695;   // 開いた板の上と下
        public const float FrameWidth = 20;                   // 開いた板のまわりの黄色い枠

        // ===== 時間 =====
        public const float MoveSeconds = 0.15f;               // カーソルが1つ動く時間
        public const float ExpandDelay = 0.43f;               // 止まってから板が開き始めるまで
        public const float ExpandSeconds = 0.1f;              // 板が開ききるまで
        public const float BlinkEnd = 0.53f;                  // 決定してから、ここまで黄色い枠が点滅
        private const float BlinkCycle = 0.15f, BlinkOn = 0.11f;

        public static readonly Color InkColor = new Color(24, 24, 32, 255);       // 板の縁
        public static readonly Color CursorColor = new Color(253, 253, 144, 255); // 黄色い枠(TaikøNauts の動画の色。淡い黄色)
        private const float WhiteOnYellow = 0.565f;                                // 真っ黄色の上に白をこれだけ重ねると、上の淡い黄色になる

        public float Shown { get; private set; }        // 表示上のカーソルの位置
        public float Expand { get; private set; }       // 選んでいる板の開き具合(0→1)
        public float DecideTime { get; private set; } = -1f; // 決定してからの時間(決定していないときは -1)
        private float idle;                              // カーソルが止まってからの時間

        public bool Deciding => DecideTime >= 0f;

        // 板が開き始めてからの時間(開いていないときは 0)
        public float OpenSeconds => Math.Max(0f, idle - ExpandDelay);

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
        }

        // 板を開いた状態にする(難易度の板から戻ったときなど)
        public void OpenNow()
        {
            idle = ExpandDelay;
            Expand = 1f;
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

        // 開いた板のまわりの黄色い枠(決定したあとは点滅 → 白)
        public void DrawFrame(Rectangle area, float alpha)
        {
            if (Expand <= 0f) return;
            Color color = CursorColor;
            if (Deciding)
            {
                if (DecideTime < BlinkEnd)
                {
                    if (DecideTime % BlinkCycle >= BlinkOn) return;
                }
                else color = Color.White;
            }
            Rectangle frame = new Rectangle(area.X - FrameWidth, area.Y - FrameWidth, area.Width + FrameWidth * 2, area.Height + FrameWidth * 2);
            float a = alpha * Math.Min(1f, Expand * 2f);
            // TaikøNauts のスキン(Bar/Selected.png：縦に 黄色・黄色の光・白 の3段。1段 1008×210、塗った形は 18,22 から 972×166・角の丸み 約48px)
            //   形ぴったりで切り取る(前は 7 から切っていて、左右に 11px の透明が入り、横だけ枠が細くなっていた)
            //   TaikøNauts の動画では、枠は淡い黄色(253,253,144)だった → 黄色の上に白を 56% 重ねると同じ色になる
            bool decided = Deciding && DecideTime >= BlinkEnd;
            Rectangle Row(int row) => new Rectangle(18, 22 + 210 * row, 972, 166);
            const int corner = 50; // 角の丸み(48px)より大きく切る(丸みが伸ばす場所に入って、ゆがまないように)
            if (MenuSkin.NPatchPart(Folder + "Bar/Selected.png", Row(decided ? 2 : 0), frame, corner, MenuSkin.White(a)))
            {
                if (!decided) MenuSkin.NPatchPart(Folder + "Bar/Selected.png", Row(2), frame, corner, MenuSkin.White(a * WhiteOnYellow));
                return;
            }
            string image = Deciding && DecideTime >= BlinkEnd ? "Bar_Cursor_Decide.png" : "Bar_Cursor.png";
            if (MenuSkin.NPatch(Folder + image, frame, 40, MenuSkin.White(a))) return;
            Raylib.DrawRectangleRounded(frame, MenuDraw.Roundness(frame, 30), 12, MenuDraw.Fade(color, a));
        }

        // 板の土台(縁 → 色 → 中の明るい板)を描いて、中の板の場所を返す。plain なら中の明るい板は描かない
        //   スキンに Bar_Base.png(白い土台。色をかける)と Bar_Overlay.png / Bar_Overlay_Plain.png(縁と中の板)があれば、それで描く
        public Rectangle DrawBody(Rectangle area, Color color, float alpha, bool plain)
        {
            Rectangle inner = new Rectangle(area.X + 70, area.Y + 20, area.Width - 140, area.Height - 40);
            string overlay = Folder + (plain ? "Bar_Overlay_Plain.png" : "Bar_Overlay.png");
            if (MenuSkin.Has(Folder + "Bar_Base.png", overlay))
            {
                MenuSkin.NPatch(Folder + "Bar_Base.png", area, 30, MenuDraw.Fade(color, alpha));
                MenuSkin.NPatch(overlay, area, 30, MenuSkin.White(alpha));
                return inner;
            }

            Raylib.DrawRectangleRounded(area, MenuDraw.Roundness(area, 22), 12, MenuDraw.Fade(InkColor, alpha));
            Rectangle body = new Rectangle(area.X + 6, area.Y + 6, area.Width - 12, area.Height - 12);
            Raylib.DrawRectangleRounded(body, MenuDraw.Roundness(body, 17), 12, MenuDraw.Fade(color, alpha));
            if (!plain) Raylib.DrawRectangleRounded(inner, MenuDraw.Roundness(inner, 8), 8, MenuDraw.Fade(MenuDraw.Tint(color, 0.6f), alpha));
            return inner;
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
