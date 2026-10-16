using Raylib_cs;
using TaikoSim.Configs;
using TaikoSim.Graphics;
using TaikoSim.Input;
using TaikoSim.UI;

namespace TaikoSim.Overlays
{
    // 設定パネル(TaikoNauts風に左側にかぶさる)
    public class SettingsOverlay
    {
        // ===== 大きさ(1920×1080の座標) =====
        public const int PanelWidth = 700;
        private const int HeaderHeight = 100;
        private const int SidebarWidth = 100;

        // 中身の並び
        private const float SectionX = 120;  // 見出しの左端
        private const float LabelX = 140;    // 項目名の左端
        private const float ControlX = 380;  // スライダー・プルダウンの左端
        private const float BoxX = 590;      // 数値ボックスの左端
        private const float RightEdge = 680; // 右端
        private const float RowHeight = 50;  // 1行の高さ

        // カテゴリ(左のアイコンの順番)
        private enum Category { General, Window, Sound, Play, Key, Skin }

        // プルダウンの選択肢
        private static readonly string[] ResolutionNames =
            GameConfig.Resolutions.Select(r => $"{r.Width}×{r.Height}").ToArray();
        private static readonly string[] FullscreenTypeNames = { "ボーダーレス（枠なし）", "排他フルスクリーン" };

        public bool IsOpen { get; private set; } = false;

        // スクロール
        private float scroll = 0f;        // 今の位置(なめらかに動く)
        private float scrollTarget = 0f;  // 目標の位置
        private float contentHeight = 0f; // 中身全体の高さ
        private float cursorY = 0f;       // 今描いている行の位置(中身の座標)

        // 各カテゴリの見出しの位置(アイコンを押したときのジャンプ先)
        private readonly Dictionary<Category, float> sectionPos = new();

        // アイコンでジャンプしたカテゴリ(ホイールでスクロールしたら解除)
        private Category? clickedCategory = null;

        // ===== 開閉 =====

        public void Open()
        {
            IsOpen = true;
            Gui.Reset();
        }

        public void Close()
        {
            IsOpen = false;
            Gui.Reset();
            ConfigManager.SaveAll(); // 閉じたら自動保存
        }

        // ===== 更新 =====

        public void Update(float dt)
        {
            // Esc：入力中ならキャンセル → プルダウンが開いてたら閉じる → それ以外はパネルを閉じる
            if (InputManager.IsBackPressed())
            {
                if (Gui.IsEditing) Gui.CancelEdit();
                else if (Gui.IsPopupOpen) Gui.ClosePopup();
                else Close();
                return;
            }

            // 設定キー(F1)でも閉じる(数値入力中は除く)
            if (!Gui.IsEditing && InputManager.IsSystemPressed(SystemAction.Settings))
            {
                Close();
            }
        }

        // ===== 描画 =====

        public void Draw()
        {
            Gui.BeginFrame();

            // 中身が表示される範囲(ヘッダーと左の帯を除いた部分)
            Rectangle content = new Rectangle(SidebarWidth, HeaderHeight, PanelWidth - SidebarWidth, Screen.Height - HeaderHeight);
            UpdateScroll(content);

            // 後ろの画面を暗くする
            Raylib.DrawRectangle(0, 0, Screen.Width, Screen.Height, new Color(0, 0, 0, 140));

            // パネル本体
            Raylib.DrawRectangle(0, 0, PanelWidth, Screen.Height, new Color(22, 24, 30, 250));

            // 中身(はみ出した部分は描かない)
            Raylib.BeginScissorMode((int)content.X, (int)content.Y, (int)content.Width, (int)content.Height);
            Gui.SetClip(content);

            cursorY = 20;
            DrawGeneralSection();
            DrawWindowSection();
            DrawSoundSection();
            DrawPlaySection();
            DrawKeySection();
            DrawSkinSection();
            contentHeight = cursorY + 40;

            Gui.ResetClip();
            Raylib.EndScissorMode();

            // ヘッダーと左の帯は中身より上に描く
            DrawHeader();
            DrawSidebar();

            // プルダウンの一覧は一番上に
            Gui.DrawPopups();
        }

        // ===== 各カテゴリ =====

        private void DrawGeneralSection()
        {
            Section(Category.General, "一般設定");

            GameConfig game = ConfigManager.Game;
            game.ShowFps = CheckboxRow("FPS表示", game.ShowFps);
        }

        private void DrawWindowSection()
        {
            Section(Category.Window, "ウィンドウ設定");

            GameConfig game = ConfigManager.Game;
            bool windowChanged = false;

            // 画面解像度
            int resIndex = Array.FindIndex(GameConfig.Resolutions, r => r.Width == game.WindowWidth && r.Height == game.WindowHeight);
            int newResIndex = DropdownRow("resolution", "画面解像度", ResolutionNames, resIndex);
            if (newResIndex != resIndex && newResIndex >= 0)
            {
                (game.WindowWidth, game.WindowHeight) = GameConfig.Resolutions[newResIndex];
                windowChanged = true;
            }

            // フルスクリーン
            bool fullscreen = CheckboxRow("フルスクリーン", game.Fullscreen);
            if (fullscreen != game.Fullscreen)
            {
                game.Fullscreen = fullscreen;
                windowChanged = true;
            }

            // フルスクリーン方式
            int typeIndex = (int)game.FullscreenType;
            int newTypeIndex = DropdownRow("fullscreenType", "フルスクリーン方式", FullscreenTypeNames, typeIndex);
            if (newTypeIndex != typeIndex)
            {
                game.FullscreenType = (FullscreenType)newTypeIndex;
                windowChanged = true;
            }

            // 垂直同期
            bool vsync = CheckboxRow("垂直同期", game.VSync);
            if (vsync != game.VSync)
            {
                game.VSync = vsync;
                Screen.ApplyFrameSettings();
            }

            // 目標フレームレート
            int fps = SliderRow("targetFps", "目標フレームレート", game.TargetFps, 0, 360);
            if (fps != game.TargetFps)
            {
                game.TargetFps = fps;
                game.Fix(); // 1〜29 は 30 にする
                Screen.ApplyFrameSettings();
            }
            NoteRow("0 で無制限");

            // ウィンドウの大きさやフルスクリーンは、次のフレームの最初に反映する
            if (windowChanged)
            {
                Screen.RequestApply();
            }
        }

        private void DrawSoundSection()
        {
            Section(Category.Sound, "サウンド設定");

            SoundConfig sound = ConfigManager.Sound;
            sound.MasterVolume = SliderRow("master", "Master", sound.MasterVolume, 0, 100);
            sound.SeVolume = SliderRow("se", "SE", sound.SeVolume, 0, 100);
            sound.MusicVolume = SliderRow("music", "Music", sound.MusicVolume, 0, 100);
            sound.BgmVolume = SliderRow("bgm", "BGM", sound.BgmVolume, 0, 100);
            sound.VoiceVolume = SliderRow("voice", "Voice", sound.VoiceVolume, 0, 100);
        }

        private void DrawPlaySection()
        {
            Section(Category.Play, "プレイ設定");

            GameConfig game = ConfigManager.Game;

            // 音オフセット(判定ごとずらす)
            game.AudioOffsetMs = SliderRow("audioOffset", "音オフセット[ms]", game.AudioOffsetMs, GameConfig.OffsetMin, GameConfig.OffsetMax);
            NoteRow("判定のタイミングごとずらします（判定が合わないとき）");

            // 譜面オフセット(見た目だけずらす)
            game.ChartOffsetMs = SliderRow("chartOffset", "譜面オフセット[ms]", game.ChartOffsetMs, GameConfig.OffsetMin, GameConfig.OffsetMax);
            NoteRow("音符の見た目だけずらします（判定は変わりません）");

            NoteRow("どちらもマイナスで早く、プラスで遅くなります");
        }

        private void DrawKeySection()
        {
            Section(Category.Key, "キー設定");
            NoteRow("（次のステップで作成）");
        }

        private void DrawSkinSection()
        {
            Section(Category.Skin, "スキン設定");
            NoteRow("（次のステップで作成）");
        }

        // ===== 行の部品 =====

        // 見出し
        private void Section(Category category, string title)
        {
            if (category != Category.General) cursorY += 30; // カテゴリの間を空ける
            sectionPos[category] = cursorY;
            Gui.Header(title, SectionX, ToScreenY(cursorY));
            cursorY += 64;
        }

        // チェックボックスの行
        private bool CheckboxRow(string label, bool value)
        {
            float y = ToScreenY(cursorY);
            bool result = Gui.Checkbox(new Rectangle(LabelX, y, RightEdge - LabelX, 40), label, value);
            cursorY += RowHeight;
            return result;
        }

        // スライダーの行
        private int SliderRow(string id, string label, int value, int min, int max)
        {
            float y = ToScreenY(cursorY);
            Gui.Label(label, LabelX, y + 4, 26);

            Rectangle bar = new Rectangle(ControlX, y + 6, BoxX - ControlX - 12, 30);
            Rectangle box = new Rectangle(BoxX, y + 3, RightEdge - BoxX, 36);
            int result = Gui.Slider(id, bar, box, value, min, max);

            cursorY += RowHeight;
            return result;
        }

        // プルダウンの行
        private int DropdownRow(string id, string label, string[] options, int selected)
        {
            float y = ToScreenY(cursorY);
            Gui.Label(label, LabelX, y + 4, 26);

            int result = Gui.Dropdown(id, new Rectangle(ControlX, y + 3, RightEdge - ControlX, 36), options, selected);

            cursorY += RowHeight;
            return result;
        }

        // 小さい説明文の行
        private void NoteRow(string text)
        {
            FontManager.Draw(text, LabelX, ToScreenY(cursorY), 22, Gui.SubTextColor);
            cursorY += 36;
        }

        // 中身の座標 → 画面の座標(スクロール分ずらす)
        private float ToScreenY(float contentY)
        {
            return HeaderHeight + contentY - scroll;
        }

        // ===== スクロール =====

        private void UpdateScroll(Rectangle content)
        {
            // マウスホイール(プルダウンが開いている間は無効)
            float wheel = Raylib.GetMouseWheelMove();
            if (!Gui.IsPopupOpen && wheel != 0 && Gui.IsMouseOver(content))
            {
                scrollTarget -= wheel * 100f;
                clickedCategory = null;
            }

            // 行きすぎないようにする
            float maxScroll = Math.Max(0f, contentHeight - content.Height);
            scrollTarget = Math.Clamp(scrollTarget, 0f, maxScroll);

            // なめらかに動かす
            scroll += (scrollTarget - scroll) * Math.Min(1f, Raylib.GetFrameTime() * 15f);
            if (Math.Abs(scrollTarget - scroll) < 0.5f) scroll = scrollTarget;
        }

        // 今見ているカテゴリ(左のアイコンを光らせる用)
        private Category GetCurrentCategory()
        {
            if (clickedCategory != null) return clickedCategory.Value;

            Category current = Category.General;
            float best = -1f;
            foreach (var pair in sectionPos)
            {
                if (pair.Value <= scroll + 10f && pair.Value > best)
                {
                    best = pair.Value;
                    current = pair.Key;
                }
            }
            return current;
        }

        // ===== ヘッダーと左の帯 =====

        private void DrawHeader()
        {
            Raylib.DrawRectangle(0, 0, PanelWidth, HeaderHeight, new Color(10, 11, 14, 255));

            // ロゴ(仮)
            Rectangle logo = new Rectangle(15, 15, 70, 70);
            Raylib.DrawRectangleRounded(logo, 0.25f, 8, new Color(240, 110, 40, 255));
            Gui.DrawCentered("TS", logo, 34, Color.White);

            // タイトルと日時
            FontManager.Draw("TaikoSim System Menu", 120, 14, 40, Color.White);
            FontManager.Draw(DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"), 120, 62, 24, Color.LightGray);
        }

        private void DrawSidebar()
        {
            Color background = new Color(14, 15, 19, 255);
            Raylib.DrawRectangle(0, HeaderHeight, SidebarWidth, Screen.Height - HeaderHeight, background);

            // カテゴリのアイコン
            Category current = GetCurrentCategory();
            Category[] categories = Enum.GetValues<Category>();

            for (int i = 0; i < categories.Length; i++)
            {
                Category category = categories[i];
                Rectangle area = new Rectangle(0, 120 + i * 90, SidebarWidth, 80);
                bool hover = !Gui.IsPopupOpen && Gui.IsMouseOver(area);

                Color back = background;
                if (category == current) back = new Color(40, 60, 110, 255);
                else if (hover) back = new Color(30, 34, 48, 255);
                Raylib.DrawRectangleRec(area, back);

                DrawCategoryIcon(category, SidebarWidth / 2f, area.Y + area.Height / 2f, back);

                // クリックでそのカテゴリまでジャンプ
                if (Gui.Clicked(area) && sectionPos.TryGetValue(category, out float pos))
                {
                    scrollTarget = pos;
                    clickedCategory = category;
                }
            }

            // 左下の終了ボタン
            Rectangle power = new Rectangle(0, Screen.Height - 100, SidebarWidth, 90);
            bool powerHover = !Gui.IsPopupOpen && Gui.IsMouseOver(power);
            if (powerHover) Raylib.DrawRectangleRec(power, new Color(70, 25, 25, 255));
            Icons.Power(SidebarWidth / 2f, power.Y + power.Height / 2f, new Color(230, 70, 60, 255));

            if (Gui.Clicked(power))
            {
                Game.ShouldExit = true; // 終了(設定は Game 側で保存される)
            }
        }

        private static void DrawCategoryIcon(Category category, float x, float y, Color background)
        {
            Color color = Color.White;
            switch (category)
            {
                case Category.General: Icons.Gear(x, y, color, background); break;
                case Category.Window: Icons.Monitor(x, y, color); break;
                case Category.Sound: Icons.Speaker(x, y, color); break;
                case Category.Play: Icons.Drum(x, y, color); break;
                case Category.Key: Icons.Keyboard(x, y, color); break;
                case Category.Skin: Icons.Brush(x, y, color); break;
            }
        }
    }
}