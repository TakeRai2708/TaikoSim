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
        public static readonly Color TextColor = new Color(244, 247, 252, 255);
        public static readonly Color SubTextColor = new Color(170, 181, 200, 255);
        public static readonly Color BoxColor = new Color(23, 34, 53, 255);
        public static readonly Color BoxHoverColor = new Color(34, 52, 79, 255);
        public static readonly Color FillColor = new Color(59, 130, 246, 255);
        public static readonly Color AccentColor = new Color(59, 130, 246, 255);
        public static readonly Color BackgroundColor = new Color(11, 15, 20, 255);
        public static readonly Color PanelColor = new Color(17, 24, 39, 255);
        public static readonly Color BorderColor = new Color(37, 50, 71, 255);
        private static readonly Color PrimaryHoverColor = new Color(84, 153, 255, 255);
        private static readonly Color TrackHoverColor = new Color(52, 68, 96, 255);
        private static readonly Color BubbleTextColor = new Color(17, 24, 39, 255);
        private const float ControlRadius = 6;

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
            SmoothShapes.Rectangle(area, ControlRadius, Lerp(BoxColor, BoxHoverColor, h));
            DrawCentered(text, area, 26, TextColor);
            return Clicked(area);
        }

        // 決定・適用など、主な操作の青いボタン
        public static bool PrimaryButton(Rectangle area, string text)
        {
            bool hover = !blocked && IsMouseOver(area);
            float h = HoverAnim("pbtn", area, text, hover);
            SmoothShapes.Rectangle(area, ControlRadius, Lerp(AccentColor, PrimaryHoverColor, h));
            DrawCentered(text, area, 26, Color.White);
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
            Color off = Lerp(BoxColor, BoxHoverColor, h);
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
            SmoothShapes.Rounded(new Rectangle(bar.X, cy - 3, bar.Width, 6), 1f, 8, Lerp(BorderColor, TrackHoverColor, h));
            if (zeroRatio > 0f) // 0 の場所に小さい目印
                SmoothShapes.Rounded(new Rectangle(bar.X + bar.Width * zeroRatio - 1, cy - 8, 2, 16), 1f, 4, WithAlpha(SubTextColor, 0.6f));
            float fillWidth = bar.Width * (fillTo - fillFrom);
            if (fillWidth > 0.5f)
                SmoothShapes.Rounded(new Rectangle(bar.X + bar.Width * fillFrom, cy - 3, fillWidth, 6), 1f, 8, FillColor);

            // ----- つまみ(白い丸)。さわると大きく、選択中・ドラッグ中は細い青枠 -----
            Vector2 knob = new Vector2(bar.X + bar.Width * shown, cy);
            float radius = Approach($"sl:r:{id}", dragging ? 13f : barHover ? 12f : 10f, 20f);
            float glow = Approach($"sl:g:{id}", dragging || focused ? 1f : 0f, 16f);
            if (glow > 0.01f) SmoothShapes.Ring(knob, radius + 3, radius + 5, WithAlpha(AccentColor, 0.5f * glow));
            SmoothShapes.Circle(knob + new Vector2(0, 2f), radius + 1.5f, new Color(0, 0, 0, 90)); // うすい影
            SmoothShapes.Circle(knob, radius, Color.White);

            // ----- 数字の吹き出し(さわっている間だけ、少し下からふわっと出る) -----
            float bubble = Approach($"sl:b:{id}", dragging || barHover ? 1f : 0f, 18f);
            if (bubble > 0.02f)
            {
                string text = value.ToString();
                Vector2 size = FontManager.Measure(text, 20);
                float w = Math.Max(44f, size.X + 20f), bh = 30f;
                float by = cy - radius - 14f - bh + 6f * (1f - bubble);
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
            SmoothShapes.Rectangle(box, ControlRadius, editing ? new Color(18, 22, 36, 255) : Lerp(BoxColor, BoxHoverColor, bhov));
            SmoothShapes.Border(box, ControlRadius, editing ? 2f : 1f, editing ? AccentColor : BorderColor);

            if (editing)
            {

                if (editSelectAll)
                {
                    // 全選択中：文字の後ろを青くハイライト
                    Vector2 size = FontManager.Measure(editText, 26);
                    Rectangle highlight = new Rectangle(box.X + (box.Width - size.X) / 2f - 3, box.Y + (box.Height - size.Y) / 2f, size.X + 6, size.Y);
                    Raylib.DrawRectangleRec(highlight, new Color(50, 100, 200, 255));
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

        // プルダウン。選び直されたら新しい番号を返す
        public static int Dropdown(string id, Rectangle area, string[] options, int selected)
        {
            // 一覧で選ばれていたら、その番号にする
            if (popupResults.Remove(id, out int result))
            {
                selected = result;
            }

            bool open = popupId == id;
            bool hover = !blocked && IsMouseOver(area);
            float h = HoverAnim("dd", area, id, hover || open);
            SmoothShapes.Rectangle(area, ControlRadius, Lerp(BoxColor, BoxHoverColor, h));

            string text = (selected >= 0 && selected < options.Length) ? options[selected] : "-";
            FontManager.Draw(FitDropdownText(text, area.Width - 58), area.X + 16,
                area.Y + (area.Height - FontManager.Measure(text, 26).Y) / 2f, 26, TextColor);
            FontManager.Draw("▼", area.X + area.Width - 32, area.Y + (area.Height - 20) / 2f, 20, SubTextColor);

            if (Clicked(area))
            {
                popupId = id;
                popupJustOpened = true;
                popupAnchor = area;
                popupOptions = options;
                popupSelected = selected;
            }

            return selected;
        }

        // プルダウンの一覧を描く(他の部品より上に出したいので、全部描いた最後に呼ぶ)
        public static void DrawPopups()
        {
            if (popupId == null) return;

            const float itemHeight = 40f;
            Rectangle list = new Rectangle(popupAnchor.X, popupAnchor.Y + popupAnchor.Height, popupAnchor.Width, itemHeight * popupOptions.Length);

            // 下にはみ出すなら上に出す
            if (list.Y + list.Height > Screen.Height)
            {
                list.Y = popupAnchor.Y - list.Height;
            }

            Raylib.DrawRectangleRec(list, new Color(14, 17, 27, 250));
            Raylib.DrawRectangleLinesEx(list, 1, new Color(90, 100, 130, 255));

            for (int i = 0; i < popupOptions.Length; i++)
            {
                Rectangle item = new Rectangle(list.X, list.Y + i * itemHeight, list.Width, itemHeight);
                bool hover = Raylib.CheckCollisionPointRec(mouse, item);

                if (i == popupSelected) Raylib.DrawRectangleRec(item, new Color(40, 70, 140, 255));
                else if (hover) Raylib.DrawRectangleRec(item, BoxHoverColor);

                FontManager.Draw(FitDropdownText(popupOptions[i], item.Width - 32), item.X + 16,
                    item.Y + (item.Height - FontManager.Measure(popupOptions[i], 26).Y) / 2f, 26, TextColor);

                if (hover && mousePressed && !popupJustOpened)
                {
                    popupResults[popupId] = i;
                    popupId = null;
                    return;
                }
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
