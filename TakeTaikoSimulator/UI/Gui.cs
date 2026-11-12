using System.Numerics;
using Raylib_cs;
using TakeTaikoSimulator.Graphics;

namespace TakeTaikoSimulator.UI
{
    // 設定画面などで使うGUI部品
    // 「描画」と「クリック判定」を1つの関数でまとめてやる方式
    //
    // 動き(10/29)：shadcn/ui・reui.io・transitions.dev あたりの「今風の設定画面」をまねした
    //   ・ボタン・プルダウンは、マウスを乗せると色がじわっと変わる(約0.1秒)
    //   ・トグルの丸は、ON/OFF でスッとすべる
    //   ・スライダーは細いバー(6px)＋白い丸つまみ。さわるとつまみが大きくなって、上に数字の吹き出しが出る
    public static class Gui
    {
        // ===== 色 =====
        //   11/12 たけらい「色使いをもっとプロが作ったかのように。アイコンの色に合わせて、まるまる真似じゃなく似せて」
        //   アイコン(TakeTaiko の丸い絵)の色＝黒・深い赤・朱色・桜色・白っぽいピンク を、見やすいように落ち着かせた：
        //     背景やパネルは、ほぼ黒に赤みを少しだけ。朱色は「選んでいる物」「決定」だけ。桜色は見出しと光だけ
        //   前は 紺の背景＋青のアクセントだった
        public static readonly Color TextColor = new Color(252, 238, 242, 255);       // 文字
        public static readonly Color SubTextColor = new Color(191, 165, 174, 255);    // うすい文字
        public static readonly Color MutedTextColor = new Color(125, 103, 112, 255);  // もっとうすい文字(補足)
        public static readonly Color BoxColor = new Color(38, 23, 29, 255);           // 箱
        public static readonly Color BoxHoverColor = new Color(51, 32, 42, 255);      // マウスを乗せた箱
        public static readonly Color FillColor = new Color(226, 58, 94, 255);         // スライダーの塗り
        public static readonly Color AccentColor = new Color(226, 58, 94, 255);       // 朱色(選んでいる物・決定)
        public static readonly Color AccentDeepColor = new Color(163, 4, 27, 255);    // 押したとき
        public static readonly Color SakuraColor = new Color(249, 188, 206, 255);     // 桜色(見出し・光)
        public static readonly Color BackgroundColor = new Color(18, 10, 13, 255);    // 背景
        public static readonly Color PanelColor = new Color(27, 16, 20, 255);         // パネル
        public static readonly Color BorderColor = new Color(67, 42, 53, 255);        // 線
        public static readonly Color TrackColor = new Color(74, 50, 60, 255);         // スイッチ・スライダーの溝
        private static readonly Color PrimaryHoverColor = new Color(240, 96, 127, 255);
        private static readonly Color TrackHoverColor = new Color(92, 62, 74, 255);
        private static readonly Color BubbleTextColor = new Color(27, 16, 20, 255);
        private const float ControlRadius = 10;

        // ===== 光(glow) =====
        //   11/12 たけらい「いい感じに glow つけて。ボタンに合わせたら輪郭が光るとか」
        //   マウスを乗せたボタン・さわっているスライダーのつまみ・開いているプルダウンだけ、まわりが朱色にぼんやり光る。
        //   強さは GlowStrength の数字1つで変えられる(0 で光らない。0.6 がひかえめ、1 がふつう、1.6 がつよめ)
        public const float GlowStrength = 1.0f;
        private const int GlowLayers = 6;
        private const float GlowSpread = 14f;   // 光が広がる幅(px)

        // area のまわりに、うすい色を少しずつ大きく重ねて、ぼんやりした光にする(amount：0〜1。部品を描く前に呼ぶ)
        public static void Glow(Rectangle area, float radius, Color color, float amount)
        {
            float a = amount * GlowStrength;
            if (a <= 0.01f) return;
            for (int i = GlowLayers; i >= 1; i--)
            {
                float e = GlowSpread * i / GlowLayers;
                Rectangle r = new Rectangle(area.X - e, area.Y - e, area.Width + e * 2, area.Height + e * 2);
                SmoothShapes.Rectangle(r, radius + e, WithAlpha(color, 0.075f * a));
            }
        }

        // お知らせ（「このモードは1人プレイ専用です」など）。画面の上のまん中に出す
        //   11/12 たけらい「注意書きのポップアップUIも今のに合わせてほしい」
        //   前は画面ごとに、黒い帯に黄色い字（モード選択・段位は左はし、選曲はまん中）だった。
        //   設定画面と同じ見た目（パネルの色・線・朱色のしるし）にして、場所もまん中にそろえた
        public static void Notice(string text, float y = 140f)
        {
            const float size = 28f, height = 64f, pad = 34f;
            float width = FontManager.Measure(text, size).X + pad * 2 + 14f;
            Rectangle box = new Rectangle((1920f - width) / 2f, y, width, height);
            SmoothShapes.Rectangle(new Rectangle(box.X, box.Y + 6f, box.Width, box.Height), 16f, new Color(0, 0, 0, 90));   // 影
            Glow(box, 16f, AccentColor, 0.5f);
            SmoothShapes.Rectangle(box, 16f, PanelColor);
            SmoothShapes.Border(box, 16f, 1.5f, BorderColor);
            SmoothShapes.Rectangle(new Rectangle(box.X + 18f, box.Y + 18f, 6f, height - 36f), 3f, AccentColor);   // 左の朱色のしるし
            FontManager.Draw(text, box.X + pad + 14f, box.Y + (height - FontManager.Measure(text, size).Y) / 2f, size, TextColor);
        }

        // 丸のまわりの光(スライダーのつまみ)
        public static void GlowCircle(Vector2 center, float radius, Color color, float amount, float spread = GlowSpread * 0.8f)
        {
            float a = amount * GlowStrength;
            if (a <= 0.01f) return;
            for (int i = GlowLayers; i >= 1; i--)
                SmoothShapes.Circle(center, radius + spread * i / GlowLayers, WithAlpha(color, 0.085f * a));
        }

        // 押している間、ほんの少し(2%)小さくする
        private static Rectangle Pressed(Rectangle area, float press)
        {
            float k = 0.02f * press;
            return new Rectangle(area.X + area.Width * k / 2f, area.Y + area.Height * k / 2f, area.Width * (1f - k), area.Height * (1f - k));
        }

        // ===== 動き(じわっと変わる値) =====
        // 部品ごとに「今の値」を覚えておいて、毎フレーム目標の値に近づける
        //   名前(キー)で覚える。しばらく使われなかったものは捨てる(スクロールで場所が変わったボタンなど)
        private static readonly Dictionary<string, (float Value, int Frame)> anims = new();
        private static readonly List<string> staleAnims = new();
        private static int frameNo;
        private static float frameDt;

        // このフレームの経過時間(重いフレームでアニメが飛ばないように 1/30 秒までにする)
        public static float FrameDt => frameDt;

        // key の値を target に近づけて返す。speed が大きいほど速い(10 で約0.1秒、20 で約0.05秒で 2/3 まで近づく)
        //   はじめて使うキーは、いきなり target から始める(画面を開いた瞬間に全部が動かないように)
        public static float Approach(string key, float target, float speed)
        {
            float value = anims.TryGetValue(key, out var a) ? a.Value : target;
            value += (target - value) * (1f - MathF.Exp(-speed * frameDt));
            if (Math.Abs(target - value) < 0.001f) value = target;
            anims[key] = (value, frameNo);
            return value;
        }

        // key の値をすぐに value にする(ドラッグ中のつまみなど)
        public static float SetAnim(string key, float value)
        {
            anims[key] = (value, frameNo);
            return value;
        }

        // 2つの色の間(t = 0 で a、1 で b)
        public static Color Lerp(Color a, Color b, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return new Color((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t), (byte)(a.A + (b.A - a.A) * t));
        }

        public static Color WithAlpha(Color c, float alpha) =>
            new Color(c.R, c.G, c.B, (byte)Math.Clamp(c.A * alpha, 0f, 255f));

        // ===== 今のフレームのマウスの状態 =====
        private static Vector2 mouse;
        private static bool mousePressed;
        private static bool mouseDown;
        // 背後のシーンを描く間は、設定画面へ向けたクリックを渡さない
        private static bool mouseInputEnabled = true;

        public static void SetMouseInputEnabled(bool enabled) => mouseInputEnabled = enabled;

        // はみ出して見えない部分をクリックできないようにする範囲
        private static Rectangle clip = FullScreen();

        // プルダウンが開いている間は、他の部品を反応させない
        private static bool blocked;

        // スライダーのドラッグ中のID
        private static string? draggingId;

        // 選択中のスライダーのID(←→キーで動かせる)
        private static string? focusedId;

        // 数値の直接入力
        private static string? editingId;
        private static string editText = "";
        private static bool editSelectAll;  // 全選択中か(打つと上書き)
        private static float caretTimer;

        // プルダウン
        private static string? popupId;
        private static bool popupJustOpened;
        private static Rectangle popupAnchor;
        private static string[] popupOptions = Array.Empty<string>();
        private static int popupSelected;
        private static int popupFirst, popupRows = int.MaxValue;
        private static readonly Dictionary<string, int> popupResults = new();

        // 数値を入力中か
        public static bool IsEditing => editingId != null;

        // プルダウンが開いているか
        public static bool IsPopupOpen => popupId != null;

        // スライダーを選択中か(←→で動かせる状態)
        public static bool IsSliderFocused => focusedId != null;

        // ===== 毎フレームの準備 =====

        // 部品を描く前に、毎フレーム1回呼ぶ
        public static void BeginFrame()
        {
            frameNo++;
            frameDt = Math.Min(Raylib.GetFrameTime(), 1f / 30f);
            // 1秒(60フレーム)使われなかった動きは捨てる
            if (frameNo % 120 == 0)
            {
                staleAnims.Clear();
                foreach (var pair in anims) if (frameNo - pair.Value.Frame > 60) staleAnims.Add(pair.Key);
                foreach (string key in staleAnims) anims.Remove(key);
            }
            mouse = Screen.GetMousePosition();
            mousePressed = mouseInputEnabled && Raylib.IsMouseButtonPressed(MouseButton.Left);
            mouseDown = mouseInputEnabled && Raylib.IsMouseButtonDown(MouseButton.Left);
            blocked = popupId != null;
            popupJustOpened = false;
            caretTimer += Raylib.GetFrameTime();

            // マウスを離したらドラッグ終了
            //   本当にボタンを離したときだけ見る(mouseDown は「マウスを受け付けない」間 false になる。
            //   F1 の設定の後ろで選曲画面なども BeginFrame を呼ぶので、mouseDown で見るとドラッグが毎フレーム切れていた)
            if (!Raylib.IsMouseButtonDown(MouseButton.Left)) draggingId = null;
        }

        // クリックできる範囲を決める(スクロールする場所で使う)
        public static void SetClip(Rectangle area) => clip = area;
        public static void ResetClip() => clip = FullScreen();

        // 入力中・プルダウンなどを全部リセット(パネルを開閉したとき)
        public static void Reset()
        {
            draggingId = null;
            focusedId = null;
            editingId = null;
            popupId = null;
            popupResults.Clear();
        }

        // 数値入力をキャンセル
        public static void CancelEdit() => editingId = null;

        // プルダウンを閉じる
        public static void ClosePopup() => popupId = null;

        // ===== 判定 =====

        // マウスが乗っているか(見えない部分は無視。マウスを受け付けないときは乗っていないことにする)
        public static bool IsMouseOver(Rectangle area)
        {
            return mouseInputEnabled && Raylib.CheckCollisionPointRec(mouse, area) && Raylib.CheckCollisionPointRec(mouse, clip);
        }

        // クリックされたか(プルダウンが開いている間は反応しない)
        public static bool Clicked(Rectangle area)
        {
            return !blocked && mousePressed && IsMouseOver(area);
        }

        // ===== 部品 =====

        // 見出し(「サウンド設定」とか)
        public static void Header(string text, float x, float y)
        {
            FontManager.Draw(text, x, y, 40, TextColor);
        }

        // ふつうの文字
        public static void Label(string text, float x, float y, float size = 28)
        {
            FontManager.Draw(text, x, y, size, TextColor);
        }

        // 四角の真ん中に文字を書く
        public static void DrawCentered(string text, Rectangle area, float size, Color color)
        {
            Rectangle ink = FontManager.MeasureInk(text, size);
            float fit = Math.Min(1, Math.Min(Math.Max(1, area.Width - 12) / Math.Max(1, ink.Width), Math.Max(1, area.Height - 8) / Math.Max(1, ink.Height)));
            size *= fit;
            ink = FontManager.MeasureInk(text, size);
            FontManager.Draw(text, area.X + (area.Width - ink.Width) / 2f - ink.X, area.Y + (area.Height - ink.Height) / 2f - ink.Y, size, color);
        }

        // マウスを乗せたときの色の変わり具合(0→1。約0.1秒でじわっと)
        private static float HoverAnim(string kind, Rectangle area, string text, bool hover) =>
            Approach($"{kind}:{text}:{(int)area.X}:{(int)area.Y}:{(int)area.Width}", hover ? 1f : 0f, 18f);

        // ボタン。クリックされたら true
        public static bool Button(Rectangle area, string text)
        {
            bool hover = !blocked && IsMouseOver(area);
            float h = HoverAnim("btn", area, text, hover);
            float press = HoverAnim("btnp", area, text, hover && mouseDown);
            Rectangle body = Pressed(area, press);
            Glow(body, ControlRadius, AccentColor, h * 0.7f);
            SmoothShapes.Rectangle(body, ControlRadius, Lerp(BoxColor, BoxHoverColor, h));
            SmoothShapes.Border(body, ControlRadius, 1.5f, Lerp(BorderColor, AccentColor, h));
            DrawCentered(text, body, 26, TextColor);
            return Clicked(area);
        }

        // 決定・適用など、主な操作の朱色のボタン
        public static bool PrimaryButton(Rectangle area, string text)
        {
            bool hover = !blocked && IsMouseOver(area);
            float h = HoverAnim("pbtn", area, text, hover);
            float press = HoverAnim("pbtnp", area, text, hover && mouseDown);
            Rectangle body = Pressed(area, press);
            Glow(body, ControlRadius, AccentColor, h);
            SmoothShapes.Rectangle(body, ControlRadius, Lerp(Lerp(AccentColor, PrimaryHoverColor, h), AccentDeepColor, press));
            SmoothShapes.Border(body, ControlRadius, 1.5f, WithAlpha(SakuraColor, h));
            DrawCentered(text, body, 26, Color.White);
            return Clicked(area);
        }

        // チェックボックス。area は行全体(文字の部分を押してもON/OFFできる)
        public static bool Checkbox(Rectangle area, string text, bool value)
        {
            bool hover = !blocked && IsMouseOver(area);
            Label(text, area.X, area.Y + 4, 26);

            // ON/OFF は共通の丸いノブにする。丸は 0.1秒くらいでスッとすべって、色もじわっと変わる
            Rectangle track = new Rectangle(area.X + area.Width - 70, area.Y + (area.Height - 34) / 2f, 66, 34);
            float on = Approach($"chk:{text}", value ? 1f : 0f, 22f);
            float h = HoverAnim("chk", area, text, hover);
            Color off = Lerp(TrackColor, TrackHoverColor, h);
            Glow(track, 17f, AccentColor, h * on * 0.6f);
            SmoothShapes.Rounded(track, 1f, 16, Lerp(off, AccentColor, on));
            Vector2 knob = new Vector2(track.X + 18 + 30 * on, track.Y + 17);
            SmoothShapes.Circle(knob + new Vector2(0, 1.5f), 13.5f, new Color(0, 0, 0, 70)); // うすい影
            SmoothShapes.Circle(knob, 13, Color.White);

            return Clicked(area) ? !value : value;
        }

        // スライダー＋数値ボックス
        // bar：ドラッグする部分、box：数字の部分(クリックで直接入力)
        // クリックすると選択中になり、←→で±1、Shift＋←→で±10
        public static int Slider(string id, Rectangle bar, Rectangle box, int value, int min, int max)
        {
            // ----- 選択(クリックで選択、ほかの場所をクリックで解除) -----
            if (Clicked(bar) || Clicked(box))
            {
                focusedId = id;
            }
            else if (focusedId == id && mousePressed && !IsMouseOver(bar) && !IsMouseOver(box))
            {
                focusedId = null;
            }

            bool focused = focusedId == id;

            // ----- ←→キーで動かす(数値入力中とプルダウン表示中は無効) -----
            if (focused && editingId != id && !blocked)
            {
                bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
                int step = shift ? 10 : 1;

                if (Raylib.IsKeyPressed(KeyboardKey.Right) || Raylib.IsKeyPressedRepeat(KeyboardKey.Right)) value += step;
                if (Raylib.IsKeyPressed(KeyboardKey.Left) || Raylib.IsKeyPressedRepeat(KeyboardKey.Left)) value -= step;

                value = Math.Clamp(value, min, max);
            }

            // ----- バー -----
            if (Clicked(bar)) draggingId = id;

            if (draggingId == id && mouseDown)
            {
                float t = Math.Clamp((mouse.X - bar.X) / bar.Width, 0f, 1f);
                value = (int)MathF.Round(min + t * (max - min));
            }

            float ratio = max == min ? 0f : Math.Clamp((value - min) / (float)(max - min), 0f, 1f);
            bool dragging = draggingId == id;
            bool barHover = !blocked && IsMouseOver(bar);

            // つまみの場所：ドラッグ中はマウスにぴったり、キーや数字入力で変えたときはスッと動く
            float shown = dragging ? SetAnim($"sl:v:{id}", ratio) : Approach($"sl:v:{id}", ratio, 22f);

            // マイナスがあるスライダー(オフセットとか)は0の位置から塗る
            float zeroRatio = (min < 0 && max > 0) ? -min / (float)(max - min) : 0f;
            float fillFrom = Math.Min(zeroRatio, shown);
            float fillTo = Math.Max(zeroRatio, shown);

            // ----- 細いバー(6px) -----
            float cy = bar.Y + bar.Height / 2f;
            float h = Approach($"sl:h:{id}", barHover || dragging ? 1f : 0f, 18f);
            SmoothShapes.Rounded(new Rectangle(bar.X, cy - 3, bar.Width, 6), 1f, 8, Lerp(TrackColor, TrackHoverColor, h));
            if (zeroRatio > 0f) // 0 の場所に小さい目印
                SmoothShapes.Rounded(new Rectangle(bar.X + bar.Width * zeroRatio - 1, cy - 8, 2, 16), 1f, 4, WithAlpha(SubTextColor, 0.6f));
            float fillWidth = bar.Width * (fillTo - fillFrom);
            if (fillWidth > 0.5f)
                SmoothShapes.Rounded(new Rectangle(bar.X + bar.Width * fillFrom, cy - 3, fillWidth, 6), 1f, 8, FillColor);

            // ----- つまみ(白い丸に朱色の点)。さわると大きく、まわりが朱色に光る -----
            Vector2 knob = new Vector2(bar.X + bar.Width * shown, cy);
            float radius = Approach($"sl:r:{id}", dragging ? 13f : barHover ? 12f : 10f, 20f);
            float glow = Approach($"sl:g:{id}", dragging || focused ? 1f : 0f, 16f);
            // 光は、つまみのまわり 7px だけ(11/12：広いと、上の数字の吹き出しに上半分が隠れて、下にずれて見えた)
            GlowCircle(knob, radius, AccentColor, Math.Max(glow, h), 7f);
            SmoothShapes.Circle(knob + new Vector2(0, 1f), radius + 1f, new Color(0, 0, 0, 70)); // うすい影(下に 1px だけ。大きいと光が下にずれて見える)
            SmoothShapes.Circle(knob, radius, Color.White);
            SmoothShapes.Circle(knob, radius * 0.42f, AccentColor);

            // ----- 数字の吹き出し(さわっている間だけ、少し下からふわっと出る) -----
            float bubble = Approach($"sl:b:{id}", dragging || barHover ? 1f : 0f, 18f);
            if (bubble > 0.02f)
            {
                string text = value.ToString();
                Vector2 size = FontManager.Measure(text, 20);
                float w = Math.Max(44f, size.X + 20f), bh = 30f;
                float by = cy - radius - 22f - bh + 6f * (1f - bubble);   // つまみの光にかぶらないように、少し上に
                Rectangle tip = new Rectangle(knob.X - w / 2f, by, w, bh);
                Color white = WithAlpha(Color.White, bubble);
                SmoothShapes.Rectangle(tip, ControlRadius, white);
                // 下向きの小さい三角(左回りの順に点を並べる)
                SmoothShapes.Triangle(new Vector2(knob.X - 6, by + bh - 1), new Vector2(knob.X, by + bh + 6), new Vector2(knob.X + 6, by + bh - 1), white);
                DrawCentered(text, tip, 20, WithAlpha(BubbleTextColor, bubble));
            }

            // ----- 数値ボックス -----
            bool editing = editingId == id;

            if (!editing && Clicked(box))
            {
                // 入力開始
                editingId = id;
                editText = value.ToString();
                editSelectAll = true; // 最初は全選択
                caretTimer = 0f;
                editing = true;
            }
            else if (editing)
            {
                // 打った文字を受け取る(数字と、マイナスOKなときは先頭の「-」だけ)
                int c = Raylib.GetCharPressed();
                while (c > 0)
                {
                    bool isDigit = c >= '0' && c <= '9';
                    bool isMinus = c == '-' && min < 0;

                    if (isDigit || isMinus)
                    {
                        // 全選択中なら、前の数字を消して上書き
                        if (editSelectAll)
                        {
                            editText = "";
                            editSelectAll = false;
                        }

                        if (isDigit && editText.Length < 6) editText += (char)c;
                        else if (isMinus && editText.Length == 0) editText += "-";
                    }
                    c = Raylib.GetCharPressed();
                }

                // BackSpace：全選択中なら全部消す、そうでなければ1文字消す(押しっぱなしで連続)
                if (Raylib.IsKeyPressed(KeyboardKey.Backspace) || Raylib.IsKeyPressedRepeat(KeyboardKey.Backspace))
                {
                    if (editSelectAll)
                    {
                        editText = "";
                        editSelectAll = false;
                    }
                    else if (editText.Length > 0)
                    {
                        editText = editText[..^1];
                    }
                }

                // ←→で全選択を解除(末尾から編集できるようにする)
                if (Raylib.IsKeyPressed(KeyboardKey.Left) || Raylib.IsKeyPressed(KeyboardKey.Right))
                {
                    editSelectAll = false;
                }

                // Enter か、ほかの場所をクリックしたら決定
                bool enter = Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter);
                bool clickOutside = mousePressed && !IsMouseOver(box);
                if (enter || clickOutside)
                {
                    if (int.TryParse(editText, out int typed))
                    {
                        value = Math.Clamp(typed, min, max);
                    }
                    editingId = null;
                    editing = false;
                }
            }

            // 数字の箱は角を丸くして、細いふち
            bool boxHover = !blocked && IsMouseOver(box);
            float bhov = HoverAnim("slbox", box, id, boxHover);
            if (editing) Glow(box, ControlRadius, AccentColor, 0.7f);
            SmoothShapes.Rectangle(box, ControlRadius, editing ? BackgroundColor : Lerp(BoxColor, BoxHoverColor, bhov));
            SmoothShapes.Border(box, ControlRadius, editing ? 2f : 1f, editing ? AccentColor : BorderColor);

            if (editing)
            {

                if (editSelectAll)
                {
                    // 全選択中：文字の後ろを朱色でハイライト
                    Vector2 size = FontManager.Measure(editText, 26);
                    Rectangle highlight = new Rectangle(box.X + (box.Width - size.X) / 2f - 3, box.Y + (box.Height - size.Y) / 2f, size.X + 6, size.Y);
                    SmoothShapes.Rectangle(highlight, 4f, AccentDeepColor);
                    DrawCentered(editText, box, 26, TextColor);
                }
                else
                {
                    string caret = (caretTimer % 1f) < 0.5f ? "|" : " ";
                    DrawCentered(editText + caret, box, 26, TextColor);
                }
            }
            else
            {
                DrawCentered(value.ToString(), box, 26, TextColor);
            }

            return value;
        }

        // 横に並んだ切り替え(2〜4個から1つ選ぶ)。選び直されたら新しい番号を返す
        //   11/12 たけらい「A(カード＋横並びの切り替え)」。プルダウンは「押す → 開く → 選ぶ」の2回だけど、
        //   これは選べる物が最初から全部見えて、1回で変えられる。選んでいる所の朱色の板は、0.1秒くらいですべって動く
        public static int Segmented(string id, Rectangle area, string[] options, int selected)
        {
            SmoothShapes.Rectangle(area, ControlRadius, BackgroundColor);
            SmoothShapes.Border(area, ControlRadius, 1.5f, BorderColor);
            float cell = (area.Width - 6f) / options.Length;
            float pos = Approach($"seg:{id}", Math.Clamp(selected, 0, options.Length - 1), 20f);
            if (selected >= 0 && selected < options.Length)
                SmoothShapes.Rectangle(new Rectangle(area.X + 3f + pos * cell, area.Y + 3f, cell, area.Height - 6f), ControlRadius - 3f, AccentColor);
            int result = selected;
            for (int i = 0; i < options.Length; i++)
            {
                Rectangle item = new Rectangle(area.X + 3f + i * cell, area.Y + 3f, cell, area.Height - 6f);
                bool hover = !blocked && IsMouseOver(item);
                float h = Approach($"seg:{id}:{i}", hover && i != selected ? 1f : 0f, 18f);
                DrawCentered(options[i], item, 22, i == selected ? Color.White : Lerp(SubTextColor, TextColor, h));
                if (Clicked(item)) result = i;
            }
            return result;
        }

        // 横に並べても字が入るか(入らなければプルダウンにする)
        public static bool FitsSegmented(Rectangle area, string[] options)
        {
            if (options.Length < 2 || options.Length > 4) return false;
            float cell = (area.Width - 6f) / options.Length;
            foreach (string option in options)
                if (FontManager.Measure(option, 22).X + 24f > cell) return false;
            return true;
        }

        // プルダウン。選び直されたら新しい番号を返す
        public static int Dropdown(string id, Rectangle area, string[] options, int selected, int maxVisibleRows = int.MaxValue)
        {
            // 一覧で選ばれていたら、その番号にする
            if (popupResults.Remove(id, out int result))
            {
                selected = result;
            }

            bool open = popupId == id;
            bool hover = !blocked && IsMouseOver(area);
            float h = HoverAnim("dd", area, id, hover || open);
            float openGlow = Approach($"dd:o:{id}", open ? 1f : 0f, 16f);
            Glow(area, ControlRadius, AccentColor, openGlow * 0.7f);
            SmoothShapes.Rectangle(area, ControlRadius, Lerp(BoxColor, BoxHoverColor, h));
            SmoothShapes.Border(area, ControlRadius, 1.5f, Lerp(BorderColor, AccentColor, openGlow));

            string text = (selected >= 0 && selected < options.Length) ? options[selected] : "-";
            FontManager.Draw(FitDropdownText(text, area.Width - 58), area.X + 16,
                area.Y + (area.Height - FontManager.Measure(text, 26).Y) / 2f, 26, TextColor);
            // 下向きの「く」の字(線で描く。字の「▼」より細くてきれい)
            Vector2 chevron = new Vector2(area.X + area.Width - 28, area.Y + area.Height / 2f + 3);
            SmoothShapes.Line(chevron + new Vector2(-8, -8), chevron, 2.5f, SubTextColor);
            SmoothShapes.Line(chevron + new Vector2(8, -8), chevron, 2.5f, SubTextColor);

            if (options.Length > 0 && Clicked(area))
            {
                popupId = id;
                popupJustOpened = true;
                popupAnchor = area;
                popupOptions = options;
                popupSelected = selected;
                popupRows = Math.Min(options.Length, Math.Max(1, maxVisibleRows));
                popupFirst = Math.Clamp(selected - popupRows / 2, 0, Math.Max(0, options.Length - popupRows));
            }

            return selected;
        }

        // プルダウンの一覧を描く(他の部品より上に出したいので、全部描いた最後に呼ぶ)
        public static void DrawPopups()
        {
            if (popupId == null) return;

            const float itemHeight = 40f;
            Rectangle list = new Rectangle(popupAnchor.X, popupAnchor.Y + popupAnchor.Height, popupAnchor.Width, itemHeight * popupRows);

            // 下にはみ出すなら上に出す
            if (list.Y + list.Height > Screen.Height)
            {
                list.Y = popupAnchor.Y - list.Height;
            }

            // 一覧の箱(角を丸く、なめらかに。前はカクカクの四角だった)
            list.Y += list.Y >= popupAnchor.Y ? 6 : -6;
            SmoothShapes.Rectangle(new Rectangle(list.X, list.Y + 4, list.Width, list.Height), ControlRadius, new Color(0, 0, 0, 90));   // 影
            SmoothShapes.Rectangle(list, ControlRadius, new Color(33, 20, 26, 255));
            SmoothShapes.Border(list, ControlRadius, 1.5f, BorderColor);

            if (Raylib.CheckCollisionPointRec(mouse, list))
                popupFirst = Math.Clamp(popupFirst - Math.Sign(Raylib.GetMouseWheelMove()), 0, Math.Max(0, popupOptions.Length - popupRows));
            for (int row = 0; row < popupRows; row++)
            {
                int i = popupFirst + row;
                Rectangle item = new Rectangle(list.X, list.Y + row * itemHeight, list.Width, itemHeight);
                bool hover = Raylib.CheckCollisionPointRec(mouse, item);

                Rectangle pill = new Rectangle(item.X + 5, item.Y + 3, item.Width - 10, item.Height - 6);
                if (i == popupSelected || hover) SmoothShapes.Rectangle(pill, 7f, i == popupSelected ? BoxHoverColor : BoxColor);
                if (i == popupSelected) SmoothShapes.Rounded(new Rectangle(pill.X, pill.Y + 8, 4, pill.Height - 16), 1f, 4, AccentColor);

                FontManager.Draw(FitDropdownText(popupOptions[i], item.Width - 32), item.X + 16,
                    item.Y + (item.Height - FontManager.Measure(popupOptions[i], 26).Y) / 2f, 26, i == popupSelected || hover ? TextColor : SubTextColor);

                if (hover && mousePressed && !popupJustOpened)
                {
                    popupResults[popupId] = i;
                    popupId = null;
                    return;
                }
            }

            if (popupOptions.Length > popupRows)
            {
                float thumb = list.Height * popupRows / popupOptions.Length;
                SmoothShapes.Rounded(new(list.X + list.Width - 7, list.Y + 6 + (list.Height - 12 - thumb) * popupFirst / (popupOptions.Length - popupRows), 4, Math.Max(12, thumb)), 1f, 4, AccentColor);
            }

            // 一覧の外をクリックしたら閉じる(開いた瞬間のクリックは無視)
            if (mousePressed && !popupJustOpened && !Raylib.CheckCollisionPointRec(mouse, list))
            {
                popupId = null;
            }
        }

        // 長いスキン名なども矢印やパネルの外に重ならないようにする
        private static string FitDropdownText(string text, float maxWidth)
        {
            if (FontManager.Measure(text, 26).X <= maxWidth) return text;
            string cut = text;
            while (cut.Length > 1 && FontManager.Measure(cut + "…", 26).X > maxWidth)
                cut = cut[..^1];
            return cut + "…";
        }

        private static Rectangle FullScreen() => new Rectangle(0, 0, Screen.Width, Screen.Height);
    }
}
