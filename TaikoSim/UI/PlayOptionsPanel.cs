﻿using System.Globalization;
using System.Numerics;
using Raylib_cs;
using TaikoSim.Audio;
using TaikoSim.Configs;
using TaikoSim.Graphics;
using TaikoSim.Input;

namespace TaikoSim.UI
{
    // 演奏オプションの板(難易度選択の「オプション」・F2 で開く)
    //
    // 見た目：A-Style の 02.SongSelect/Option/ の画像で描く(無ければプログラムで同じ形を描く)
    //   Background.png(432×614 の板)・Option_Base.png(364×52 の項目の帯)・Option_Value_Base.png(値の箱)・
    //   Option_Value_Arrow.png(◀。右は左右反転)・PlayerSide.png(64×64 が横に3つ。左の 1P を選んでいる項目の横に出す)
    //   TaikøNauts の動画にこの板が映っていないので、置き場所は TaikoSim で決めた(左下。開いた形で x=20, y=450)
    //
    // 操作(OpenTaiko の CActPlayOption と同じ)：
    //   カッ・←→ … 選んでいる項目の値を変える
    //   ドン・Enter … 次の項目へ。最後の項目でドンを押すと閉じる(開ききるまではドンを受け付けない)
    //   ↑↓ … 項目を選ぶ(上と下はつながっている)
    //   数字キー … 数字の項目に直接入力(Enter で決定)
    //   Esc・F2 … 閉じる(SongSelectScene から呼ぶ)
    //   マウス … 項目をクリックで選ぶ、◀▶で値を変える、値の箱をクリックで ON/OFF・直接入力、板の外をクリックで閉じる
    //
    // 動き(OpenTaiko の CActPlayOption と同じ式。1段 6ms × 50段 = 0.3秒)：
    //   開く：下から上がってきて、少し行きすぎてから戻る。閉じる：少し上がってから、下へ落ちていく
    public sealed class PlayOptionsPanel
    {
        // ===== 置き場所(1920×1080) =====
        private const string Folder = "Image/02.SongSelect/Option/";
        private const float PanelX = 20f, PanelY = 450f, PanelWidth = 432f, PanelHeight = 614f;
        private const float RowX = 34f, RowTop = 84f, RowStep = 56f, RowWidth = 364f, RowHeight = 52f; // 板の左上から
        private static readonly Rectangle ValueBox = new Rectangle(196, 6, 134, 40);   // 項目の帯の左上から
        private static readonly Rectangle LeftArrow = new Rectangle(166, 10, 28, 32);
        private static readonly Rectangle RightArrow = new Rectangle(332, 10, 28, 32);

        private static readonly Color LabelColor = new Color(48, 48, 58, 255);
        private static readonly Color SelectedRowTint = new Color(255, 236, 110, 255);  // 選んでいる項目の帯(黄色)
        private static readonly Color ValueBoxTint = new Color(64, 72, 92, 255);
        private static readonly Color ErrorColor = new Color(255, 110, 100, 255);
        private static readonly Color DescColor = new Color(255, 255, 255, 235);

        // ===== 開く・閉じる動き =====
        private const float StepSeconds = 0.006f, Steps = 50f;
        private float openTime = -1f, closeTime = -1f;   // -1 なら動いていない

        public bool IsOpen { get; private set; }
        private bool Closing => closeTime >= 0f;
        private bool Opened => openTime >= StepSeconds * Steps;   // 開ききった(ドンを受け付ける)

        // 数字を入力中か(入力中は、ドン・カッ・Enter を入力の方に使う)
        public bool IsTyping => editRow >= 0;

        // ===== 項目 =====
        private enum Kind { Toggle, HiSpeed, AutoRoll, Percent }
        private sealed class Item
        {
            public string Label = "", Desc = "";
            public Kind Kind;
            public Func<bool> GetBool = () => false;
            public Action<bool> SetBool = _ => { };
            public Func<int> GetInt = () => 0;
            public Action<int> SetInt = _ => { };
        }

        private static GameConfig Cfg => ConfigManager.Game;

        private readonly Item[] items =
        {
            new Item { Label = "オート演奏", Desc = "オートで叩いてくれる", Kind = Kind.Toggle,
                GetBool = () => Cfg.AutoPlay, SetBool = v => Cfg.AutoPlay = v },
            new Item { Label = "HS倍率", Desc = "SCROLL に掛ける倍率。数字キーでも入力", Kind = Kind.HiSpeed },
            new Item { Label = "オート連打", Desc = "1秒に叩く数（0＝叩かない）", Kind = Kind.AutoRoll,
                GetInt = () => Cfg.AutoRollPerSecond, SetInt = v => Cfg.AutoRollPerSecond = v },
            new Item { Label = "精度：良", Desc = "オートの判定の割合（%）", Kind = Kind.Percent,
                GetInt = () => Cfg.AutoGoodPercent, SetInt = v => Cfg.AutoGoodPercent = v },
            new Item { Label = "精度：可", Desc = "オートの判定の割合（%）", Kind = Kind.Percent,
                GetInt = () => Cfg.AutoOkPercent, SetInt = v => Cfg.AutoOkPercent = v },
            new Item { Label = "精度：不可", Desc = "オートの判定の割合（%）", Kind = Kind.Percent,
                GetInt = () => Cfg.AutoBadPercent, SetInt = v => Cfg.AutoBadPercent = v },
            new Item { Label = "毎回同じ出方", Desc = "オートのずれ方を毎回同じに", Kind = Kind.Toggle,
                GetBool = () => Cfg.AutoFixedPattern, SetBool = v => Cfg.AutoFixedPattern = v },
            new Item { Label = "判定の板", Desc = "演奏中に良・可・不可の数を出す", Kind = Kind.Toggle,
                GetBool = () => Cfg.ShowScoreboard, SetBool = v => Cfg.ShowScoreboard = v },
            new Item { Label = "デバッグ記録", Desc = "重さ・入力を Logs に CSV で記録", Kind = Kind.Toggle,
                GetBool = () => Cfg.DebugLog, SetBool = v => Cfg.DebugLog = v },
        };

        // カッで変えるときの段階
        //   HS倍率は OpenTaiko と同じ(0.5, 1.0〜2.0 は 0.1 ずつ, 2.5〜4.0 は 0.5 ずつ)
        //   オート連打と精度は OpenTaiko に無いので、TaikoSim で決めた
        private static readonly float[] HiSpeedSteps =
            { 0.5f, 1.0f, 1.1f, 1.2f, 1.3f, 1.4f, 1.5f, 1.6f, 1.7f, 1.8f, 1.9f, 2.0f, 2.5f, 3.0f, 3.5f, 4.0f };
        private static readonly int[] AutoRollSteps = { 0, 5, 8, 10, 12, 15, 18, 20, 25, 30, 40, 50, 60, 80, 100 };
        private const int PercentStep = 5;

        private int cursor;            // 選んでいる項目
        private int editRow = -1;      // 数字を入力中の項目(-1 ならしていない)
        private string editText = "";
        private bool editSelectAll, editInvalid;
        private float caretTimer;

        // ===== 開く・閉じる =====

        public void Open()
        {
            if (IsOpen && !Closing) return;
            IsOpen = true;
            openTime = 0f;
            closeTime = -1f;
            cursor = 0;
            editRow = -1;
            caretTimer = 0f;
            MenuSounds.Option();
        }

        // 閉じる動きを始める(入力中の数字は決定する。だめな数字なら元に戻す)
        public void Close()
        {
            if (!IsOpen || Closing) return;
            FinishEdit();
            closeTime = 0f;
        }

        // すぐ閉じる(画面を出るとき・難易度の板を閉じるとき)
        public void CloseNow()
        {
            if (!IsOpen) return;
            FinishEdit();
            IsOpen = false;
            openTime = closeTime = -1f;
        }

        // ===== 毎フレーム(キーの入力) =====

        public void Update(float dt)
        {
            if (!IsOpen) return;
            float step = Math.Min(dt, 1f / 30f);
            caretTimer += dt;
            if (Closing)
            {
                closeTime += step;
                if (closeTime >= StepSeconds * Steps) { IsOpen = false; openTime = closeTime = -1f; }
                return;
            }
            openTime += step;
            if (TaikoSim.Game.IsSettingsOpen) { DiscardTypedChars(); return; }

            if (editRow >= 0) { UpdateEdit(); return; }

            // 数字キーを打ったら、数字の項目に直接入力を始める
            int c = Raylib.GetCharPressed();
            while (c > 0)
            {
                if (items[cursor].Kind != Kind.Toggle && (char.IsAsciiDigit((char)c) || (c == '.' && items[cursor].Kind == Kind.HiSpeed)))
                {
                    BeginEdit(cursor, selectAll: false);
                    editText = ((char)c).ToString();
                }
                c = Raylib.GetCharPressed();
            }
            if (editRow >= 0) return;

            bool left = InputManager.IsDrumPressed(DrumPart.LeftKa) || KeyPressed(KeyboardKey.Left);
            bool right = InputManager.IsDrumPressed(DrumPart.RightKa) || KeyPressed(KeyboardKey.Right);
            if (left) Change(cursor, -1);
            if (right) Change(cursor, +1);

            if (KeyPressed(KeyboardKey.Up)) { cursor = (cursor + items.Length - 1) % items.Length; MenuSounds.Ka(); }
            if (KeyPressed(KeyboardKey.Down)) { cursor = (cursor + 1) % items.Length; MenuSounds.Ka(); }

            bool decide = InputManager.IsDonPressed() || EnterPressed();
            if (decide && Opened)
            {
                MenuSounds.Don();
                if (cursor < items.Length - 1) cursor++;
                else Close();
            }
        }

        // 値を1段変える(dir：-1 で左、+1 で右)
        private void Change(int row, int dir)
        {
            Item item = items[row];
            switch (item.Kind)
            {
                case Kind.Toggle:
                    item.SetBool(!item.GetBool());
                    break;
                case Kind.HiSpeed:
                {
                    float now = Cfg.HiSpeed > 0f ? Cfg.HiSpeed : 1f; // 0 は「譜面どおり」＝1.0倍
                    float next = dir > 0
                        ? HiSpeedSteps.FirstOrDefault(s => s > now + 0.0001f, HiSpeedSteps[^1])
                        : HiSpeedSteps.LastOrDefault(s => s < now - 0.0001f, HiSpeedSteps[0]);
                    Cfg.HiSpeed = next;
                    break;
                }
                case Kind.AutoRoll:
                {
                    int now = item.GetInt();
                    int next = dir > 0
                        ? AutoRollSteps.FirstOrDefault(s => s > now, AutoRollSteps[^1])
                        : AutoRollSteps.LastOrDefault(s => s < now, AutoRollSteps[0]);
                    item.SetInt(Math.Clamp(next, GameConfig.AutoRollMin, GameConfig.AutoRollMax));
                    break;
                }
                case Kind.Percent:
                {
                    int now = item.GetInt();
                    // 5 の倍数にそろえながら動かす(7 → 右で 10、左で 5)
                    int next = dir > 0 ? (now / PercentStep + 1) * PercentStep : ((now + PercentStep - 1) / PercentStep - 1) * PercentStep;
                    item.SetInt(Math.Clamp(next, GameConfig.AutoPercentMin, GameConfig.AutoPercentMax));
                    break;
                }
            }
            ConfigManager.SaveAll();
            MenuSounds.Ka();
        }

        // ===== 数字の直接入力 =====

        private void BeginEdit(int row, bool selectAll)
        {
            editRow = row;
            editText = ValueText(items[row]).TrimStart('×').Replace("%", "");
            editSelectAll = selectAll;
            editInvalid = false;
            caretTimer = 0f;
        }

        private void UpdateEdit()
        {
            Item item = items[editRow];
            int c;
            while ((c = Raylib.GetCharPressed()) > 0)
            {
                bool ok = char.IsAsciiDigit((char)c) || (c == '.' && item.Kind == Kind.HiSpeed);
                if (!ok) continue;
                if (editSelectAll) { editText = ""; editSelectAll = false; }
                if (editText.Length < 8) editText += (char)c;
                editInvalid = false;
            }
            if (KeyPressed(KeyboardKey.Backspace))
            {
                if (editSelectAll) editText = "";
                else if (editText.Length > 0) editText = editText[..^1];
                editSelectAll = editInvalid = false;
            }
            if (EnterPressed() && CommitEdit()) editRow = -1;
        }

        // 入力した数字を設定に入れる。だめな数字なら false(赤く出す)
        private bool CommitEdit()
        {
            if (editRow < 0) return true;
            Item item = items[editRow];
            string text = editText.Length == 0 ? "0" : editText;
            switch (item.Kind)
            {
                case Kind.HiSpeed:
                    if (!float.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out float hs)
                        || !float.IsFinite(hs) || hs < 0f || hs > 100f) { editInvalid = true; return false; }
                    Cfg.HiSpeed = hs;
                    break;
                case Kind.AutoRoll:
                    if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int rate)
                        || rate < GameConfig.AutoRollMin || rate > GameConfig.AutoRollMax) { editInvalid = true; return false; }
                    item.SetInt(rate);
                    break;
                case Kind.Percent:
                    if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int percent)
                        || percent < GameConfig.AutoPercentMin || percent > GameConfig.AutoPercentMax) { editInvalid = true; return false; }
                    item.SetInt(percent);
                    break;
            }
            ConfigManager.SaveAll();
            editInvalid = false;
            return true;
        }

        private void FinishEdit()
        {
            if (editRow >= 0) CommitEdit(); // だめな数字なら、設定は前のまま
            editRow = -1;
        }

        private static string ValueText(Item item) => item.Kind switch
        {
            Kind.Toggle => item.GetBool() ? "ON" : "OFF",
            Kind.HiSpeed => "×" + (Cfg.HiSpeed > 0f ? Cfg.HiSpeed : 1f).ToString("0.0##", CultureInfo.InvariantCulture),
            Kind.Percent => item.GetInt().ToString(CultureInfo.InvariantCulture) + "%",
            _ => item.GetInt().ToString(CultureInfo.InvariantCulture),
        };

        private static string ErrorText(Item item) => item.Kind switch
        {
            Kind.HiSpeed => "0〜100 の数字を入れてね",
            Kind.AutoRoll => "0〜1000 の整数を入れてね",
            _ => "0〜100 の整数を入れてね",
        };

        // ===== 描く(マウスの操作もここで受け取る) =====

        // OpenTaiko の開く・閉じる動き(板の y のずれ)
        private float SlideY()
        {
            float y = 0f;
            if (openTime >= 0f)
            {
                float ct = Math.Min(openTime / StepSeconds, Steps);
                y += ct < 30f ? 410f - ct * 18f : -80f + (ct - 30f) * 4f; // 下から上がって、少し行きすぎて戻る
            }
            if (closeTime >= 0f)
            {
                float ct = Math.Min(closeTime / StepSeconds, Steps);
                y += ct < 20f ? -ct * 3f : 20f + (ct - 20f) * 16f;          // 少し上がってから、下へ落ちる
            }
            return y;
        }

        public void Draw(bool active)
        {
            if (!IsOpen) return;
            float px = PanelX, py = PanelY + SlideY();
            bool input = active && !Closing;
            Vector2 mouse = Screen.GetMousePosition();
            bool pressed = input && Raylib.IsMouseButtonPressed(MouseButton.Left);

            // ----- 板 -----
            Rectangle panel = new Rectangle(px, py, PanelWidth, PanelHeight);
            if (!MenuSkin.Whole(Folder + "Background.png", panel, Color.White))
            {
                Raylib.DrawRectangleRounded(panel, 0.06f, 8, new Color(20, 24, 34, 245));
                Raylib.DrawRectangleRoundedLinesEx(panel, 0.06f, 8, 3f, Gui.AccentColor);
            }

            // 題と、選んでいる項目の説明(入力がだめなときは赤で)
            FontManager.DrawOutlined("演奏オプション", px + (PanelWidth - FontManager.Measure("演奏オプション", 30).X) / 2f, py + 20,
                30, Color.White, new Color(30, 30, 40, 255), 4f);
            bool error = editRow >= 0 && editInvalid;
            string desc = error ? ErrorText(items[editRow]) : items[cursor].Desc;
            float descSize = 17f;
            FontManager.DrawOutlined(desc, px + (PanelWidth - FontManager.Measure(desc, descSize).X) / 2f, py + 58,
                descSize, error ? ErrorColor : DescColor, new Color(30, 30, 40, 200), 3f);

            // ----- 項目 -----
            for (int i = 0; i < items.Length; i++)
            {
                Item item = items[i];
                Rectangle row = new Rectangle(px + RowX, py + RowTop + i * RowStep, RowWidth, RowHeight);
                bool selected = i == cursor;
                Color rowTint = selected ? SelectedRowTint : Color.White;
                if (!MenuSkin.Whole(Folder + "Option_Base.png", row, rowTint))
                    Raylib.DrawRectangleRounded(row, 0.3f, 8, rowTint);

                Vector2 label = FontManager.Measure(item.Label, 22);
                FontManager.Draw(item.Label, row.X + 18, row.Y + (RowHeight - label.Y) / 2f, 22, LabelColor);

                Rectangle box = Offset(ValueBox, row);
                if (!MenuSkin.NPatch(Folder + "Option_Value_Base.png", box, 20, ValueBoxTint))
                    Raylib.DrawRectangleRounded(box, 1f, 12, ValueBoxTint);

                bool editing = editRow == i;
                if (editing)
                {
                    Raylib.DrawRectangleRoundedLinesEx(box, 1f, 12, 2f, editInvalid ? ErrorColor : Gui.AccentColor);
                    Vector2 size = FontManager.Measure(editText, 24);
                    float tx = box.X + (box.Width - size.X) / 2f, ty = box.Y + (box.Height - size.Y) / 2f;
                    if (editSelectAll && editText.Length > 0)
                        Raylib.DrawRectangleRec(new Rectangle(tx - 3, ty, size.X + 6, size.Y), new Color(45, 82, 130, 255));
                    FontManager.Draw(editText, tx, ty, 24, Color.White);
                    if (!editSelectAll && caretTimer % 1f < 0.5f) FontManager.Draw("|", tx + size.X + 1, ty, 24, Color.White);
                }
                else
                {
                    string value = ValueText(item);
                    Color color = item.Kind == Kind.Toggle && !item.GetBool() ? Gui.SubTextColor : Color.White;
                    Gui.DrawCentered(value, box, 24, color);
                }

                // 選んでいる項目：左に 1P の印、値の両わきに◀▶
                if (selected)
                {
                    Rectangle mark = new Rectangle(row.X - 44, row.Y + 2, 48, 48);
                    if (!MenuSkin.Part(Folder + "PlayerSide.png", new Rectangle(0, 0, 64, 64), mark, Color.White))
                    {
                        Raylib.DrawCircleV(new Vector2(mark.X + 24, mark.Y + 24), 22, new Color(232, 72, 58, 255));
                        Gui.DrawCentered("1P", mark, 20, Color.White);
                    }
                    DrawArrow(Offset(LeftArrow, row), flip: false);
                    DrawArrow(Offset(RightArrow, row), flip: true);
                }

                // ----- マウス -----
                if (!pressed || editRow >= 0 && editRow != i) continue;
                if (selected && Raylib.CheckCollisionPointRec(mouse, Offset(LeftArrow, row))) Change(i, -1);
                else if (selected && Raylib.CheckCollisionPointRec(mouse, Offset(RightArrow, row))) Change(i, +1);
                else if (Raylib.CheckCollisionPointRec(mouse, box))
                {
                    cursor = i;
                    if (item.Kind == Kind.Toggle) Change(i, +1);
                    else if (!editing) BeginEdit(i, selectAll: true);
                }
                else if (Raylib.CheckCollisionPointRec(mouse, row) && !selected)
                {
                    FinishEdit();
                    cursor = i;
                    MenuSounds.Ka();
                }
            }

            // 入力中に、ほかの場所をクリックしたら決定する
            if (pressed && editRow >= 0 && !Raylib.CheckCollisionPointRec(mouse, Offset(ValueBox, RowArea(editRow, px, py))))
                FinishEdit();

            // 板の外をクリックしたら閉じる
            if (pressed && !Raylib.CheckCollisionPointRec(mouse, panel)) Close();
        }

        private static Rectangle RowArea(int i, float px, float py) =>
            new Rectangle(px + RowX, py + RowTop + i * RowStep, RowWidth, RowHeight);

        private static Rectangle Offset(Rectangle r, Rectangle row) => new Rectangle(row.X + r.X, row.Y + r.Y, r.Width, r.Height);

        private static void DrawArrow(Rectangle area, bool flip)
        {
            if (SkinManager.GetTexture(Folder + "Option_Value_Arrow.png") is Texture2D tex)
            {
                Rectangle source = new Rectangle(0, 0, flip ? -tex.Width : tex.Width, tex.Height); // 幅をマイナスにすると左右反転
                Raylib.DrawTexturePro(tex, source, area, Vector2.Zero, 0f, Color.White);
                return;
            }
            float cx = area.X + area.Width / 2f, cy = area.Y + area.Height / 2f;
            Color c = new Color(60, 170, 230, 255);
            if (flip) Raylib.DrawTriangle(new Vector2(cx + 9, cy), new Vector2(cx - 7, cy - 11), new Vector2(cx - 7, cy + 11), c);
            else Raylib.DrawTriangle(new Vector2(cx - 9, cy), new Vector2(cx + 7, cy + 11), new Vector2(cx + 7, cy - 11), c);
        }

        private static bool KeyPressed(KeyboardKey key) => Raylib.IsKeyPressed(key) || Raylib.IsKeyPressedRepeat(key);

        private static bool EnterPressed() => Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter);

        private static void DiscardTypedChars()
        {
            while (Raylib.GetCharPressed() > 0) { }
        }
    }
}
