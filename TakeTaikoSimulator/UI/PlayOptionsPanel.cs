using System.Globalization;
using System.Numerics;
using Raylib_cs;
using TakeTaikoSimulator.Audio;
using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Graphics;
using TakeTaikoSimulator.Input;

namespace TakeTaikoSimulator.UI
{
    // 演奏オプションの板(難易度選択の「オプション」・F2 で開く)
    //
    // 見た目：A-Style の 02.SongSelect/Option/ の画像で描く(無ければプログラムで同じ形を描く)
    //   TaikøNauts の動画(10/18 の 2026-10-18_00-27-01.mp4 の 50〜58秒)を 1/60秒ずつ測った：
    //   ・板(Background.png 432×614)は原寸で (0, 465)。8項目ぶんの大きさ。TakeTaikoSimulator の項目の数に合わせて、1項目 61px ずつ縦に伸ばす・縮める(下の端はそのまま)
    //   ・左上に 1P の印(PlayerSide.png の左の 64×64 を 54px で (36, 28))、その右に「演奏オプション」(白い字に黒い縁)
    //   ・項目の帯(Option_Base.png 364×52)は板の (32, 94) から 61px ずつ。ふだんは白を半分だけ重ねる(うすい黄緑に見える)、選んでいる帯はこい黄色 (254,255,0)
    //   ・項目の名前は白い字に黒い縁(帯の左から 11px)。値は白い箱(Option_Value_Base.png 180×40 を帯の (178, 6))に、黒っぽい字に白い縁
    //   ・選んでいる帯は値の箱を出さずに、◀ 値 ▶(矢印は帯の (186, 10) と (326, 10))
    //   ・ON/OFF は「する」「しない」
    //
    // 操作(OpenTaiko の CActPlayOption と同じ)：
    //   カッ・←→ … 選んでいる項目の値を変える
    //   ドン・Enter … 次の項目へ。最後の項目でドンを押すと閉じる(開ききるまではドンを受け付けない)
    //   ↑↓ … 項目を選ぶ(上と下はつながっている)
    //   数字キー … 数字の項目に直接入力(Enter で決定)
    //   Esc・F2 … 閉じる(SongSelectScene から呼ぶ)
    //   マウス … 項目をクリックで選ぶ、◀▶で値を変える、値の箱をクリックで ON/OFF・直接入力、板の外をクリックで閉じる
    //
    // 動き(TaikøNauts の動画を測った。OpenTaiko の式とは少しちがった)：
    //   開く：画面の下から 1秒に 3000px で上がってきて、60px 行きすぎたところ(0.22秒くらい)から 0.07秒で戻る
    //   閉じる：0.04秒で 54px 上がって、0.04秒で戻り、そこから 1秒に 3100px で下へ落ちていく
    public sealed class PlayOptionsPanel
    {
        // ===== 置き場所(1920×1080) =====
        private const string Folder = "Image/02.SongSelect/Option/";
        private const float PanelX = 0f, PanelWidth = 432f;
        private const float BaseTop = 465f, BaseHeight = 614f, BaseRows = 8f;   // TaikøNauts の板(8項目)
        private const float RowX = 32f, RowTop = 94f, RowStep = 61f, RowWidth = 364f, RowHeight = 52f; // 板の左上から
        private static readonly Rectangle ValueBox = new Rectangle(178, 6, 180, 40);   // 項目の帯の左上から
        private static readonly Rectangle LeftArrow = new Rectangle(186, 10, 28, 32);
        private static readonly Rectangle RightArrow = new Rectangle(326, 10, 28, 32);
        private static readonly Rectangle SideMark = new Rectangle(36, 28, 54, 54);     // 板の左上から
        private const float TitleX = 105f, TitleCenterY = 55f, TitleSize = 32f;
        private const float LabelSize = 26f, ValueSize = 26f;

        // 項目の数に合わせて、板を縦に伸ばす(下の端はそのまま、上へ伸ばす)
        //   11/12 たけらい「オプション画面は8まで表示だからそれ以上あったら次に行くように」
        //   → 8項目までは今までどおり。9項目から先は次のページ（板は8項目ぶんの大きさのまま。右上に「1/2」）
        private const int PageRows = 8;
        private int VisibleRows => Math.Min(items.Length, PageRows);
        private int PageCount => (items.Length + PageRows - 1) / PageRows;
        private int Page => cursor / PageRows;
        private float PanelHeight => BaseHeight + (VisibleRows - BaseRows) * RowStep;
        private float PanelTop => BaseTop - (VisibleRows - BaseRows) * RowStep;

        private static readonly Color Ink = new Color(20, 20, 24, 255);
        private static readonly Color ValueInk = new Color(16, 16, 16, 255);   // 値の字(ほぼ黒。薄く見えないように)
        private static readonly Color SelectedRowTint = new Color(254, 255, 0, 255);  // 選んでいる項目の帯(こい黄色)
        private static readonly Color RowTint = new Color(255, 255, 255, 128);        // ふだんの帯(白を半分)
        private static readonly Color ErrorColor = new Color(255, 70, 60, 255);

        // ===== 開く・閉じる動き(秒・px) =====
        private const float RiseSpeed = 3000f, Overshoot = 60f, SettleSeconds = 0.07f;
        private const float CloseUp = 54f, CloseUpSeconds = 0.04f, FallSpeed = 3100f;
        private float openTime = -1f, closeTime = -1f;   // -1 なら動いていない

        public bool IsOpen { get; private set; }
        private bool Closing => closeTime >= 0f;
        private float RiseSeconds => (Screen.Height - PanelTop + Overshoot) / RiseSpeed;     // 画面の下から、行きすぎるところまで
        private bool Opened => openTime >= RiseSeconds + SettleSeconds;                      // 開ききった(ドンを受け付ける)
        private bool CloseDone => closeTime >= CloseUpSeconds * 2f + (Screen.Height - PanelTop) / FallSpeed;

        // 入力した数字がだめなときの説明(選曲画面の一番下のヒントに出す)。だめでなければ null
        public string? ErrorMessage => editRow >= 0 && editInvalid ? ErrorText(items[editRow]) : null;

        // 数字を入力中か(入力中は、ドン・カッ・Enter を入力の方に使う)
        public bool IsTyping => editRow >= 0;

        // ===== 項目 =====
        //   Choice：決まった選択肢から選ぶ（GetInt / SetInt が何番目か）。Disabled：まだ作っていない項目（灰色で、選べない）
        private enum Kind { Toggle, HiSpeed, AutoRoll, Percent, HitSound, Choice, Disabled }
        private sealed class Item
        {
            public string Label = "";
            public Kind Kind;
            public Func<bool> GetBool = () => false;
            public Action<bool> SetBool = _ => { };
            public Func<int> GetInt = () => 0;
            public Action<int> SetInt = _ => { };
            public string[] Choices = Array.Empty<string>();
            public bool Wrap = true;   // Choice で、端まで行ったら反対の端に回るか
        }

        private static GameConfig Cfg => TakeTaikoSimulator.Scenes.LocalSession.OptionsContext ?? ConfigManager.Game;

        // 変えた値を保存する
        //   1人プレイ：いつもの設定に保存（今遊んでいる人のデータにも書かれる）
        //   2人プレイ：その人のデータの設定にだけ保存（11/12 たけらい「プレイヤーデータごとに設定」）
        private static void SaveOptions()
        {
            if (TakeTaikoSimulator.Scenes.LocalSession.OptionsContext is not GameConfig own) { ConfigManager.SaveAll(); return; }
            int side = TakeTaikoSimulator.Scenes.LocalSession.MenuPlayer;
            PlayerSettings.Save(TakeTaikoSimulator.Scenes.LocalSession.Profiles[side], own);
        }

        // 11/12 たけらい：並びは TaikøNauts の板に合わせた（1ページ目 8個、2ページ目 4個）
        //   ・演奏スキップ：素材がまだ無いので、行だけ出して灰色（選べない）
        //   ・音符位置調整：オフセットのことなので出さない（設定画面にある）
        //   ・オート連打・毎回同じ出方：TaikøNauts には無い、TakeTaikoSimulator の項目。2ページ目の後ろに置く
        private static readonly string[] PlaySpeedTexts =
            GameConfig.PlaySpeeds.Select(s => s.ToString("0.0", CultureInfo.InvariantCulture)).ToArray();
        private readonly Item[] items =
        {
            // ----- 1ページ目 -----
            new Item { Label = "はやさ", Kind = Kind.HiSpeed },  // TJA の SCROLL に掛ける倍率(本家の「はやさ」)
            new Item { Label = "ドロン", Kind = Kind.Toggle,     // 音符の絵を消す（下の文字だけ流れる）
                GetBool = () => Cfg.Doron, SetBool = v => Cfg.Doron = v },
            new Item { Label = "あべこべ", Kind = Kind.Toggle,   // ドン⇔カッ を全部入れかえる
                GetBool = () => Cfg.Abekobe, SetBool = v => Cfg.Abekobe = v },
            new Item { Label = "ランダム", Kind = Kind.Choice, Choices = new[] { "しない", "きまぐれ", "でたらめ" },
                GetInt = () => (int)Cfg.Random, SetInt = v => Cfg.Random = (NoteRandom)v },
            new Item { Label = "演奏スキップ", Kind = Kind.Disabled },
            new Item { Label = "ボイス", Kind = Kind.Toggle,     // 演奏中のコンボの声
                GetBool = () => Cfg.ComboVoice, SetBool = v => Cfg.ComboVoice = v },
            new Item { Label = "音色", Kind = Kind.HitSound },  // ドン・カッの音(Audio/HitSounds.cs。本家の「音色」)
            new Item { Label = "オートプレイ", Kind = Kind.Toggle,
                GetBool = () => Cfg.AutoPlay, SetBool = v => Cfg.AutoPlay = v },
            // ----- 2ページ目 -----
            new Item { Label = "演奏速度", Kind = Kind.Choice, Choices = PlaySpeedTexts, Wrap = false,   // 曲そのものの速さ
                GetInt = () => Math.Max(0, Array.FindIndex(GameConfig.PlaySpeeds, s => Math.Abs(s - Cfg.PlaySpeed) < 0.001f)),
                SetInt = v => Cfg.PlaySpeed = GameConfig.PlaySpeeds[v] },
            new Item { Label = "演奏情報表示", Kind = Kind.Toggle,   // 良・可・不可・連打数の板（前の名前は「判定の板」）
                GetBool = () => Cfg.ShowScoreboard, SetBool = v => Cfg.ShowScoreboard = v },
            new Item { Label = "オート連打", Kind = Kind.AutoRoll,
                GetInt = () => Cfg.AutoRollPerSecond, SetInt = v => Cfg.AutoRollPerSecond = v },
            new Item { Label = "毎回同じ出方", Kind = Kind.Toggle,
                GetBool = () => Cfg.AutoFixedPattern, SetBool = v => Cfg.AutoFixedPattern = v },
        };

        private static bool NoTyping(Kind kind) => kind is Kind.Toggle or Kind.HitSound or Kind.Choice or Kind.Disabled;

        // 選べる項目まで、dir の向きに進む（灰色の項目は飛ばす。上と下はつながっている）
        private int NextRow(int from, int dir)
        {
            int row = from;
            for (int i = 0; i < items.Length; i++)
            {
                row = (row + dir + items.Length) % items.Length;
                if (items[row].Kind != Kind.Disabled) return row;
            }
            return from;
        }

        // カッで変えるときの段階
        //   HS倍率は OpenTaiko と同じ(0.5, 1.0〜2.0 は 0.1 ずつ, 2.5〜4.0 は 0.5 ずつ)
        //   オート連打と精度は OpenTaiko に無いので、TakeTaikoSimulator で決めた
        internal static readonly float[] HiSpeedSteps =
            { 0.5f, 1.0f, 1.1f, 1.2f, 1.3f, 1.4f, 1.5f, 1.6f, 1.7f, 1.8f, 1.9f, 2.0f, 2.5f, 3.0f, 3.5f, 4.0f };
        private static readonly int[] AutoRollSteps = { 0, 5, 8, 10, 12, 15, 18, 20, 25, 30, 40, 50, 60, 80, 100 };
        private const int PercentStep = 5;

        private int cursor;            // 選んでいる項目
        private float autoNoticeAge = -1f;   // 「オートプレイ中は記録されません」を出してからの秒（-1 なら出していない）
        private const float AutoNoticeSeconds = 4f;
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
            autoNoticeAge = -1f;
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
            if (autoNoticeAge >= 0f) autoNoticeAge += dt;
            HitSounds.Update(dt); // 音色の試し聞きのカッ
            if (Closing)
            {
                closeTime += step;
                if (CloseDone) { IsOpen = false; openTime = closeTime = -1f; }
                return;
            }
            openTime += step;
            if (TakeTaikoSimulator.Game.IsSettingsOpen) { DiscardTypedChars(); return; }

            if (editRow >= 0) { UpdateEdit(); return; }

            // 数字キーを打ったら、数字の項目に直接入力を始める
            int c = Raylib.GetCharPressed();
            while (c > 0)
            {
                if (!NoTyping(items[cursor].Kind) && (char.IsAsciiDigit((char)c) || (c == '.' && items[cursor].Kind == Kind.HiSpeed)))
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

            if (KeyPressed(KeyboardKey.Up)) { cursor = NextRow(cursor, -1); MenuSounds.Ka(); }
            if (KeyPressed(KeyboardKey.Down)) { cursor = NextRow(cursor, +1); MenuSounds.Ka(); }
            // PageUp・PageDown：ページを変える
            if (PageCount > 1 && (KeyPressed(KeyboardKey.PageDown) || KeyPressed(KeyboardKey.PageUp)))
            {
                int page = (Page + (KeyPressed(KeyboardKey.PageDown) ? 1 : PageCount - 1)) % PageCount;
                cursor = NextRow(page * PageRows - 1 < 0 ? items.Length - 1 : page * PageRows - 1, +1);
                MenuSounds.Ka();
            }

            bool decide = InputManager.IsDonPressed() || EnterPressed();
            if (decide && Opened)
            {
                MenuSounds.Don();
                // 次の選べる項目へ（ページの最後なら次のページへ）。もう後ろに無ければ閉じる
                int next = NextRow(cursor, +1);
                if (next > cursor) cursor = next;
                else Close();
            }
        }

        // 値を1段変える(dir：-1 で左、+1 で右)
        private void Change(int row, int dir)
        {
            Item item = items[row];
            bool autoBefore = Cfg.AutoPlay;
            switch (item.Kind)
            {
                case Kind.Disabled:
                    return;
                case Kind.Toggle:
                    item.SetBool(!item.GetBool());
                    break;
                case Kind.Choice:
                {
                    int count = item.Choices.Length, now = item.GetInt();
                    int next = item.Wrap ? ((now + dir) % count + count) % count : Math.Clamp(now + dir, 0, count - 1);
                    if (next == now) return;
                    item.SetInt(next);
                    break;
                }
                case Kind.HitSound:
                {
                    // 一覧をぐるっと回る。変えたら「ドン → カッ」を1回ずつ鳴らす(カッの音は鳴らさない)
                    var list = HitSounds.List;
                    int next = ((HitSounds.List.FindIndex(e => e.Key == Cfg.HitSound) + dir) % list.Count + list.Count) % list.Count;
                    Cfg.HitSound = list[next].Key;
                    SaveOptions();
                    ConfigManager.Game.HitSound = Cfg.HitSound;
                    HitSounds.PlayPreview();
                    return;
                }
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
            // オートプレイを「する」にした：記録が残らないことを、板の右に帯で知らせる（UI/InfoBar.cs）
            if (Cfg.AutoPlay && !autoBefore) autoNoticeAge = 0f;
            else if (!Cfg.AutoPlay) autoNoticeAge = -1f;
            SaveOptions();
            MenuSounds.Ka();
        }

        // ===== 数字の直接入力 =====

        private void BeginEdit(int row, bool selectAll)
        {
            editRow = row;
            editText = ValueText(items[row]).Replace("%", "");
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
            SaveOptions();
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
            Kind.Toggle => item.GetBool() ? "する" : "しない",
            Kind.Choice => item.Choices[Math.Clamp(item.GetInt(), 0, item.Choices.Length - 1)],
            Kind.Disabled => "しない",
            Kind.HitSound => HitSounds.List.FirstOrDefault(e => e.Key == Cfg.HitSound)?.Name ?? "スキン",
            Kind.HiSpeed => (Cfg.HiSpeed > 0f ? Cfg.HiSpeed : 1f).ToString("0.0##", CultureInfo.InvariantCulture),
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

        // 開く・閉じる動き(板の y のずれ。TaikøNauts の動画を測った)
        private float SlideY()
        {
            float y = 0f;
            if (openTime >= 0f)
            {
                float start = Screen.Height - PanelTop; // 画面の下にちょうど隠れる場所から
                if (openTime < RiseSeconds) y = start - RiseSpeed * openTime;   // 一定の速さで上がる
                else
                {
                    float u = Math.Clamp((openTime - RiseSeconds) / SettleSeconds, 0f, 1f);
                    y = -Overshoot * (1f - u) * (1f - u);                       // 行きすぎた分を、ゆっくり戻す
                }
            }
            if (closeTime >= 0f)
            {
                float t = closeTime;
                if (t < CloseUpSeconds) { float u = t / CloseUpSeconds; y = -CloseUp * (1f - (1f - u) * (1f - u)); }      // すっと上がる
                else if (t < CloseUpSeconds * 2f) { float u = (t - CloseUpSeconds) / CloseUpSeconds; y = -CloseUp * (1f - u * u); } // 戻る
                else y = FallSpeed * (t - CloseUpSeconds * 2f);                                                        // 下へ落ちる
            }
            return y;
        }

        public void Draw(bool active)
        {
            if (!IsOpen) return;
            float px = PanelX, py = PanelTop + SlideY();
            // 開いたクリックを「板の外を押した」と二重処理しない。移動中の板もクリックさせない。
            bool input = active && !Closing && Opened;
            Vector2 mouse = Screen.GetMousePosition();
            bool pressed = input && Raylib.IsMouseButtonPressed(MouseButton.Left);

            // ----- 板(項目の数に合わせて縦に伸ばす。角や縁がゆがまないように9つに切って伸ばす) -----
            Rectangle panel = new Rectangle(px, py, PanelWidth, PanelHeight);
            if (!MenuSkin.NPatch(Folder + "Background.png", panel, 60, Color.White))
            {
                SmoothShapes.Rounded(panel, 0.06f, 8, new Color(20, 24, 34, 245));
                SmoothShapes.RoundedLines(panel, 0.06f, 8, 3f, Gui.AccentColor);
            }

            // ----- 左上の 1P の印と「演奏オプション」 -----
            Rectangle mark = Offset(SideMark, panel);
            int side = TakeTaikoSimulator.Scenes.LocalSession.MenuPlayer;
            if (!MenuSkin.Part(Folder + "PlayerSide.png", new Rectangle(side * 64, 0, 64, 64), mark, Color.White))
            {
                SmoothShapes.Circle(new Vector2(mark.X + mark.Width / 2f, mark.Y + mark.Height / 2f), mark.Width / 2f - 2f, side == 0 ? new Color(232, 72, 58, 255) : Color.SkyBlue);
                Gui.DrawCentered($"{side + 1}P", mark, 22, Color.White);
            }
            Outlined("演奏オプション", px + TitleX, py + TitleCenterY, TitleSize, Color.White, Ink, centerX: false);
            // 2ページ以上あるときは、右上に「1/2」
            if (PageCount > 1)
                Outlined($"{Page + 1}/{PageCount}", px + PanelWidth - 62f, py + TitleCenterY, 24f, Color.White, Ink, centerX: true);

            // ----- 項目（今のページの分だけ） -----
            int first = Page * PageRows, last = Math.Min(items.Length, first + PageRows);
            for (int i = first; i < last; i++)
            {
                Item item = items[i];
                Rectangle row = RowArea(i, px, py);
                bool selected = i == cursor;
                bool editing = editRow == i;
                if (item.Kind == Kind.Disabled)
                {
                    // まだ作っていない項目：灰色で出すだけ（TaikøNauts の、選べない項目と同じ見た目）
                    Color gray = new Color(110, 116, 104, 150);
                    if (!MenuSkin.Whole(Folder + "Option_Base.png", row, gray)) SmoothShapes.Rounded(row, 0.3f, 8, gray);
                    Outlined(item.Label, row.X + 11, row.Y + RowHeight / 2f, LabelSize, new Color(150, 150, 150, 255), new Color(70, 70, 70, 255), centerX: false);
                    Rectangle grayBox = Offset(ValueBox, row);
                    if (!MenuSkin.NPatch(Folder + "Option_Value_Base.png", grayBox, 20, new Color(120, 120, 120, 255)))
                        SmoothShapes.Rounded(grayBox, 1f, 12, new Color(120, 120, 120, 255));
                    Outlined(ValueText(item), grayBox.X + grayBox.Width / 2f, row.Y + RowHeight / 2f, ValueSize, new Color(70, 70, 70, 255), new Color(150, 150, 150, 255), centerX: true);
                    continue;
                }
                Color rowTint = selected ? SelectedRowTint : RowTint;
                if (!MenuSkin.Whole(Folder + "Option_Base.png", row, rowTint))
                    SmoothShapes.Rounded(row, 0.3f, 8, rowTint);

                // 項目の名前(白い字に黒い縁)
                Outlined(item.Label, row.X + 11, row.Y + RowHeight / 2f, LabelSize, Color.White, Ink, centerX: false);

                // 値：選んでいる帯は ◀ 値 ▶、ほかは白い箱の中(入力中は選んでいても箱を出す)
                Rectangle box = Offset(ValueBox, row);
                string value = editing ? editText : ValueText(item);
                if (!selected || editing)
                {
                    if (!MenuSkin.NPatch(Folder + "Option_Value_Base.png", box, 20, Color.White))
                        SmoothShapes.Rounded(box, 1f, 12, Color.White);
                }
                if (editing)
                {
                    SmoothShapes.RoundedLines(box, 1f, 12, 3f, editInvalid ? ErrorColor : Gui.AccentColor);
                    Vector2 size = MeasureSkin(value, ValueSize);
                    float cx = box.X + box.Width / 2f, cy = box.Y + box.Height / 2f;
                    if (editSelectAll && value.Length > 0)
                        Raylib.DrawRectangleRec(new Rectangle(cx - size.X / 2f - 3, cy - size.Y / 2f, size.X + 6, size.Y), new Color(120, 170, 255, 255));
                    Outlined(value, cx, cy, ValueSize, ValueInk, Color.White, centerX: true);
                    if (!editSelectAll && caretTimer % 1f < 0.5f) Outlined("|", cx + size.X / 2f + 4, cy, ValueSize, ValueInk, Color.White, centerX: false);
                }
                else
                {
                    float cx = selected ? row.X + (LeftArrow.X + LeftArrow.Width + RightArrow.X) / 2f : box.X + box.Width / 2f;
                    Outlined(value, cx, row.Y + RowHeight / 2f, ValueSize, ValueInk, Color.White, centerX: true);
                }
                if (selected && !editing)
                {
                    DrawArrow(Offset(LeftArrow, row), flip: false);
                    DrawArrow(Offset(RightArrow, row), flip: true);
                }

                // ----- マウス -----
                if (!pressed || editRow >= 0 && editRow != i) continue;
                if (selected && !editing && Raylib.CheckCollisionPointRec(mouse, Offset(LeftArrow, row))) Change(i, -1);
                else if (selected && !editing && Raylib.CheckCollisionPointRec(mouse, Offset(RightArrow, row))) Change(i, +1);
                else if (Raylib.CheckCollisionPointRec(mouse, box))
                {
                    cursor = i;
                    if (NoTyping(item.Kind)) Change(i, +1);
                    else if (!editing) BeginEdit(i, selectAll: true);
                }
                else if (Raylib.CheckCollisionPointRec(mouse, row) && !selected)
                {
                    FinishEdit();
                    cursor = i;
                    MenuSounds.Ka();
                }
            }

            // オートプレイを「する」にしたときのお知らせ（板の右どなり・画面の下の方）
            InfoBar.Draw(PanelWidth + 4f, 950f, InfoBar.AutoOnText, InfoBar.AlphaAt(autoNoticeAge, AutoNoticeSeconds));

            // 入力中に、ほかの場所をクリックしたら決定する
            if (pressed && editRow >= 0 && !Raylib.CheckCollisionPointRec(mouse, Offset(ValueBox, RowArea(editRow, px, py))))
                FinishEdit();

            // 板の外をクリックしたら閉じる
            if (pressed && !Raylib.CheckCollisionPointRec(mouse, panel)) Close();
        }

        // スキンのフォント(太鼓っぽい太字)で、縁取りした文字を描く。y は文字の上下の真ん中
        private static void Outlined(string text, float x, float centerY, float size, Color fill, Color outline, bool centerX)
        {
            Vector2 m = MeasureSkin(text, size);
            float thickness = Math.Min(Math.Max(2.5f, size * MenuDraw.OutlineRatio), DynamicFont.MaxOutline(size));
            FontManager.DrawSkinMainOutlined(text, centerX ? x - m.X / 2f : x, centerY - m.Y / 2f, size, fill, outline, thickness);
        }

        private static Vector2 MeasureSkin(string text, float size) => FontManager.MeasureSkinMain(text, size);

        private static Rectangle RowArea(int i, float px, float py) =>
            new Rectangle(px + RowX, py + RowTop + i % PageRows * RowStep, RowWidth, RowHeight);

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
            if (flip) SmoothShapes.Triangle(new Vector2(cx + 9, cy), new Vector2(cx - 7, cy - 11), new Vector2(cx - 7, cy + 11), c);
            else SmoothShapes.Triangle(new Vector2(cx - 9, cy), new Vector2(cx + 7, cy + 11), new Vector2(cx + 7, cy - 11), c);
        }

        private static bool KeyPressed(KeyboardKey key) => Raylib.IsKeyPressed(key) || Raylib.IsKeyPressedRepeat(key);

        private static bool EnterPressed() => Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter);

        private static void DiscardTypedChars()
        {
            while (Raylib.GetCharPressed() > 0) { }
        }
    }
}

