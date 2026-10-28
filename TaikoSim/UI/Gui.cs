using System.Numerics;
using Raylib_cs;
using TaikoSim.Graphics;

namespace TaikoSim.UI
{
    // 設定画面などで使うGUI部品
    // 「描画」と「クリック判定」を1つの関数でまとめてやる方式
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
            mouse = Screen.GetMousePosition();
            mousePressed = mouseInputEnabled && Raylib.IsMouseButtonPressed(MouseButton.Left);
            mouseDown = mouseInputEnabled && Raylib.IsMouseButtonDown(MouseButton.Left);
            blocked = popupId != null;
            popupJustOpened = false;
            caretTimer += Raylib.GetFrameTime();

            // マウスを離したらドラッグ終了
            if (!mouseDown) draggingId = null;
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

        // マウスが乗っているか(見えない部分は無視)
        public static bool IsMouseOver(Rectangle area)
        {
            return Raylib.CheckCollisionPointRec(mouse, area) && Raylib.CheckCollisionPointRec(mouse, clip);
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
            Vector2 measured = FontManager.Measure(text, size);
            FontManager.Draw(text, area.X + (area.Width - measured.X) / 2f, area.Y + (area.Height - measured.Y) / 2f, size, color);
        }

        // ボタン。クリックされたら true
        public static bool Button(Rectangle area, string text)
        {
            bool hover = !blocked && IsMouseOver(area);
            Raylib.DrawRectangleRounded(area, 0.18f, 8, hover ? BoxHoverColor : BoxColor);
            DrawCentered(text, area, 26, TextColor);
            return Clicked(area);
        }

        // 決定・適用など、主な操作の青いボタン
        public static bool PrimaryButton(Rectangle area, string text)
        {
            bool hover = !blocked && IsMouseOver(area);
            Raylib.DrawRectangleRounded(area, 0.18f, 8, hover ? new Color(84, 153, 255, 255) : AccentColor);
            DrawCentered(text, area, 26, Color.White);
            return Clicked(area);
        }

        // チェックボックス。area は行全体(文字の部分を押してもON/OFFできる)
        public static bool Checkbox(Rectangle area, string text, bool value)
        {
            bool hover = !blocked && IsMouseOver(area);
            Label(text, area.X, area.Y + 4, 26);

            // ON/OFF は共通の丸いノブにする
            Rectangle track = new Rectangle(area.X + area.Width - 70, area.Y + (area.Height - 34) / 2f, 66, 34);
            Raylib.DrawRectangleRounded(track, 1f, 16, value ? AccentColor : (hover ? BoxHoverColor : BoxColor));
            Raylib.DrawCircleV(new Vector2(track.X + (value ? 48 : 18), track.Y + 17), 13, Color.White);

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

            // マイナスがあるスライダー(オフセットとか)は0の位置から塗る
            float zeroRatio = (min < 0 && max > 0) ? -min / (float)(max - min) : 0f;
            float fillFrom = Math.Min(zeroRatio, ratio);
            float fillTo = Math.Max(zeroRatio, ratio);

            bool barHover = !blocked && IsMouseOver(bar);
            Raylib.DrawRectangleRounded(bar, 0.7f, 8, barHover || draggingId == id ? BoxHoverColor : BoxColor);
            Raylib.DrawRectangleRounded(new Rectangle(bar.X + bar.Width * fillFrom, bar.Y, Math.Max(1, bar.Width * (fillTo - fillFrom)), bar.Height), 0.7f, 8, FillColor);
            Raylib.DrawCircleV(new Vector2(bar.X + bar.Width * ratio, bar.Y + bar.Height / 2f), 12, Color.White);

            // 選択中は枠を光らせる
            if (focused)
            {
                Raylib.DrawRectangleLinesEx(new Rectangle(bar.X - 3, bar.Y - 3, bar.Width + 6, bar.Height + 6), 2, AccentColor);
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

            bool boxHover = !blocked && IsMouseOver(box);
            Raylib.DrawRectangleRec(box, editing ? new Color(18, 22, 36, 255) : (boxHover ? BoxHoverColor : BoxColor));

            if (editing)
            {
                Raylib.DrawRectangleLinesEx(box, 2, AccentColor);

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
            Raylib.DrawRectangleRounded(area, 0.18f, 8, hover || open ? BoxHoverColor : BoxColor);

            string text = (selected >= 0 && selected < options.Length) ? options[selected] : "-";
            DrawCentered(text, area, 26, TextColor);
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

                DrawCentered(popupOptions[i], item, 26, TextColor);

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

        private static Rectangle FullScreen() => new Rectangle(0, 0, Screen.Width, Screen.Height);
    }
}
