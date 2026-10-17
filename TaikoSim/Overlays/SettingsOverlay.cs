using System.Diagnostics;
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
        private const int HintHeight = 44;    // 一番下の操作説明の高さ

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
        // フルスクリーン方式の選択肢と説明文は UI/SettingTexts.cs にまとめてある(初回セットアップと共通)

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

        // ===== キー設定用 =====

        // キー待ちの対象(叩く場所 か システムキー)
        private readonly record struct KeyTarget(bool IsDrum, DrumPart Part, SystemAction Action);

        // 今キー待ちしている対象(null なら待っていない)
        private KeyTarget? waitingTarget = null;

        // 開いている叩く場所(アコーディオン)
        private readonly HashSet<DrumPart> openParts = new();

        // 結果メッセージ(パネルの下に数秒出す)
        private string message = "";
        private Color messageColor = Color.White;
        private float messageTimer = 0f;

        // 点滅用のタイマー
        private float blinkTimer = 0f;

        // メッセージの色
        private static readonly Color SuccessColor = new Color(110, 220, 130, 255);
        private static readonly Color ErrorColor = new Color(255, 110, 100, 255);
        private static readonly Color NeutralColor = new Color(200, 205, 215, 255);

        // キー入力待ち中か(Game がスクショキーを無視するのに使う)
        public bool IsWaitingKey => waitingTarget != null;

        // ===== 閉じるときの確認ダイアログ =====
        private bool confirmOpen = false;
        private List<DrumPart> confirmEmptyParts = new();

        // クリックを全部無効にするための「大きさ0の範囲」
        private static readonly Rectangle NoClickArea = new Rectangle(0, 0, 0, 0);

        // ===== スキン設定用 =====

        // 「フォルダを選択」が押された(描画中に画面を出すと危ないので、次の Update で出す)
        private bool pickFolderRequested = false;

        // プレビューに出す画像(判定枠・ドン・カッ・大ドン・大カッ)
        private static readonly string[] PreviewImages =
        {
            "Image/04.EnsoGame/Notes/JudgementFrame.png",
            "Image/04.EnsoGame/Notes/Normal/0/0.png",
            "Image/04.EnsoGame/Notes/Normal/0/1.png",
            "Image/04.EnsoGame/Notes/Normal/0/2.png",
            "Image/04.EnsoGame/Notes/Normal/0/3.png",
        };

        // ===== 開閉 =====

        public void Open()
        {
            IsOpen = true;
            Gui.Reset();

            // スキンを入れたあとに開いたときのために、スキン一覧を読み直す
            SkinManager.Reload();
        }

        public void Close()
        {
            IsOpen = false;
            Gui.Reset();
            waitingTarget = null;
            messageTimer = 0f;
            confirmOpen = false;
            ConfigManager.SaveAll(); // 閉じたら自動保存
        }

        // 閉じたいとき(キーが無い場所があれば確認ダイアログを出す)
        public void RequestClose()
        {
            List<DrumPart> emptyParts = ConfigManager.Key.GetEmptyParts();
            if (emptyParts.Count > 0)
            {
                confirmEmptyParts = emptyParts;
                confirmOpen = true;
                waitingTarget = null;
                Gui.Reset();
                return;
            }
            Close();
        }

        // ===== 更新 =====

        public void Update(float dt)
        {
            blinkTimer += dt;
            if (messageTimer > 0f) messageTimer -= dt;

            // 「フォルダを選択」が押されていたら、Windowsの選択画面を出す
            if (pickFolderRequested)
            {
                pickFolderRequested = false;
                PickSkinsFolder();
                return;
            }

            // 確認ダイアログ中は、Escで「設定に戻る」
            if (confirmOpen)
            {
                if (InputManager.IsBackPressed()) confirmOpen = false;
                return;
            }

            // キー入力待ち中は、押されたキーを登録する(F1やF11もここで受け取る)
            if (waitingTarget is KeyTarget target)
            {
                // Escでキャンセル
                if (InputManager.IsBackPressed())
                {
                    waitingTarget = null;
                    ShowMessage("キャンセルしました", NeutralColor);
                    return;
                }

                int pressed = Raylib.GetKeyPressed();
                if (pressed != 0)
                {
                    KeyboardKey newKey = (KeyboardKey)pressed;
                    KeyConfig keyConfig = ConfigManager.Key;

                    bool success;
                    string result;
                    if (target.IsDrum) success = keyConfig.TryAddDrumKey(target.Part, newKey, out result);
                    else success = keyConfig.TrySetSystemKey(target.Action, newKey, out result);

                    ShowMessage(result, success ? SuccessColor : ErrorColor);
                    waitingTarget = null;
                }
                return;
            }

            // Esc：入力中ならキャンセル → プルダウンが開いてたら閉じる → それ以外はパネルを閉じる
            if (InputManager.IsBackPressed())
            {
                if (Gui.IsEditing) Gui.CancelEdit();
                else if (Gui.IsPopupOpen) Gui.ClosePopup();
                else RequestClose();
                return;
            }

            // 設定キー(F1)でも閉じる(数値入力中は除く)
            if (!Gui.IsEditing && InputManager.IsSystemPressed(SystemAction.Settings))
            {
                RequestClose();
            }
        }

        // ===== 描画 =====

        public void Draw()
        {
            Gui.BeginFrame();

            // 中身が表示される範囲(ヘッダー・左の帯・一番下の操作説明を除いた部分)
            Rectangle content = new Rectangle(SidebarWidth, HeaderHeight, PanelWidth - SidebarWidth, Screen.Height - HeaderHeight - HintHeight);
            if (!confirmOpen) UpdateScroll(content); // ダイアログ中はスクロールしない

            // 後ろの画面を暗くする
            Raylib.DrawRectangle(0, 0, Screen.Width, Screen.Height, new Color(0, 0, 0, 140));

            // パネル本体
            Raylib.DrawRectangle(0, 0, PanelWidth, Screen.Height, new Color(22, 24, 30, 250));

            // 中身(はみ出した部分は描かない)
            Raylib.BeginScissorMode((int)content.X, (int)content.Y, (int)content.Width, (int)content.Height);
            Gui.SetClip(confirmOpen ? NoClickArea : content); // ダイアログ中は後ろを押せないようにする

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
            if (confirmOpen) Gui.SetClip(NoClickArea);
            DrawHeader();
            DrawSidebar();
            Gui.ResetClip();

            // 結果メッセージ(パネルの下)
            DrawMessage();

            // 一番下の操作説明
            DrawHint();

            // 一番上：確認ダイアログ or プルダウンの一覧
            if (confirmOpen) DrawConfirmDialog();
            else Gui.DrawPopups();
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
            if (!game.Fullscreen) DescRows(SettingTexts.Window); // オフのとき(ウィンドウ)の説明

            // フルスクリーン方式
            int typeIndex = (int)game.FullscreenType;
            int newTypeIndex = DropdownRow("fullscreenType", "フルスクリーン方式", SettingTexts.FullscreenTypeNames, typeIndex);
            if (newTypeIndex != typeIndex)
            {
                game.FullscreenType = (FullscreenType)newTypeIndex;
                windowChanged = true;
            }

            // 選んでいる方式の説明
            foreach (string text in SettingTexts.GetFullscreenTypeTexts(game.FullscreenType))
            {
                DescRows(text);
            }

            // 垂直同期
            bool vsync = CheckboxRow("垂直同期", game.VSync);
            if (vsync != game.VSync)
            {
                game.VSync = vsync;
                Screen.ApplyFrameSettings();
            }
            DescRows(SettingTexts.VSync);

            // 目標フレームレート
            int fps = SliderRow("targetFps", "目標フレームレート", game.TargetFps, 0, 360);
            if (fps != game.TargetFps)
            {
                game.TargetFps = fps;
                game.Fix(); // 1〜29 は 30 にする
                Screen.ApplyFrameSettings();
            }
            DescRows(SettingTexts.TargetFps);

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

            KeyConfig key = ConfigManager.Key;

            // ----- 叩くキー -----
            SubHeader("叩くキー");

            // キーが無い場所があったら赤字で注意
            if (key.GetEmptyParts().Count > 0)
            {
                FontManager.Draw("！ キーが無い場所があります（このままだと演奏できません）", LabelX, ToScreenY(cursorY), 22, ErrorColor);
                cursorY += 36;
            }

            foreach (DrumPart part in Enum.GetValues<DrumPart>())
            {
                DrumPartRows(key, part);
            }

            // ----- システムキー -----
            cursorY += 10;
            SubHeader("システムキー");
            FixedKeyRow("戻る", $"[ {KeyConfig.BackKey} ]（固定）");

            foreach (SystemAction action in Enum.GetValues<SystemAction>())
            {
                SystemKeyRow(key, action);
            }

            // リセットボタン
            float buttonY = ToScreenY(cursorY);
            if (Gui.Button(new Rectangle(LabelX, buttonY + 3, RightEdge - LabelX, 40), "システムキーを初期値に戻す"))
            {
                waitingTarget = null;
                key.ResetSystemKeys();
                ShowMessage("システムキーを初期値に戻しました", SuccessColor);
            }
            cursorY += RowHeight + 6;
        }

        private void DrawSkinSection()
        {
            Section(Category.Skin, "スキン設定");

            SkinConfig skin = ConfigManager.Skin;

            // ----- スキンフォルダ -----
            Gui.Label("スキンフォルダ", LabelX, ToScreenY(cursorY) + 4, 26);
            cursorY += 40;

            string folderText = (string.IsNullOrWhiteSpace(skin.SkinsFolder) ? "（初期）" : "") + skin.GetSkinsFolderPath();
            FontManager.Draw(FitText(folderText, RightEdge - LabelX, 20), LabelX, ToScreenY(cursorY), 20, Gui.SubTextColor);
            cursorY += 34;

            float y = ToScreenY(cursorY);
            float buttonWidth = (RightEdge - LabelX - 20) / 3f;
            if (Gui.Button(new Rectangle(LabelX, y, buttonWidth, 40), "フォルダ選択"))
            {
                pickFolderRequested = true;
            }
            if (Gui.Button(new Rectangle(LabelX + buttonWidth + 10, y, buttonWidth, 40), "場所を開く"))
            {
                OpenSkinsFolder();
            }
            if (Gui.Button(new Rectangle(LabelX + (buttonWidth + 10) * 2, y, buttonWidth, 40), "初期に戻す"))
            {
                SetSkinsFolder("", null);
            }
            cursorY += 52;

            // スキン一覧を更新(スキンをフォルダに入れたあとに押す)
            if (Gui.Button(new Rectangle(LabelX, ToScreenY(cursorY), RightEdge - LabelX, 40), "スキン一覧を更新"))
            {
                RefreshSkins();
            }
            cursorY += 52;
            NoteRow("※ フォルダをこのウィンドウにドラッグ＆ドロップでも変更できます");

            // ----- 使うスキン -----
            List<string> names = SkinManager.SkinNames;
            if (names.Count == 0)
            {
                Gui.Label("使うスキン", LabelX, ToScreenY(cursorY) + 4, 26);
                FontManager.Draw("スキンがありません", ControlX, ToScreenY(cursorY) + 6, 24, ErrorColor);
                cursorY += RowHeight;
                NoteRow("スキンフォルダにスキン（A-Style など）を入れてください");
                return;
            }

            int index = names.IndexOf(SkinManager.CurrentName);
            int newIndex = DropdownRow("skinName", "使うスキン", names.ToArray(), index);
            if (newIndex != index && newIndex >= 0)
            {
                skin.SkinName = names[newIndex];
                SkinManager.Reload(); // その場で切り替え
                ShowMessage($"スキンを {SkinManager.CurrentName} にしました", SuccessColor);
            }

            // スキンの情報
            string info = SkinManager.DisplayName;
            if (SkinManager.Version.Length > 0) info += $"　ver.{SkinManager.Version}";
            NoteRow(FitText(info, RightEdge - LabelX, 22));
            if (SkinManager.Description.Length > 0) NoteRow(FitText(SkinManager.Description, RightEdge - LabelX, 22));

            // ----- プレビュー(画像がちゃんと読めているかの確認) -----
            float previewY = ToScreenY(cursorY);
            Rectangle previewBox = new Rectangle(LabelX, previewY, RightEdge - LabelX, 110);
            Raylib.DrawRectangleRec(previewBox, new Color(12, 13, 17, 255));

            bool anyDrawn = false;
            float px = previewBox.X + 10;
            foreach (string image in PreviewImages)
            {
                // 195×195の画像を半分の大きさで並べる
                if (SkinManager.Draw(image, px, previewY + 7, 0.5f)) anyDrawn = true;
                px += 104;
            }
            if (!anyDrawn)
            {
                Gui.DrawCentered("画像を読み込めませんでした", previewBox, 22, ErrorColor);
            }
            cursorY += 122;
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

        // 説明文の行(長い文はパネルの幅で折り返す)
        private void DescRows(string text)
        {
            cursorY += SettingTexts.DrawWrapped(text, LabelX, ToScreenY(cursorY), RightEdge - LabelX, 22, Gui.SubTextColor, 30);
            cursorY += 6;
        }

        // 小見出し(「叩くキー」とか)
        private void SubHeader(string text)
        {
            FontManager.Draw(text, SectionX + 10, ToScreenY(cursorY), 28, Gui.SubTextColor);
            cursorY += 44;
        }

        // 叩く場所1つ分(開閉できる見出し＋キー一覧＋追加ボタン)
        private void DrumPartRows(KeyConfig key, DrumPart part)
        {
            List<KeyboardKey> keys = key.DrumKeys.GetValueOrDefault(part, new List<KeyboardKey>());
            bool open = openParts.Contains(part);

            // --- 見出し(クリックで開閉) ---
            float y = ToScreenY(cursorY);
            Rectangle header = new Rectangle(LabelX - 10, y, RightEdge - LabelX + 10, 42);
            bool hover = !Gui.IsPopupOpen && Gui.IsMouseOver(header);
            Raylib.DrawRectangleRec(header, hover ? Gui.BoxHoverColor : new Color(30, 36, 56, 255));

            FontManager.Draw(open ? "▼" : "▶", LabelX, y + 11, 20, Gui.SubTextColor);
            FontManager.Draw(KeyConfig.GetDrumPartName(part), LabelX + 34, y + 6, 26, Color.White);

            // 閉じているときは、右側に登録されているキーをまとめて表示
            if (!open)
            {
                if (keys.Count == 0) FontManager.Draw("未割り当て", ControlX, y + 8, 24, ErrorColor);
                else FontManager.Draw(string.Join(", ", keys), ControlX, y + 8, 24, Gui.SubTextColor);
            }

            if (Gui.Clicked(header))
            {
                if (open) openParts.Remove(part);
                else openParts.Add(part);
            }
            cursorY += 50;

            if (!open) return;

            // --- 登録されているキー(×で外す) ---
            foreach (KeyboardKey k in keys.ToList()) // 外すと一覧が変わるのでコピーして回す
            {
                float keyY = ToScreenY(cursorY);
                Gui.Label($"[ {k} ]", LabelX + 34, keyY + 4, 26);

                Rectangle removeButton = new Rectangle(RightEdge - 44, keyY + 3, 40, 36);
                if (Gui.Button(removeButton, "×"))
                {
                    bool success = key.TryRemoveDrumKey(part, k, out string result);
                    ShowMessage(result, success ? SuccessColor : ErrorColor);
                }
                cursorY += 44;
            }

            // --- 追加ボタン ---
            float addY = ToScreenY(cursorY);
            bool waiting = waitingTarget is KeyTarget t && t.IsDrum && t.Part == part;

            if (waiting)
            {
                DrawWaitingBox(new Rectangle(LabelX + 34, addY + 2, RightEdge - LabelX - 34, 36), "キーを押してください（Escでキャンセル）");
            }
            else if (Gui.Button(new Rectangle(LabelX + 34, addY + 2, 160, 36), "＋ 追加"))
            {
                waitingTarget = new KeyTarget(true, part, default);
            }
            cursorY += 54;
        }

        // システムキー1つ分(クリック → キーを押して変更)
        private void SystemKeyRow(KeyConfig key, SystemAction action)
        {
            float y = ToScreenY(cursorY);
            Gui.Label(KeyConfig.GetSystemActionName(action), LabelX, y + 4, 26);

            Rectangle box = new Rectangle(ControlX, y + 3, RightEdge - ControlX, 36);
            bool waiting = waitingTarget is KeyTarget t && !t.IsDrum && t.Action == action;

            if (waiting)
            {
                DrawWaitingBox(box, "キーを押してください");
            }
            else
            {
                KeyboardKey current = key.SystemKeys.GetValueOrDefault(action, KeyboardKey.Null);
                if (Gui.Button(box, $"[ {current} ]"))
                {
                    waitingTarget = new KeyTarget(false, default, action);
                }
            }
            cursorY += RowHeight;
        }

        // 変更できないキーの行(戻るキー)
        private void FixedKeyRow(string label, string keyText)
        {
            float y = ToScreenY(cursorY);
            Gui.Label(label, LabelX, y + 4, 26);

            Rectangle box = new Rectangle(ControlX, y + 3, RightEdge - ControlX, 36);
            Raylib.DrawRectangleRec(box, new Color(30, 32, 40, 255));
            Gui.DrawCentered(keyText, box, 24, Gui.SubTextColor);

            cursorY += RowHeight;
        }

        // キー入力待ちの箱(点滅)
        private void DrawWaitingBox(Rectangle area, string text)
        {
            Raylib.DrawRectangleRec(area, new Color(18, 22, 36, 255));
            Raylib.DrawRectangleLinesEx(area, 2, Gui.AccentColor);

            if ((blinkTimer % 1f) < 0.7f)
            {
                Gui.DrawCentered(text, area, 22, Gui.AccentColor);
            }
        }

        // 結果メッセージを出す
        private void ShowMessage(string text, Color color)
        {
            message = text;
            messageColor = color;
            messageTimer = 3f; // 3秒表示
        }

        // 結果メッセージを描く(スクロールしても見えるように、パネルの下に固定)
        private void DrawMessage()
        {
            if (messageTimer <= 0f) return;

            // 操作説明の1段上に出す
            Rectangle bar = new Rectangle(SidebarWidth, Screen.Height - HintHeight - 64, PanelWidth - SidebarWidth, 64);
            Raylib.DrawRectangleRec(bar, new Color(8, 9, 12, 240));
            FontManager.Draw(FitText(message, PanelWidth - LabelX - 20, 26), LabelX, bar.Y + 18, 26, messageColor);
        }

        // 一番下の操作説明(今の状況に合わせて変える)
        private void DrawHint()
        {
            string hint;
            if (confirmOpen) hint = "Esc：設定に戻る";
            else if (waitingTarget != null) hint = "キーを押すと登録　Esc：キャンセル";
            else if (Gui.IsEditing) hint = "数字を入力　Enter：決定　Esc：キャンセル";
            else if (Gui.IsSliderFocused) hint = "←→：±1　Shift＋←→：±10　数字クリック：直接入力";
            else hint = "Esc：閉じる　スライダーは←→でも調整できます";

            Rectangle bar = new Rectangle(SidebarWidth, Screen.Height - HintHeight, PanelWidth - SidebarWidth, HintHeight);
            Raylib.DrawRectangleRec(bar, new Color(12, 13, 17, 255));
            FontManager.Draw(hint, SidebarWidth + 20, bar.Y + 11, 20, Gui.SubTextColor);
        }

        // 閉じるときの確認ダイアログ
        private void DrawConfirmDialog()
        {
            // 全体を暗くする
            Raylib.DrawRectangle(0, 0, Screen.Width, Screen.Height, new Color(0, 0, 0, 160));

            Rectangle box = new Rectangle(560, 340, 800, 400);
            Raylib.DrawRectangleRec(box, new Color(28, 32, 46, 255));
            Raylib.DrawRectangleLinesEx(box, 2, ErrorColor);

            string names = string.Join("・", confirmEmptyParts.Select(KeyConfig.GetDrumPartName));
            FontManager.Draw("！ キーが設定されていない場所があります", box.X + 40, box.Y + 40, 34, ErrorColor);
            FontManager.Draw($"{names} にキーがありません。", box.X + 40, box.Y + 120, 30, Color.White);
            FontManager.Draw("このままだと演奏できません。", box.X + 40, box.Y + 165, 30, Color.White);
            FontManager.Draw("※ 次に起動したときに、キーの設定画面が出ます", box.X + 40, box.Y + 215, 24, Gui.SubTextColor);

            Rectangle backButton = new Rectangle(box.X + 40, box.Y + 300, 340, 60);
            Rectangle closeButton = new Rectangle(box.X + box.Width - 380, box.Y + 300, 340, 60);

            bool backClicked = Gui.Button(backButton, "設定に戻る（Esc）");
            bool closeClicked = Gui.Button(closeButton, "このまま閉じる");

            if (backClicked) confirmOpen = false;
            else if (closeClicked) Close();
        }

        // ===== スキンフォルダの変更 =====

        // ファイルがドロップされたとき(Game から呼ばれる)
        public void OnFilesDropped(string[] paths)
        {
            string? folder = paths.FirstOrDefault(Directory.Exists);
            if (folder == null)
            {
                ShowMessage("フォルダをドロップしてください", ErrorColor);
                return;
            }
            ChangeSkinsFolder(folder);
        }

        // Windowsの「フォルダの選択」画面を出す
        private void PickSkinsFolder()
        {
            string current = ConfigManager.Skin.GetSkinsFolderPath();

            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "スキンフォルダ（スキンが入っているフォルダ）を選んでください",
                UseDescriptionForTitle = true,
                InitialDirectory = Directory.Exists(current) ? current : "",
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                ChangeSkinsFolder(dialog.SelectedPath);
            }
        }

        // 選ばれたフォルダに合わせてスキンフォルダを変える
        // スキン1つ分のフォルダ(A-Style など)が選ばれたら、親フォルダをスキンフォルダにして、そのスキンを使う
        private void ChangeSkinsFolder(string folder)
        {
            if (SkinConfig.IsSkinFolder(folder))
            {
                string parent = Path.GetDirectoryName(folder) ?? folder;
                SetSkinsFolder(parent, Path.GetFileName(folder));
            }
            else
            {
                SetSkinsFolder(folder, null);
            }
        }

        // スキンフォルダを設定して、スキンを読み直す
        private void SetSkinsFolder(string folder, string? skinName)
        {
            SkinConfig skin = ConfigManager.Skin;
            skin.SkinsFolder = folder;
            if (skinName != null) skin.SkinName = skinName;
            skin.Fix();

            SkinManager.Reload(); // その場で切り替え

            if (SkinManager.SkinNames.Count == 0)
            {
                ShowMessage("このフォルダにスキンが見つかりませんでした", ErrorColor);
            }
            else
            {
                ShowMessage($"スキンフォルダを変更しました（{SkinManager.SkinNames.Count}個、使用中：{SkinManager.CurrentName}）", SuccessColor);
            }
        }

        // スキン一覧を読み直す
        private void RefreshSkins()
        {
            SkinManager.Reload();
            if (SkinManager.SkinNames.Count == 0)
            {
                ShowMessage("スキンが見つかりませんでした", ErrorColor);
            }
            else
            {
                ShowMessage($"スキン一覧を更新しました（{SkinManager.SkinNames.Count}個、使用中：{SkinManager.CurrentName}）", SuccessColor);
            }
        }

        // スキンフォルダをエクスプローラーで開く
        private void OpenSkinsFolder()
        {
            string folder = ConfigManager.Skin.GetSkinsFolderPath();
            try
            {
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ShowMessage("フォルダを開けませんでした", ErrorColor);
                Console.WriteLine($"[Skin] フォルダを開けませんでした: {ex.Message}");
            }
        }

        // 長すぎる文字は、先頭を「…」にして幅に収める(パスの最後の方が大事なので)
        private static string FitText(string text, float maxWidth, float size)
        {
            if (FontManager.Measure(text, size).X <= maxWidth) return text;

            string cut = text;
            while (cut.Length > 1 && FontManager.Measure("…" + cut, size).X > maxWidth)
            {
                cut = cut[1..];
            }
            return "…" + cut;
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
