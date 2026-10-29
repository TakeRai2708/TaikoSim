using System.Globalization;
using System.Numerics;
using Raylib_cs;
using TaikoSim.Configs;
using TaikoSim.Graphics;

namespace TaikoSim.UI
{
    // 演奏オプションの板(難易度選択の「オプション」で開く)
    // 中身は前の選曲画面の右上のメニューと同じ(オート演奏・HS倍率・オート連打・オート精度・毎回同じ出方・板・デバッグ記録)
    // マウスでクリック、数字の箱はキーボードで入力。Esc・板の外のクリックで閉じる
    public sealed class PlayOptionsPanel
    {
        // ===== 配置(1920×1080。TaikøNauts と同じく左下) =====
        private static readonly Rectangle Menu = new Rectangle(20, 480, 330, 580);
        private static readonly Rectangle AutoPlayItem = new Rectangle(30, 490, 310, 62);
        private static readonly Rectangle HiSpeedBox = new Rectangle(144, 566, 184, 52);
        private static readonly Rectangle AutoRollBox = new Rectangle(144, 686, 184, 52);
        private static readonly Rectangle AutoFixedItem = new Rectangle(30, 900, 310, 44);
        private const float DisplaySectionY = 952;
        private static readonly Rectangle ScoreboardItem = new Rectangle(30, 958, 310, 44);
        private static readonly Rectangle DebugLogItem = new Rectangle(30, 1006, 310, 44);
        private static readonly Color ErrorColor = new Color(255, 110, 100, 255);

        public bool IsOpen { get; private set; }

        // 数字の箱のどれかに入力中か(入力中は、ドン・カッ・Enter を箱の方に使う)
        public bool IsTyping => hiSpeedFocused || autoRollFocused || percentBoxes.Any(b => b.Focused);

        private float caretTimer;
        private bool hiSpeedFocused, hiSpeedSelectAll, hiSpeedInvalid;
        private string hiSpeedText = "0";
        private bool autoRollFocused, autoRollSelectAll, autoRollInvalid;
        private string autoRollText = "20";

        private sealed class PercentBox
        {
            public Rectangle Area;
            public string Label = "";
            public float LabelX;
            public Func<int> Get = () => 0;
            public Action<int> Set = _ => { };
            public string Text = "0";
            public bool Focused, SelectAll, Invalid;
        }
        private readonly PercentBox[] percentBoxes =
        {
            new PercentBox { Label = "良", LabelX = 36, Area = new Rectangle(62, 818, 58, 46),
                Get = () => ConfigManager.Game.AutoGoodPercent, Set = v => ConfigManager.Game.AutoGoodPercent = v },
            new PercentBox { Label = "可", LabelX = 132, Area = new Rectangle(158, 818, 58, 46),
                Get = () => ConfigManager.Game.AutoOkPercent, Set = v => ConfigManager.Game.AutoOkPercent = v },
            new PercentBox { Label = "不可", LabelX = 226, Area = new Rectangle(274, 818, 58, 46),
                Get = () => ConfigManager.Game.AutoBadPercent, Set = v => ConfigManager.Game.AutoBadPercent = v },
        };

        // ===== 開く・閉じる =====

        public void Open()
        {
            ResetHiSpeedInput();
            ResetAutoRollInput();
            ResetPercentInputs();
            caretTimer = 0f;
            IsOpen = true;
        }

        public void Close()
        {
            if (!IsOpen) return;
            FinishHiSpeedInput();
            FinishAutoRollInput();
            FinishPercentInputs();
            IsOpen = false;
        }

        // 毎フレーム(開いているとき)。数字の箱に入力中なら文字を受け取る
        public void Update(float dt)
        {
            if (!IsOpen) return;
            caretTimer += dt;
            if (Game.IsSettingsOpen) { DiscardTypedChars(); return; }
            if (hiSpeedFocused) UpdateHiSpeedInput();
            else if (autoRollFocused) UpdateAutoRollInput();
            else if (percentBoxes.FirstOrDefault(b => b.Focused) is PercentBox box) UpdatePercentInput(box);
            else DiscardTypedChars();
        }

        // 描く(クリックもここで受け取る)。active が false(設定パネルが開いている)なら押せない
        public void Draw(bool active)
        {
            if (!IsOpen) return;

            Vector2 mouse = Screen.GetMousePosition();
            bool pressed = active && Raylib.IsMouseButtonPressed(MouseButton.Left);
            GameConfig game = ConfigManager.Game;

            Raylib.DrawRectangleRec(Menu, new Color(14, 17, 27, 245));
            Raylib.DrawRectangleLinesEx(Menu, 2, Gui.AccentColor);

            // オート演奏
            ToggleItem(AutoPlayItem, "オート演奏", game.AutoPlay, mouse, pressed, v => game.AutoPlay = v, 26);

            bool overHiSpeed = Raylib.CheckCollisionPointRec(mouse, HiSpeedBox);
            bool overAutoRoll = Raylib.CheckCollisionPointRec(mouse, AutoRollBox);
            if (pressed)
            {
                if (overHiSpeed)
                {
                    FinishAutoRollInput();
                    if (!hiSpeedFocused) hiSpeedSelectAll = true;
                    hiSpeedFocused = true;
                    caretTimer = 0f;
                }
                else FinishHiSpeedInput();

                if (overAutoRoll)
                {
                    if (!autoRollFocused) autoRollSelectAll = true;
                    autoRollFocused = true;
                    caretTimer = 0f;
                }
                else FinishAutoRollInput();

                foreach (PercentBox box in percentBoxes)
                {
                    if (Raylib.CheckCollisionPointRec(mouse, box.Area))
                    {
                        if (!box.Focused) box.SelectAll = true;
                        box.Focused = true;
                        caretTimer = 0f;
                    }
                    else FinishPercentInput(box);
                }
            }

            // HS倍率
            FontManager.Draw("HS倍率", 44, 578, 24, Color.White);
            DrawNumberBox(HiSpeedBox, hiSpeedText, hiSpeedFocused, hiSpeedSelectAll, hiSpeedInvalid);
            FontManager.Draw(hiSpeedInvalid ? "0〜100の数値を入力してください" : "0＝譜面どおり / Enterで確定", 34, 632, 19,
                hiSpeedInvalid ? ErrorColor : Gui.SubTextColor);
            FontManager.Draw("TJAのSCROLLに掛ける倍率", 34, 659, 18, Gui.SubTextColor);

            // オートの連打の速さ
            FontManager.Draw("オート連打", 32, 700, 21, Color.White);
            DrawNumberBox(AutoRollBox, autoRollText, autoRollFocused, autoRollSelectAll, autoRollInvalid);
            FontManager.Draw("打/秒", AutoRollBox.X + 120, AutoRollBox.Y + 14, 20, Gui.SubTextColor);
            FontManager.Draw(autoRollInvalid ? "0〜1000の整数を入力してください" : "0＝連打・風船を叩かない / Enterで確定", 34, 752, 19,
                autoRollInvalid ? ErrorColor : Gui.SubTextColor);

            // オートの精度(良・可・不可の%)
            FontManager.Draw("オート精度（%）", 32, 786, 21, Color.White);
            foreach (PercentBox box in percentBoxes) DrawPercentBox(box);
            bool percentInvalid = percentBoxes.Any(b => b.Invalid);
            FontManager.Draw(percentInvalid ? "0〜100の整数を入力してください" : "合計が100でなくても割合で使う", 34, 872, 18,
                percentInvalid ? ErrorColor : Gui.SubTextColor);

            // 毎回同じ出方にするか
            ToggleItem(AutoFixedItem, "毎回同じ出方", game.AutoFixedPattern, mouse, pressed, v => game.AutoFixedPattern = v, 22);

            // 表示・記録
            Raylib.DrawLineEx(new Vector2(Menu.X + 12, DisplaySectionY),
                new Vector2(Menu.X + Menu.Width - 12, DisplaySectionY), 1, Gui.SubTextColor);
            ToggleItem(ScoreboardItem, "良・可・不可の板", game.ShowScoreboard, mouse, pressed, v => game.ShowScoreboard = v, 22);
            ToggleItem(DebugLogItem, "デバッグ記録(CSV)", game.DebugLog, mouse, pressed, v => game.DebugLog = v, 22);

            // 板の外をクリックしたら閉じる
            if (pressed && !Raylib.CheckCollisionPointRec(mouse, Menu)) Close();
        }

        // ===== 部品 =====

        // ON/OFF の項目。クリックされたら set で新しい値を入れてから保存する
        private static void ToggleItem(Rectangle area, string label, bool value, Vector2 mouse, bool pressed, Action<bool> set, float size)
        {
            bool over = Raylib.CheckCollisionPointRec(mouse, area);
            if (over) Raylib.DrawRectangleRec(area, Gui.BoxHoverColor);
            if (pressed && over)
            {
                value = !value;
                set(value);
                ConfigManager.SaveAll();
            }
            FontManager.Draw(label, area.X + 8, area.Y + (area.Height - size) / 2f - 2, size, Color.White);
            Gui.DrawCentered(value ? "ON" : "OFF",
                new Rectangle(area.X + 220, area.Y, 76, area.Height),
                size + 2, value ? Color.Lime : Gui.SubTextColor);
        }

        private void DrawNumberBox(Rectangle box, string text, bool focused, bool selectAll, bool invalid)
        {
            Raylib.DrawRectangleRec(box, focused ? Gui.BoxHoverColor : Gui.BoxColor);
            Raylib.DrawRectangleLinesEx(box, 2, invalid ? ErrorColor : focused ? Gui.AccentColor : Gui.SubTextColor);
            if (focused && selectAll)
                Raylib.DrawRectangleRec(new Rectangle(box.X + 10, box.Y + 8, 130, 36), new Color(45, 82, 130, 255));
            string caret = focused && caretTimer % 1f < 0.5f ? "|" : "";
            FontManager.Draw(text + caret, box.X + 12, box.Y + 10, 26, Color.White);
        }

        private void DrawPercentBox(PercentBox box)
        {
            FontManager.Draw(box.Label, box.LabelX, box.Area.Y + 11, 22, Color.White);
            Raylib.DrawRectangleRec(box.Area, box.Focused ? Gui.BoxHoverColor : Gui.BoxColor);
            Raylib.DrawRectangleLinesEx(box.Area, 2,
                box.Invalid ? ErrorColor : box.Focused ? Gui.AccentColor : Gui.SubTextColor);
            if (box.Focused && box.SelectAll)
                Raylib.DrawRectangleRec(new Rectangle(box.Area.X + 6, box.Area.Y + 7, box.Area.Width - 12, 32), new Color(45, 82, 130, 255));
            string caret = box.Focused && caretTimer % 1f < 0.5f ? "|" : "";
            FontManager.Draw(box.Text + caret, box.Area.X + 8, box.Area.Y + 9, 24, Color.White);
        }

        private static bool KeyPressed(KeyboardKey key) => Raylib.IsKeyPressed(key) || Raylib.IsKeyPressedRepeat(key);

        private static bool EnterPressed() => Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter);

        private static void DiscardTypedChars()
        {
            while (Raylib.GetCharPressed() > 0) { }
        }

        // ===== HS倍率の箱(0〜100の数値) =====

        private void ResetHiSpeedInput()
        {
            hiSpeedText = ConfigManager.Game.HiSpeed.ToString("0.#######", CultureInfo.InvariantCulture);
            hiSpeedFocused = hiSpeedSelectAll = hiSpeedInvalid = false;
        }

        private void UpdateHiSpeedInput()
        {
            bool control = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
            if (control && Raylib.IsKeyPressed(KeyboardKey.A)) hiSpeedSelectAll = true;
            int c;
            while ((c = Raylib.GetCharPressed()) > 0)
            {
                if (control || (c != '.' && (c < '0' || c > '9'))) continue;
                if (hiSpeedSelectAll) { hiSpeedText = ""; hiSpeedSelectAll = false; }
                if (hiSpeedText.Length < 8) hiSpeedText += (char)c;
                hiSpeedInvalid = false;
            }
            if (KeyPressed(KeyboardKey.Backspace))
            {
                if (hiSpeedSelectAll) hiSpeedText = "";
                else if (hiSpeedText.Length > 0) hiSpeedText = hiSpeedText[..^1];
                hiSpeedSelectAll = hiSpeedInvalid = false;
            }
            if (EnterPressed() && CommitHiSpeed()) hiSpeedFocused = false;
        }

        private bool CommitHiSpeed()
        {
            string value = hiSpeedText.Length == 0 ? "0" : hiSpeedText;
            if (!float.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out float hs)
                || !float.IsFinite(hs) || hs < 0f || hs > 100f)
            {
                hiSpeedInvalid = true;
                return false;
            }
            if (ConfigManager.Game.HiSpeed != hs)
            {
                ConfigManager.Game.HiSpeed = hs;
                ConfigManager.SaveAll();
            }
            hiSpeedText = hs.ToString("0.#######", CultureInfo.InvariantCulture);
            hiSpeedInvalid = hiSpeedSelectAll = false;
            return true;
        }

        private void FinishHiSpeedInput()
        {
            if (hiSpeedFocused && !CommitHiSpeed()) ResetHiSpeedInput();
            hiSpeedFocused = false;
        }

        // ===== オートの連打の速さの箱(整数だけ) =====

        private void ResetAutoRollInput()
        {
            autoRollText = ConfigManager.Game.AutoRollPerSecond.ToString(CultureInfo.InvariantCulture);
            autoRollFocused = autoRollSelectAll = autoRollInvalid = false;
        }

        private void UpdateAutoRollInput()
        {
            bool control = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
            if (control && Raylib.IsKeyPressed(KeyboardKey.A)) autoRollSelectAll = true;
            int c;
            while ((c = Raylib.GetCharPressed()) > 0)
            {
                if (control || c < '0' || c > '9') continue;
                if (autoRollSelectAll) { autoRollText = ""; autoRollSelectAll = false; }
                if (autoRollText.Length < 4) autoRollText += (char)c;
                autoRollInvalid = false;
            }
            if (KeyPressed(KeyboardKey.Backspace))
            {
                if (autoRollSelectAll) autoRollText = "";
                else if (autoRollText.Length > 0) autoRollText = autoRollText[..^1];
                autoRollSelectAll = autoRollInvalid = false;
            }
            if (EnterPressed() && CommitAutoRoll()) autoRollFocused = false;
        }

        private bool CommitAutoRoll()
        {
            string value = autoRollText.Length == 0 ? "0" : autoRollText;
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int rate)
                || rate < GameConfig.AutoRollMin || rate > GameConfig.AutoRollMax)
            {
                autoRollInvalid = true;
                return false;
            }
            if (ConfigManager.Game.AutoRollPerSecond != rate)
            {
                ConfigManager.Game.AutoRollPerSecond = rate;
                ConfigManager.SaveAll();
            }
            autoRollText = rate.ToString(CultureInfo.InvariantCulture);
            autoRollInvalid = autoRollSelectAll = false;
            return true;
        }

        private void FinishAutoRollInput()
        {
            if (autoRollFocused && !CommitAutoRoll()) ResetAutoRollInput();
            autoRollFocused = false;
        }

        // ===== オートの精度の箱(良・可・不可。0〜100の整数) =====

        private void ResetPercentInputs()
        {
            foreach (PercentBox box in percentBoxes)
            {
                box.Text = box.Get().ToString(CultureInfo.InvariantCulture);
                box.Focused = box.SelectAll = box.Invalid = false;
            }
        }

        private void FinishPercentInputs()
        {
            foreach (PercentBox box in percentBoxes) FinishPercentInput(box);
        }

        private void FinishPercentInput(PercentBox box)
        {
            if (box.Focused && !CommitPercent(box))
            {
                box.Text = box.Get().ToString(CultureInfo.InvariantCulture);
                box.Invalid = false;
            }
            box.Focused = box.SelectAll = false;
        }

        private void UpdatePercentInput(PercentBox box)
        {
            bool control = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
            if (control && Raylib.IsKeyPressed(KeyboardKey.A)) box.SelectAll = true;
            int c;
            while ((c = Raylib.GetCharPressed()) > 0)
            {
                if (control || c < '0' || c > '9') continue;
                if (box.SelectAll) { box.Text = ""; box.SelectAll = false; }
                if (box.Text.Length < 3) box.Text += (char)c;
                box.Invalid = false;
            }
            if (KeyPressed(KeyboardKey.Backspace))
            {
                if (box.SelectAll) box.Text = "";
                else if (box.Text.Length > 0) box.Text = box.Text[..^1];
                box.SelectAll = box.Invalid = false;
            }
            if (EnterPressed() && CommitPercent(box)) box.Focused = false;
            // Tab：次の箱へ
            if (Raylib.IsKeyPressed(KeyboardKey.Tab) && CommitPercent(box))
            {
                int next = (Array.IndexOf(percentBoxes, box) + 1) % percentBoxes.Length;
                box.Focused = false;
                percentBoxes[next].Focused = percentBoxes[next].SelectAll = true;
                caretTimer = 0f;
            }
        }

        private bool CommitPercent(PercentBox box)
        {
            string value = box.Text.Length == 0 ? "0" : box.Text;
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int percent)
                || percent < GameConfig.AutoPercentMin || percent > GameConfig.AutoPercentMax)
            {
                box.Invalid = true;
                return false;
            }
            if (box.Get() != percent)
            {
                box.Set(percent);
                ConfigManager.SaveAll();
            }
            box.Text = percent.ToString(CultureInfo.InvariantCulture);
            box.Invalid = box.SelectAll = false;
            return true;
        }
    }
}
