using System.Diagnostics;
using System.Numerics;
using Raylib_cs;
using TakeTaikoSimulator.Charts;
using TakeTaikoSimulator.Audio;
using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Diagnostics;
using TakeTaikoSimulator.Graphics;
using TakeTaikoSimulator.Input;
using TakeTaikoSimulator.Scenes;
using TakeTaikoSimulator.UI;

namespace TakeTaikoSimulator.Overlays
{
    // モード選択からは全画面、F1 からは左側のパネルとして開く
    //
    // 動き(10/29。transitions.dev・reui.io の「横からすべりこむ画面」をまねした)
    //   ・開く：全画面は右から、F1 のパネルは左から、0.3秒でスッとすべりこむ(はじめ速く、最後はゆっくり止まる)。後ろはだんだん暗くなる
    //   ・閉じる：同じ向きへ 0.22秒で戻っていく(はじめゆっくり、だんだん速く)
    //   ・動いている間は、ボタンやキーに反応しない
    //   ・カテゴリを変えると、左の青い帯が次の場所までスッと動き、右の中身は 18px ずれながら 0.22秒でじわっと出る
    public class SettingsOverlay
    {
        // ===== 大きさ(1920×1080の座標) =====
        public const int PanelWidth = 900; // F1 でも説明と操作欄が並ぶ幅
        private bool fullScreen;
        // 画面全体をおおっているか(すべっている間は、後ろの画面も見えるので false)
        public bool IsFullScreen => IsOpen && fullScreen && !IsAnimating;

        // ===== 開く・閉じる動き =====
        private const float OpenSeconds = 0.30f, CloseSeconds = 0.22f;
        // カテゴリの切りかえ（Motion Prompt #057 タブ。11/10）
        // Motion Prompt #057 · MIT License (c) 2026 Shohei Ohtaki · https://showheyohtaki.github.io/motion-prompt/#057
        //   選んでいる帯：前の場所から次の場所へ 0.25秒（easeInOutCubic）で滑る（前は「だんだん近づく」動きで約0.1秒）
        //   中身：0.1秒待ってから 0.2秒で出てくる（下から 8px 上がりながら）。#057 では古い中身が 0.2秒で消えるけど、
        //   TakeTaikoSimulator は古い中身を描き直せないので、そこは「すぐ消える」にした
        private const float SwitchDelay = 0.1f, SwitchSeconds = 0.2f, SwitchShift = 8f, IndicatorSeconds = 0.25f;
        private float indicatorFrom = -1f, indicatorTo = -1f, indicatorTime = 99f;
        private float animTime;          // 開き始め・閉じ始めてからの時間
        private bool closing;            // 閉じる動きの途中
        private float switchTime = 99f;  // カテゴリを変えてからの時間
        private float switchDir = 1f;    // 下のカテゴリへ行くとき 1(中身は下から)、上へ行くとき -1
        private float indicatorY = -1f;  // 左の青い帯の今の y(-1 なら次に描くときに目標へいきなり置く)
        private bool IsAnimating => closing || animTime < OpenSeconds;

        // 開き具合(0 で画面の外、1 で開ききった)
        private float OpenAmount()
        {
            if (closing)
            {
                float t = Math.Clamp(animTime / CloseSeconds, 0f, 1f);
                return 1f - t * t * t;                       // ease-in(だんだん速く出ていく)
            }
            float u = 1f - Math.Clamp(animTime / OpenSeconds, 0f, 1f);
            return 1f - u * u * u * u * u;                   // ease-out quint(スッと入って、ゆっくり止まる)
        }
        private float HeaderHeight => fullScreen ? 104 : 90;
        private float SidebarWidth => fullScreen ? 280 : 176;
        private float HintHeight => fullScreen ? 88 : 80;
        private float CurrentWidth => fullScreen ? Screen.Width : PanelWidth;

        // 中身の並び
        private float SectionX => fullScreen ? 340 : 198;
        private float LabelX => fullScreen ? 360 : 212;
        private float ControlX => fullScreen ? 1210 : 530;
        private float BoxX => fullScreen ? 1560 : 730;
        private float RightEdge => fullScreen ? 1760 : 850;
        private float RowHeight => fullScreen ? 66 : 60;

        // カテゴリ(アイコンを使わず、番号と文字で探せるようにする)
        private enum Category { General, Window, Sound, Play, Key, Skin, Debug }
        private static readonly string[] CategoryNames = { "一般", "画面", "音", "演奏", "キー", "スキン", "デバッグ" };
        // 左に出すカテゴリ。「デバッグ」は裏コマンドで出したときだけ（GameConfig.DebugMenu。11/26）
        private static Category[] VisibleCategories => ConfigManager.Game.DebugMenu
            ? Enum.GetValues<Category>()
            : Enum.GetValues<Category>().Where(c => c != Category.Debug).ToArray();

        // ===== 裏コマンド（11/26 たけらい「裏コマンド的なやつで設定項目出せるように」→ 2 と 3 の両方） =====
        //   ・キーボードで d → e → b → u → g（1文字ごとに 1.5秒以内。文字の入力欄を使っているときは数えない）
        //   ・上の「設定」の文字を 7回クリック（3秒以内）
        //   どちらも、もう一度やると隠れる
        private static readonly KeyboardKey[] SecretWord = { KeyboardKey.D, KeyboardKey.E, KeyboardKey.B, KeyboardKey.U, KeyboardKey.G };
        private const float SecretKeyGap = 1.5f, SecretClickWindow = 3f;
        private const int SecretClicks = 7;
        private int secretIndex;
        private float secretKeyTimer;
        private int secretClickCount;
        private float secretClickTimer;
        private Category selectedCategory = Category.General;

        // 全画面では、適用を押すまで本番の設定に触れない
        private GameConfig? draftGame, savedGame;
        private SoundConfig? draftSound, savedSound;
        private KeyConfig? draftKey, savedKey;
        private SkinConfig? draftSkin, savedSkin;
        private List<string> visibleSkinNames = new();
        private bool unsavedOpen;
        private bool restartOpen;   // 描画方式を変えて適用したあとの「再起動しますか？」
        private GameConfig ActiveGame => fullScreen ? draftGame! : ConfigManager.Game;
        // 右の中身のパネル（グラデーションの基準の四角。上に重ねる帯もこの四角の続きの色でぬる）
        private Rectangle PanelArea => new Rectangle(SidebarWidth, HeaderHeight, CurrentWidth - SidebarWidth, Screen.Height - HeaderHeight - HintHeight);
        private Rectangle BackArea => new Rectangle(0, 0, CurrentWidth, Screen.Height);   // 背景のグラデーションの基準
        private static readonly string[] UiThemeNames = { "ダーク", "さくら", "くれない" };   // 11/26 さくら色に作り直したので名前も（設定ファイルの値は Pink のまま）   // UiThemeKind の順
        private SoundConfig ActiveSound => fullScreen ? draftSound! : ConfigManager.Sound;
        private KeyConfig ActiveKey => fullScreen ? draftKey! : ConfigManager.Key;
        private SkinConfig ActiveSkin => fullScreen ? draftSkin! : ConfigManager.Skin;
        private bool HasPendingChanges => fullScreen && (
            !ConfigManager.Same(draftGame!, savedGame!) || !ConfigManager.Same(draftSound!, savedSound!)
            || !ConfigManager.Same(draftKey!, savedKey!) || !ConfigManager.Same(draftSkin!, savedSkin!));

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

        // 各カテゴリの見出しの位置(左の項目を押したときのジャンプ先)
        private readonly Dictionary<Category, float> sectionPos = new();

        // 左の項目でジャンプしたカテゴリ(ホイールでスクロールしたら解除)
        private Category? clickedCategory = null;
        private bool scrollDragging;
        private float scrollGrabOffset;

        // ===== キー設定用 =====

        // キー待ちの対象(叩く場所 か システムキー)
        private readonly record struct KeyTarget(bool IsDrum, DrumPart Part, SystemAction Action);

        // 今キー待ちしている対象(null なら待っていない)
        private KeyTarget? waitingTarget = null;

        // 開いている叩く場所(アコーディオン)
        private readonly HashSet<DrumPart> openParts = new();

        // 結果メッセージ(パネルの下に数秒出す)
        private string message = "";
        private Color messageColor = Gui.TextColor;
        private float messageTimer = 0f;

        // 点滅用のタイマー
        private float blinkTimer = 0f;

        // メッセージの色
        private static Color SuccessColor => UiTheme.P.Success;   // UIの色で変わる（11/26）
        private static Color ErrorColor => UiTheme.P.Error;
        private static Color NeutralColor => UiTheme.P.Neutral;

        // キー入力待ち中か(Game がスクショキーを無視するのに使う)
        public bool IsWaitingKey => waitingTarget != null || presetNaming >= 0;

        // ===== 閉じるときの確認ダイアログ =====
        private bool confirmOpen = false;

        // ===== プレイヤーデータの初期化（11/12 たけらい） =====
        //   一般 →「プレイヤーデータ」：データを選ぶ →［全部初期化］か［選んで初期化］
        //   resetStep：0 出していない、1 消す物をチェックで選ぶ、2 「本当に消しますか？」
        private int resetSlot = 1;
        private int resetStep;
        private bool resetAll;                       // ［全部初期化］から来たか（確認で Esc を押したとき、どこに戻るか）
        private PlayerDataParts resetParts;
        private List<DrumPart> confirmEmptyParts = new();

        // クリックを全部無効にするための「大きさ0の範囲」
        private static readonly Rectangle NoClickArea = new Rectangle(0, 0, 0, 0);

        // ===== スキン設定用 =====

        // 「フォルダを選択」が押された(描画中に画面を出すと危ないので、次の Update で出す)
        private bool pickFolderRequested = false;

        // 選んでいるスキンのプレビュー(UI/SkinPreview.cs)
        private readonly SkinPreview skinPreview = new();

        // ===== 開閉 =====

        public void Open(bool fullScreen = false)
        {
            this.fullScreen = fullScreen;
            IsOpen = true;
            animTime = 0f;
            closing = false;
            switchTime = 99f;
            indicatorY = -1f;
            Gui.Reset();
            selectedCategory = Category.General;
            scroll = scrollTarget = contentHeight = 0f;
            scrollDragging = false;
            unsavedOpen = false;
            savedSkin = ConfigManager.Copy(ConfigManager.Skin);
            visibleSkinNames = ConfigManager.Skin.GetSkinList();
            if (fullScreen)
            {
                savedGame = ConfigManager.Copy(ConfigManager.Game);
                savedSound = ConfigManager.Copy(ConfigManager.Sound);
                savedKey = ConfigManager.Copy(ConfigManager.Key);
                draftGame = ConfigManager.Copy(savedGame);
                draftSound = ConfigManager.Copy(savedSound);
                draftKey = ConfigManager.Copy(savedKey);
                draftSkin = ConfigManager.Copy(savedSkin);
            }
            LoadVolumes(); // 音量のバーは、今の設定の値から始める

            // スキンは適用・変更したときだけ読み直す。開くたびの全画像再読み込みは重い。
        }

        // 閉じる(すべって出ていく動きを始める。動き終わったら FinishClose で片付ける)
        public void Close()
        {
            if (closing) return;
            closing = true;
            animTime = 0f;
            Gui.Reset();
        }

        private void FinishClose()
        {
            closing = false;
            bool wasFullScreen = fullScreen;
            // F1 で選んだだけのスキン(［スキンを適用］を押していない)は、閉じたら元に戻す
            //   11/26 たけらい「スキン入れたときにもう反映されてる」：前は閉じたときに読み直して切り替えていた
            if (!wasFullScreen && savedSkin != null && !ConfigManager.Same(savedSkin, ConfigManager.Skin))
            {
                ConfigManager.Skin.SkinsFolder = savedSkin.SkinsFolder;
                ConfigManager.Skin.SkinName = savedSkin.SkinName;
                ConfigManager.Skin.Fix();
            }
            IsOpen = false;
            Gui.Reset();
            waitingTarget = null;
            scrollDragging = false;
            messageTimer = 0f;
            confirmOpen = false;
            unsavedOpen = false;
            resetStep = 0;
            fullScreen = false;
            skinPreview.Unload(); // プレビューの画像を片付ける
            draftGame = savedGame = null;
            UiTheme.Apply(ConfigManager.Game);   // 全画面の設定で色を試して、取り消したときに戻す
            draftSound = savedSound = null;
            draftKey = savedKey = null;
            draftSkin = savedSkin = null;
            if (!wasFullScreen)
            {
                ConfigManager.SaveAll(); // 次回起動しても選んだ設定を使う(スキンは［スキンを適用］したものだけ)
            }
        }

        // 閉じたいとき(キーが無い場所があれば確認ダイアログを出す)
        public void RequestClose()
        {
            if (HasPendingChanges)
            {
                unsavedOpen = true;
                Gui.Reset();
                return;
            }
            List<DrumPart> emptyParts = ActiveKey.GetEmptyParts();
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

        // 全画面の［適用］：値をまとめて反映して保存する
        private void ApplyFullSettings()
        {
            if (!HasPendingChanges) return;
            GameConfig beforeGame = ConfigManager.Game;
            SoundConfig beforeSound = ConfigManager.Sound;
            SkinConfig beforeSkin = ConfigManager.Skin;

            draftGame!.Fix();
            draftSound!.Fix();
            draftKey!.Fix();
            draftSkin!.Fix();
            ConfigManager.Replace(draftGame, draftSound, draftKey, draftSkin);

            if (beforeGame.WindowWidth != draftGame.WindowWidth || beforeGame.WindowHeight != draftGame.WindowHeight
                || beforeGame.Fullscreen != draftGame.Fullscreen)
                Screen.RequestApply();
            if (beforeGame.VSync != draftGame.VSync || beforeGame.TargetFps != draftGame.TargetFps)
                Screen.ApplyFrameSettings();
            // 描画方式は再起動しないと変わらない → 今動いている方式と違えば「再起動しますか？」を出す
            bool backendChanged = beforeGame.GraphicsBackend != draftGame.GraphicsBackend && draftGame.GraphicsBackend != GraphicsBackend.Active;
            if ((beforeSkin.SkinsFolder != draftSkin.SkinsFolder || beforeSkin.SkinName != draftSkin.SkinName) && !InPlay)
                SkinManager.Reload();

            bool outputChanged = beforeSound.OutputType != draftSound.OutputType
                || beforeSound.BassBufferMs != draftSound.BassBufferMs
                || beforeSound.WasapiBufferMs != draftSound.WasapiBufferMs
                || beforeSound.AsioDevice != draftSound.AsioDevice;
            if (outputChanged && !InPlay) AudioManager.Restart();
            if (!ConfigManager.Same(beforeSound, draftSound)) SceneManager.Current?.ApplyVolume();

            savedGame = ConfigManager.Copy(draftGame);
            savedSound = ConfigManager.Copy(draftSound);
            savedKey = ConfigManager.Copy(draftKey);
            savedSkin = ConfigManager.Copy(draftSkin);
            // 適用後も画面は開いたまま。次の編集が本番の設定へ直接入らないようコピーを作り直す
            draftGame = ConfigManager.Copy(savedGame);
            draftSound = ConfigManager.Copy(savedSound);
            draftKey = ConfigManager.Copy(savedKey);
            draftSkin = ConfigManager.Copy(savedSkin);
            ShowMessage("設定を適用して保存しました", SuccessColor);
            if (backendChanged) restartOpen = true;
        }

        // 「再起動しますか？」の［あとで］・Esc
        private void RestartLater()
        {
            restartOpen = false;
            ShowMessage($"{GraphicsBackend.Name(ConfigManager.Game.GraphicsBackend)} は次回の起動時に適用します", SuccessColor);
        }

        // 描画方式を変えたあとの「再起動しますか？」（11/22 たけらい）
        private void DrawRestartDialog()
        {
            Raylib.DrawRectangle(0, 0, Screen.Width, Screen.Height, new Color(0, 0, 0, 175));
            Rectangle box = new Rectangle(530, 360, 860, 330);
            UiTheme.FillPanel(box, 20);
            SmoothShapes.Border(box, 20, 1.5f, Gui.BorderColor);

            FontManager.Draw("再起動しますか？", box.X + 50, box.Y + 48, 38, Gui.TextColor);
            FontManager.Draw($"描画方式（{GraphicsBackend.Name(ConfigManager.Game.GraphicsBackend)}）は、再起動すると切り替わります。",
                box.X + 50, box.Y + 118, 26, Gui.SubTextColor);

            Rectangle later = new Rectangle(box.X + 42, box.Y + 230, 365, 65);
            Rectangle yes = new Rectangle(box.X + 453, box.Y + 230, 365, 65);
            if (Gui.Button(later, "あとで")) RestartLater();
            else if (Gui.PrimaryButton(yes, "はい"))
            {
                restartOpen = false;
                Game.RequestRestart();
            }
        }

        private void DiscardAndClose()
        {
            draftGame = ConfigManager.Copy(ConfigManager.Game);
            draftSound = ConfigManager.Copy(ConfigManager.Sound);
            draftKey = ConfigManager.Copy(ConfigManager.Key);
            draftSkin = ConfigManager.Copy(ConfigManager.Skin);
            unsavedOpen = false;
            RequestClose();
        }

        // ===== 更新 =====

        public void Update(float dt)
        {
            float step = Math.Min(dt, 1f / 30f); // 重いフレームで動きが飛ばないように
            animTime += step;
            switchTime += step;
            if (closing)
            {
                if (animTime >= CloseSeconds) FinishClose();
                return;
            }
            if (IsAnimating) return; // すべりこんでいる間は、何も受け付けない

            blinkTimer += dt;
            if (messageTimer > 0f) messageTimer -= dt;
            if (toastAge >= 0f && (toastAge += dt) > ToastIn + ToastHold + ToastOut) toastAge = -1f;

            // プリセットの名前を入れている間（Esc でやめる。変換中の Esc は IME に渡す）
            if (presetNaming >= 0)
            {
                if (InputManager.IsBackPressed()) { if (!WindowsIme.IsComposing) presetNaming = -1; return; }
                if (presetNameInput.Update()) FinishPresetName();
                return;
            }
            UpdateSecret(dt);

            // 「フォルダを選択」が押されていたら、Windowsの選択画面を出す
            if (pickFolderRequested)
            {
                pickFolderRequested = false;
                PickSkinsFolder();
                return;
            }

            // 未保存の確認は Esc で閉じて、編集中の画面に戻る
            if (resetStep != 0)
            {
                // 確認から Esc：選ぶ画面に戻る（［全部初期化］から来たときは閉じる）
                if (InputManager.IsBackPressed()) resetStep = resetStep == 2 && !resetAll ? 1 : 0;
                return;
            }
            if (unsavedOpen)
            {
                if (InputManager.IsBackPressed()) unsavedOpen = false;
                return;
            }
            if (restartOpen)
            {
                if (InputManager.IsBackPressed()) RestartLater();   // Esc は「あとで」と同じ
                return;
            }

            // キーの確認ダイアログ中は、Escで「設定に戻る」
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
                    KeyConfig keyConfig = ActiveKey;

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
            bool animating = IsAnimating;
            if (animating) Gui.SetMouseInputEnabled(false); // すべっている間はマウスにも反応しない
            Gui.BeginFrame();

            Rectangle content = new Rectangle(SidebarWidth + 18, HeaderHeight + 16,
                CurrentWidth - SidebarWidth - 42, Screen.Height - HeaderHeight - HintHeight - 32);
            if (!confirmOpen && !unsavedOpen && !restartOpen && resetStep == 0 && !animating) UpdateScroll(content);

            // 後ろを暗くする(開き具合に合わせて)。全画面は開ききったら後ろが見えないので、動いている間だけ
            float open = OpenAmount();
            if (!fullScreen) Raylib.DrawRectangle(0, 0, Screen.Width, Screen.Height, new Color(0, 0, 0, (int)(205 * open)));
            else if (animating) Raylib.DrawRectangle(0, 0, Screen.Width, Screen.Height, new Color(0, 0, 0, (int)(150 * open)));

            // 全画面は右から、パネルは左からすべりこむ(全部をまとめて横にずらして描く)
            float slideX = fullScreen ? Screen.Width * (1f - open) : -CurrentWidth * (1f - open);
            Rlgl.PushMatrix();
            Rlgl.Translatef(slideX, 0f, 0f);

            UiTheme.FillBackground(BackArea);   // 11/26 背景も斜めのグラデーション（全画面は画面全体、パネルはパネルの幅）
            UiTheme.FillPanel(PanelArea, 0f);   // 11/26 パネルは斜めのグラデーション（UiTheme）
            Raylib.DrawLine((int)SidebarWidth, (int)HeaderHeight, (int)SidebarWidth,
                Screen.Height - (int)HintHeight, Gui.BorderColor);

            // はみ出しを切る範囲は、横にずらした分も足す(切る範囲は Translatef の影響を受けないので)
            Screen.BeginScissorMode((int)(content.X + slideX), (int)content.Y, (int)content.Width, (int)content.Height);
            Gui.SetClip(confirmOpen || unsavedOpen || restartOpen || resetStep != 0 ? NoClickArea : content);

            // カテゴリを変えた直後：中身を 8px 下から戻す(ToScreenY が switchShift を足す。#057)
            float swEase = Math.Clamp((switchTime - SwitchDelay) / SwitchSeconds, 0f, 1f);   // #057：中身は等速でじわっと
            switchShift = SwitchShift * (1f - swEase);   // 下から上がる

            cursorY = 18;
            tips.Clear();
            hoveredTip = -1;
            rowValid = false;
            if (fullScreen)
            {
                switch (selectedCategory)
                {
                    case Category.General: DrawGeneralSection(); break;
                    case Category.Window: DrawWindowSection(); break;
                    case Category.Sound: DrawSoundSection(); break;
                    case Category.Play: DrawPlaySection(); break;
                    case Category.Key: DrawKeySection(); break;
                    case Category.Skin: DrawSkinSection(); break;
                    case Category.Debug: DrawDebugSection(); break;
                }
            }
            else
            {
                DrawGeneralSection();
                DrawWindowSection();
                DrawSoundSection();
                DrawPlaySection();
                DrawKeySection();
                DrawSkinSection();
                if (ConfigManager.Game.DebugMenu) DrawDebugSection();
            }
            EndCard();
            contentHeight = cursorY + 40;
            switchShift = 0f;

            // カテゴリを変えた直後：上に背景と同じ色をうすくしながら重ねて、中身がじわっと出てくるように見せる
            if (swEase < 1f)
                UiTheme.FillPanelPart(PanelArea, content, 1f - swEase);

            Gui.ResetClip();
            Screen.EndScissorMode();
            DrawScrollbar(content);

            if (confirmOpen || unsavedOpen || restartOpen || resetStep != 0 || presetNaming >= 0) Gui.SetClip(NoClickArea);
            DrawHeader();
            DrawSidebar();
            DrawHint();
            Gui.ResetClip();
            if (!confirmOpen && !unsavedOpen && !restartOpen && resetStep == 0) DrawTips();

            DrawMessage();

            if (resetStep != 0) DrawResetDialog();
            else if (confirmOpen) DrawConfirmDialog();
            else if (unsavedOpen) DrawUnsavedDialog();
            else if (restartOpen) DrawRestartDialog();
            else if (presetNaming >= 0) presetNameInput.Draw();
            else Gui.DrawPopups();

            Rlgl.PopMatrix();
            DrawToast();
            if (animating) Gui.SetMouseInputEnabled(true);
        }

        private void DrawScrollbar(Rectangle content)
        {
            if (contentHeight <= content.Height) return;
            float max = contentHeight - content.Height;
            Rectangle track = new Rectangle(content.X + content.Width - 12, content.Y, 10, content.Height);
            SmoothShapes.Rounded(track, 1f, 8, Gui.BorderColor);
            float thumbHeight = Math.Max(64, track.Height * content.Height / contentHeight);
            Rectangle thumb = new Rectangle(track.X, track.Y + (track.Height - thumbHeight) * scroll / max,
                track.Width, thumbHeight);
            SmoothShapes.Rounded(thumb, 1f, 8, Gui.AccentColor);
        }

        // ===== 各カテゴリ =====

        // デバッグ（裏コマンドで出したときだけ。11/26 たけらい「デバッグ系の機能を設定にまとめてオンオフ」）
        private void DrawDebugSection()
        {
            Section(Category.Debug, "デバッグ");
            GameConfig game = ActiveGame;

            SubHeader("記録");
            game.DebugLog = CheckboxRow("デバッグ記録（CSV）", game.DebugLog);
            NoteRow(DebugLog.Active && DebugLog.SessionFolder != null
                ? $"記録中：Logs/{Path.GetFileName(DebugLog.SessionFolder)}/ の Debug（frames・events）と Input（input）"
                : "ON にすると Logs/ に、毎フレームの重さ・できごと・入力・キャラの読みこみを記録します");

            SubHeader("画面に出す");
            game.ShowPerfOverlay = CheckboxRow("重さの内訳を画面に出す", game.ShowPerfOverlay);
            game.ShowCursorPosition = CheckboxRow("マウスの位置の座標を出す", game.ShowCursorPosition);

            SubHeader("演奏中のキー");
            game.DebugPlayKeys = CheckboxRow("デバッグ用のキーを使う", game.DebugPlayKeys);
            NoteRow("F3 デバッグ表示・F6 入力判定・F7 60打制限・F8 入力診断・F10 見た目（段位道場の F3 は別）");

            NoteRow("このカテゴリを隠すには、もう一度 d・e・b・u・g と打つか、上の「設定」を7回クリック");
        }

        private void DrawGeneralSection()
        {
            Section(Category.General, "一般");

            GameConfig game = ActiveGame;
            game.ShowFps = CheckboxRow("FPS表示", game.ShowFps);
            SubHeader("記録表示");
            game.RecordGroup = (RecordGroup)DropdownRow("recordGroup", "王冠・ランクの集計", new[] { "かんたん", "ふつう", "むずかしい", "おに", "おに＋裏" }, (int)game.RecordGroup);
            NoteRow("表示する獲得数の難易度です。曲ごとの記録は難易度別に保存します。");

            // ----- Discord(「プレイ中」の表示) -----
            SubHeader("Discord");
            game.DiscordPresence = CheckboxRow("Discord に遊んでいる内容を出す", game.DiscordPresence);
            game.DiscordHideSongTitle = CheckboxRow("曲名を出さない（難易度だけ出す）", game.DiscordHideSongTitle);
            NoteRow(Platform.DiscordPresence.CurrentStatus switch
            {
                Platform.DiscordPresence.Status.Connected => "Discord とつながっています",
                Platform.DiscordPresence.Status.Waiting => "Discord を探しています（Discord のアプリを起動してください）",
                Platform.DiscordPresence.Status.NoId => "Application ID がまだ入っていません（Platform/DiscordPresence.cs）",
                _ => "OFF",
            });

            // ----- 曲(前は選曲画面の上にあったボタン) -----
            SubHeader("曲");
            string songText = SongList.IsScanning
                ? (SongList.ScanTotal < 0 ? "曲ファイルを探しています…" : $"読み込み中…　{SongList.ScanDone} / {SongList.ScanTotal}")
                : $"{SongList.SongCount}曲" + (SongList.ErrorCount > 0 ? $"（読めなかった曲 {SongList.ErrorCount}）" : "");
            FontManager.Draw(songText, LabelX, ToScreenY(cursorY) + 6, 24, SongList.IsScanning ? Gui.AccentColor : Gui.TextColor);
            cursorY += 44;

            float y = ToScreenY(cursorY);
            float buttonWidth = (RightEdge - LabelX - 10) / 2f;
            if (Gui.Button(new Rectangle(LabelX, y, buttonWidth, 40), "曲の一覧を更新") && !SongList.IsScanning)
            {
                SongList.StartScan();
                ShowMessage("曲を読み込み直しています", SuccessColor);
            }
            if (Gui.Button(new Rectangle(LabelX + buttonWidth + 10, y, buttonWidth, 40), "Songs フォルダを開く"))
            {
                OpenSongsFolder();
            }
            cursorY += 52;
            NoteRow("※ 曲を入れたり消したりしたら［曲の一覧を更新］を押してください");

            // ----- プレイヤーデータの初期化 -----
            SubHeader("プレイヤーデータ");
            float slotWidth = (RightEdge - LabelX - 20) / 3f;
            y = ToScreenY(cursorY);
            for (int slot = 1; slot <= 3; slot++)
            {
                string name = PlayerDataStore.HasName(slot) ? PlayerDataStore.DisplayName(slot) : "（新しく遊ぶ）";
                Rectangle box = new Rectangle(LabelX + (slot - 1) * (slotWidth + 10), y, slotWidth, 44);
                bool clicked = slot == resetSlot ? Gui.PrimaryButton(box, $"{slot}：{name}") : Gui.Button(box, $"{slot}：{name}");
                if (clicked) resetSlot = slot;
            }
            cursorY += 56;
            y = ToScreenY(cursorY);
            if (Gui.Button(new Rectangle(LabelX, y, buttonWidth, 40), "全部初期化"))
            {
                resetAll = true;
                resetParts = PlayerDataParts.All;
                resetStep = 2;
                Gui.Reset();
            }
            if (Gui.Button(new Rectangle(LabelX + buttonWidth + 10, y, buttonWidth, 40), "選んで初期化"))
            {
                resetAll = false;
                resetParts = PlayerDataParts.None;
                resetStep = 1;
                Gui.Reset();
            }
            cursorY += 52;
            NoteRow("上でデータを選んでから押します。消す前に確認が出ます。消したデータは元に戻せません");
        }

        // プレイヤーデータの初期化の確認（1：消す物を選ぶ、2：本当に消すか）
        private void DrawResetDialog()
        {
            Raylib.DrawRectangle(0, 0, Screen.Width, Screen.Height, new Color(0, 0, 0, 175));
            string who = $"データ{resetSlot}（{(PlayerDataStore.HasName(resetSlot) ? PlayerDataStore.DisplayName(resetSlot) : "新しく遊ぶ")}）";
            if (resetStep == 1)
            {
                Rectangle box = new Rectangle(510, 200, 900, 680);
                UiTheme.FillPanel(box, 20);
                SmoothShapes.Border(box, 20, 1.5f, Gui.BorderColor);
                FontManager.Draw($"{who} の初期化", box.X + 50, box.Y + 40, 36, Gui.TextColor);
                FontManager.Draw("消す物にチェックを入れてください", box.X + 50, box.Y + 96, 24, Gui.SubTextColor);
                float y = box.Y + 150;
                foreach (var (part, label) in PlayerDataReset.Items)
                {
                    bool on = Gui.Checkbox(new Rectangle(box.X + 50, y, box.Width - 100, 44), label, resetParts.HasFlag(part));
                    resetParts = on ? resetParts | part : resetParts & ~part;
                    y += 56;
                }
                Rectangle cancel = new Rectangle(box.X + 50, box.Y + box.Height - 100, 380, 62);
                Rectangle next = new Rectangle(box.X + box.Width - 430, box.Y + box.Height - 100, 380, 62);
                if (Gui.Button(cancel, "やめる")) resetStep = 0;
                else if (resetParts != PlayerDataParts.None)
                {
                    if (Gui.PrimaryButton(next, "次へ")) { resetStep = 2; Gui.Reset(); }
                }
                else
                {
                    SmoothShapes.Rectangle(next, 10f, Gui.BoxColor);
                    Gui.DrawCentered("次へ", next, 26, Gui.MutedTextColor);
                }
                return;
            }

            Rectangle confirm = new Rectangle(510, 300, 900, 480);
            UiTheme.FillPanel(confirm, 20);
            SmoothShapes.Border(confirm, 20, 1.5f, Gui.BorderColor);
            FontManager.Draw("！ 本当に初期化しますか？", confirm.X + 50, confirm.Y + 40, 36, ErrorColor);
            FontManager.Draw(who, confirm.X + 50, confirm.Y + 108, 30, Gui.TextColor);
            string what = resetParts == PlayerDataParts.All ? "全部（名前・着せ替え・記録・お気に入り・設定）"
                : string.Join("・", PlayerDataReset.Items.Where(i => resetParts.HasFlag(i.Part)).Select(i => i.Label.Split('（')[0]));
            float used = SettingTexts.DrawWrapped("消す物：" + what, confirm.X + 50, confirm.Y + 160, confirm.Width - 100, 26, Gui.TextColor, 36);
            FontManager.Draw("消したデータは元に戻せません。", confirm.X + 50, confirm.Y + 172 + used, 24, ErrorColor);
            FontManager.Draw("このデータで遊んでいる途中なら、エントリー画面に戻ります。", confirm.X + 50, confirm.Y + 208 + used, 24, Gui.SubTextColor);
            Rectangle back = new Rectangle(confirm.X + 50, confirm.Y + confirm.Height - 100, 380, 62);
            Rectangle run = new Rectangle(confirm.X + confirm.Width - 430, confirm.Y + confirm.Height - 100, 380, 62);
            if (Gui.PrimaryButton(back, "やめる")) resetStep = resetAll ? 0 : 1;
            else if (Gui.Button(run, "初期化する")) RunPlayerDataReset();
        }

        private void RunPlayerDataReset()
        {
            resetStep = 0;
            int slot = resetSlot;
            bool inUse = LocalSession.Players.Any(side => LocalSession.Profiles[side] == slot);
            try
            {
                PlayerDataReset.Run(slot, resetParts);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Text.Json.JsonException)
            {
                Console.WriteLine($"[PlayerData] データ{slot} を初期化できませんでした: {ex.Message}");
                ShowMessage("初期化できませんでした：" + ex.Message, ErrorColor);
                return;
            }
            NamePlate.Clear();
            SongSelectScene.ResetForProfile();
            // このデータで遊んでいる途中・データを選んでいる途中なら、エントリー画面からやり直す（消えたデータのまま続けない）
            if (inUse || SceneManager.Current is EntryScene) SceneManager.Simple(new EntryScene());
            ShowMessage(inUse ? $"データ{slot} を初期化しました（エントリー画面に戻ります）" : $"データ{slot} を初期化しました", SuccessColor);
        }

        // Songs フォルダをエクスプローラーで開く(無ければ作る)
        private static void OpenSongsFolder()
        {
            try
            {
                Directory.CreateDirectory(SongList.SongsFolder);
                Process.Start(new ProcessStartInfo { FileName = SongList.SongsFolder, UseShellExecute = true });
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception)
            {
                Console.WriteLine($"[Songs] フォルダを開けませんでした: {ex.Message}");
            }
        }

        private void DrawWindowSection()
        {
            Section(Category.Window, "画面");

            GameConfig game = ActiveGame;
            bool windowChanged = false;

            // UI の色（11/26 たけらい）。押したらその場で色が変わる（全画面の設定でも、閉じて取り消せば元に戻る）
            int themeIndex = (int)UiTheme.Parse(game.UiTheme);
            int newThemeIndex = DropdownRow("uiTheme", "UIの色", UiThemeNames, themeIndex);
            if (newThemeIndex >= 0 && newThemeIndex != themeIndex)
            {
                game.UiTheme = ((UiThemeKind)newThemeIndex).ToString();
                UiTheme.Apply(game);
            }
            HelpTip(SettingTexts.UiTheme);

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
            if (!game.Fullscreen) HelpTip(SettingTexts.Window); // オフのとき(ウィンドウ)の説明

            // フルスクリーンの説明（方式はえらべない。OpenTaiko と同じ本物のフルスクリーンだけ）
            if (game.Fullscreen)
            {
                HelpTip(SettingTexts.Fullscreen);
                HelpTip(SettingTexts.FullscreenNote);
            }

            // 描画方式（再起動で反映。今の起動で使っているものと違えば、そう書く）
            int backendIndex = Array.IndexOf(BackendTypes, game.GraphicsBackend);
            int newBackendIndex = DropdownRow("graphicsBackend", "描画方式", BackendNames, backendIndex);
            if (newBackendIndex >= 0 && newBackendIndex != backendIndex) game.GraphicsBackend = BackendTypes[newBackendIndex];

            // ［描画方式を適用］：選んだ方式を保存して「再起動しますか？」を出す（11/22 たけらい：選ぶ所のすぐ下に）
            Rectangle applyBackend = new Rectangle(LabelX, ToScreenY(cursorY), RightEdge - LabelX, 44);
            if (game.GraphicsBackend != GraphicsBackend.Active)
            {
                if (Gui.PrimaryButton(applyBackend, "描画方式を適用"))
                {
                    ConfigManager.Game.GraphicsBackend = game.GraphicsBackend;
                    if (savedGame != null) savedGame.GraphicsBackend = game.GraphicsBackend;   // 全画面の［適用］で二重に聞かない
                    ConfigManager.SaveAll();
                    restartOpen = true;
                    Gui.Reset();
                }
            }
            else Gui.DrawCentered($"{GraphicsBackend.Name(GraphicsBackend.Active)} で動いています", applyBackend, 22, Gui.SubTextColor);
            cursorY += 56;
            HelpTip(SettingTexts.GraphicsBackend);
            if (GraphicsBackend.FallbackReason != null) NoteRow(GraphicsBackend.FallbackReason);

            // 垂直同期
            bool vsync = CheckboxRow("垂直同期", game.VSync);
            if (vsync != game.VSync)
            {
                game.VSync = vsync;
                if (!fullScreen) Screen.ApplyFrameSettings();
            }
            HelpTip(SettingTexts.VSync);

            // 目標フレームレート
            int fps = SliderRow("targetFps", "目標フレームレート", game.TargetFps, 0, 360);
            if (fps != game.TargetFps)
            {
                game.TargetFps = fps;
                game.Fix(); // 1〜29 は 30 にする
                if (!fullScreen) Screen.ApplyFrameSettings();
            }
            HelpTip(SettingTexts.TargetFps);

            // ウィンドウの大きさやフルスクリーンは、次のフレームの最初に反映する
            if (windowChanged && !fullScreen)
            {
                Screen.RequestApply();
            }
        }

        private void DrawSoundSection()
        {
            Section(Category.Sound, "音");

            DrawAudioPresetRows();
            SoundConfig sound = ActiveSound;

            // バーを動かしたらすぐ反映する（11/26 たけらい「音量適用押してなくてもされるようにしたい」）
            //   F1 のパネル：今鳴っている音にすぐかけ直す。保存はパネルを閉じたとき（FinishClose の SaveAll）
            //   全画面の設定：下書きに入れて、［保存］でまとめて反映する（今までどおり）
            volumeMaster = SliderRow("master", "Master", volumeMaster, 0, 100);
            volumeSe = SliderRow("se", "SE", volumeSe, 0, 100);
            volumeMusic = SliderRow("music", "Music", volumeMusic, 0, 100);
            volumeBgm = SliderRow("bgm", "BGM", volumeBgm, 0, 100);
            volumeVoice = SliderRow("voice", "Voice", volumeVoice, 0, 100);

            if (fullScreen)
            {
                sound.MasterVolume = volumeMaster;
                sound.SeVolume = volumeSe;
                sound.MusicVolume = volumeMusic;
                sound.BgmVolume = volumeBgm;
                sound.VoiceVolume = volumeVoice;
            }

            bool volumePending = volumeMaster != sound.MasterVolume || volumeSe != sound.SeVolume
                || volumeMusic != sound.MusicVolume || volumeBgm != sound.BgmVolume || volumeVoice != sound.VoiceVolume;
            if (!fullScreen && volumePending)
                ApplyVolumes();

            DrawAudioOutputRows(sound);
        }

        // ----- 音量(バーの値。F1 のパネルでは動かしたらすぐ設定に入れる) -----
        private int volumeMaster, volumeSe, volumeMusic, volumeBgm, volumeVoice;

        // 今の設定の音量を、バーの値に読み込む(パネルを開いたとき)
        private void LoadVolumes()
        {
            SoundConfig sound = ActiveSound;
            volumeMaster = sound.MasterVolume;
            volumeSe = sound.SeVolume;
            volumeMusic = sound.MusicVolume;
            volumeBgm = sound.BgmVolume;
            volumeVoice = sound.VoiceVolume;
        }

        // バーの値を設定に入れて、今鳴っている曲(選曲画面のプレビュー・リザルトの曲)にかけ直す（保存はパネルを閉じたとき）
        //   効果音と声は、鳴らすたびに設定を読むので、次に鳴る音から変わる
        private void ApplyVolumes()
        {
            SoundConfig sound = ActiveSound;
            sound.MasterVolume = volumeMaster;
            sound.SeVolume = volumeSe;
            sound.MusicVolume = volumeMusic;
            sound.BgmVolume = volumeBgm;
            sound.VoiceVolume = volumeVoice;
            sound.Fix();
            LoadVolumes(); // 直した値(範囲の外だったときなど)をバーにも戻す
            SceneManager.Current?.ApplyVolume();
        }

        // ----- 音の出力(出力方式・バッファ) -----

        // プルダウンの中身(AudioOutputType の番号と同じ並び)
        private static readonly string[] OutputTypeNames = { "BASS（標準）", "DirectSound", "WASAPI 共有", "WASAPI 排他", "ASIO" };

        // 方式ごとの説明
        private static string GetOutputDescription(AudioOutputType type) => type switch
        {
            AudioOutputType.Bass => "BASS におまかせで鳴らします（中では WASAPI 共有）。いちばん安定しています。Windows の音声エンジンを通る分の遅れ（ふつう約20ms）も、自動で引いて合わせます。",
            AudioOutputType.DirectSound => "昔の方式です。ほかの方式でうまく鳴らないときに使ってください。遅延は少し増えます。Windows の音声エンジンの遅れ（ふつう約20ms）は、自動で引いて合わせます。",
            AudioOutputType.WasapiShared => "Windows の普通の出し方です。ほかのアプリの音も一緒に鳴ります。Windows の音声エンジンの遅れ（ふつう約20ms）は、自動で引いて合わせます。※ bassmix.dll と basswasapi.dll が必要です",
            AudioOutputType.WasapiExclusive => "TakeTaikoSimulator が音の出口を独り占めします。遅延がかなり減るかわりに、起動中はほかのアプリの音が出なくなります。※ bassmix.dll と basswasapi.dll が必要です",
            AudioOutputType.Asio => "ASIO ドライバで出します。いちばん速いですが、対応した機器（オーディオインターフェースなど）が必要です。※ bassmix.dll と bassasio.dll が必要です",
            _ => "",
        };

        // 演奏画面（一時停止中）から開いたとき：スキンと音の出し方はさわれない
        //   11/14 たけらい「演奏画面のポーズ中は F1 で設定出せるようにするか」→ A（その2つだけ灰色にする）
        //   スキンを変えると絵を全部読み直し、音の出し方を変えると音の仕組みを作り直すので、演奏中の曲や絵がこわれる
        private static bool InPlay => SceneManager.Current is PlayScene;
        private const string InPlayNote = "演奏中は変えられないよ（曲が終わってから変えてね）";

        private void DrawAudioOutputRows(SoundConfig sound)
        {
            cursorY += 10;
            SubHeader("音の出力");
            if (InPlay)
            {
                NoteRow($"今の出力：{AudioManager.Status}");
                FontManager.Draw(InPlayNote, LabelX, ToScreenY(cursorY), 22, Gui.SubTextColor);
                cursorY += 36;
                return;
            }

            // ----- 出力方式 -----
            int selected = Math.Clamp((int)sound.OutputType, 0, OutputTypeNames.Length - 1);
            sound.OutputType = (AudioOutputType)DropdownRow("outputType", "出力方式", OutputTypeNames, selected);
            HelpTip(GetOutputDescription(sound.OutputType));

            // ----- バッファ(方式ごと) -----
            switch (sound.OutputType)
            {
                case AudioOutputType.Bass:
                case AudioOutputType.DirectSound:
                    sound.BassBufferMs = SliderRow("bassBuffer", "バッファ[ms]", sound.BassBufferMs, AudioManager.BufferMin, AudioManager.BufferMax);
                    HelpTip("0 で自動。小さいほど遅れが減りますが、小さすぎると音がブツブツ切れます");
                    break;

                case AudioOutputType.WasapiShared when AudioManager.CurrentType == AudioOutputType.WasapiShared && AudioManager.LowLatencySharedActive:
                    // 共有の低遅延で鳴っているときは、バッファの長さは Windows が決める（設定の値は使わない）
                    //   11/28 たけらい「いみないの？ｗ」→ 動かせないことが分かるように、灰色の行にする
                    DisabledRow("バッファ[ms]", "自動（Windows が決めます）");
                    HelpTip("共有（低遅延）で鳴らしているので、バッファの長さは Windows とドライバーが決めます。低遅延が使えないパソコンでは、ここで変えられます");
                    break;

                case AudioOutputType.WasapiShared:
                case AudioOutputType.WasapiExclusive:
                    sound.WasapiBufferMs = SliderRow("wasapiBuffer", "バッファ[ms]", sound.WasapiBufferMs, AudioManager.BufferMin, AudioManager.BufferMax);
                    HelpTip("0 で自動。小さいほど遅れが減りますが、小さすぎると音がブツブツ切れます");
                    break;

                case AudioOutputType.Asio:
                    List<string> devices = AudioManager.GetAsioDeviceNames();
                    if (devices.Count == 0)
                    {
                        FontManager.Draw("ASIO の機器が見つかりません（bassasio.dll と ASIO ドライバが必要です）", LabelX, ToScreenY(cursorY), 22, ErrorColor);
                        cursorY += 36;
                    }
                    else
                    {
                        int device = Math.Clamp(sound.AsioDevice, 0, devices.Count - 1);
                        sound.AsioDevice = DropdownRow("asioDevice", "ASIO 機器", devices.ToArray(), device);
                    }
                    HelpTip("バッファの長さは、ASIO の機器の設定画面で変えてください");
                    break;
            }

            // ----- 今の状態 -----
            NoteRow($"今の出力：{AudioManager.Status}");   // 状態の表示は行の下のまま

            // ----- 適用 -----
            // 設定を変えただけでは切り替わらない。［適用］を押したときに、音を準備し直す
            bool pending = !AudioManager.IsAvailable
                || sound.OutputType != AudioManager.CurrentType
                || AudioManager.GetBufferMs(sound, sound.OutputType) != AudioManager.CurrentBufferMs
                || (sound.OutputType == AudioOutputType.Asio && sound.AsioDevice != AudioManager.CurrentAsioDevice);

            string label = pending ? "適用（まだ反映されていません）" : "適用";
            if (!fullScreen && Gui.Button(new Rectangle(LabelX, ToScreenY(cursorY), RightEdge - LabelX, 40), label))
            {
                ApplyAudioOutput();
            }
            if (!fullScreen) cursorY += 52;
        }

        // ［適用］：今の設定で音を準備し直す
        // ※ 設定パネルは演奏中には開けないので、曲や効果音を読み込んだまま作り直すことはない
        private void ApplyAudioOutput()
        {
            SoundConfig sound = ActiveSound;
            AudioManager.Restart();

            bool ok = AudioManager.IsAvailable && AudioManager.CurrentType == sound.OutputType;
            if (ok) ShowMessage($"音の出力を {AudioManager.GetTypeName(AudioManager.CurrentType)} にしました", SuccessColor);
            else ShowMessage(AudioManager.Status, ErrorColor);
        }

        private void DrawPlaySection()
        {
            Section(Category.Play, "演奏");

            GameConfig game = ActiveGame;

            // 音オフセット(判定ごとずらす)
            game.AudioOffsetMs = SliderRow("audioOffset", "音オフセット[ms]", game.AudioOffsetMs, GameConfig.OffsetMin, GameConfig.OffsetMax);
            HelpTip("判定のタイミングごとずらします（判定が合わないとき）");

            // ［タイミングを測る］：メトロノームに合わせて叩いて、音オフセットを決める(Scenes/TimingCalibrationScene.cs)
            //   演奏中は曲を止めることになるので出さない
            if (!InPlay)
            {
                Rectangle measure = new Rectangle(LabelX, ToScreenY(cursorY), RightEdge - LabelX, 44);
                if (Gui.Button(measure, "タイミングを測る（叩いて音オフセットを決める）"))
                {
                    Close();
                    SceneManager.Simple(new TimingCalibrationScene());
                }
                cursorY += 56;
            }

            // 譜面オフセット(見た目だけずらす)
            game.ChartOffsetMs = SliderRow("chartOffset", "譜面オフセット[ms]", game.ChartOffsetMs, GameConfig.OffsetMin, GameConfig.OffsetMax);
            HelpTip("音符の見た目だけずらします（判定は変わりません）");

            HelpTip("どちらもマイナスで早く、プラスで遅くなります");

            // ----- オート演奏の精度(前は選曲画面の演奏オプションにあった) -----
            cursorY += 10;
            SubHeader("オート演奏の精度");
            game.AutoGoodPercent = SliderRow("autoGood", "良[%]", game.AutoGoodPercent, GameConfig.AutoPercentMin, GameConfig.AutoPercentMax);
            game.AutoOkPercent = SliderRow("autoOk", "可[%]", game.AutoOkPercent, GameConfig.AutoPercentMin, GameConfig.AutoPercentMax);
            game.AutoBadPercent = SliderRow("autoBad", "不可[%]", game.AutoBadPercent, GameConfig.AutoPercentMin, GameConfig.AutoPercentMax);
            HelpTip("オートで叩くときの、良・可・不可の割合です。合計が100でなくても割合で使います（全部0なら全部良）");
        }

        private void DrawKeySection()
        {
            Section(Category.Key, "キー");

            DrawKeyPresetRows();
            KeyConfig key = ActiveKey;

            // ----- 叩くキー -----
            SubHeader("叩くキー");

            // キーが無い場所があったら赤字で注意
            if (key.GetEmptyParts().Count > 0)
            {
                FontManager.Draw("！ キーが無い場所があります（このままだと演奏できません）", LabelX, ToScreenY(cursorY), 22, ErrorColor);
                cursorY += 36;
            }

            // 1P の4か所だけ。2P のキーは設定できない（いつも C・V・N・M。11/26 たけらい）
            foreach (DrumPart part in Enum.GetValues<DrumPart>().Take(4))
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
            Section(Category.Skin, "スキン");

            SkinConfig skin = ActiveSkin;
            if (InPlay)
            {
                FontManager.Draw($"今のスキン：{skin.SkinName}", LabelX, ToScreenY(cursorY), 22, Gui.SubTextColor);
                cursorY += 36;
                FontManager.Draw(InPlayNote, LabelX, ToScreenY(cursorY), 22, Gui.SubTextColor);
                cursorY += 36;
                return;
            }

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
            List<string> names = visibleSkinNames;
            if (names.Count == 0)
            {
                Gui.Label("使うスキン", LabelX, ToScreenY(cursorY) + 4, 26);
                FontManager.Draw("スキンがありません", ControlX, ToScreenY(cursorY) + 6, 24, ErrorColor);
                cursorY += RowHeight;
                NoteRow("スキンフォルダにスキン（A-Style など）を入れてください");
                return;
            }

            int index = names.IndexOf(skin.SkinName);
            if (index < 0) index = 0;
            int newIndex = DropdownRow("skinName", "使うスキン", names.ToArray(), index);
            if (newIndex != index && newIndex >= 0)
            {
                skin.SkinName = names[newIndex];
                ShowMessage($"{skin.SkinName} を選びました（［スキンを適用］ですぐ切り替わります）", SuccessColor);
            }

            // ［スキンを適用］：押したらすぐ画像を読み直して切り替える（11/22 たけらい）
            bool skinPending = !SameSkinPath(skin.GetCurrentSkinPath(), SkinManager.CurrentPath);
            Rectangle applySkin = new Rectangle(LabelX, ToScreenY(cursorY), RightEdge - LabelX, 44);
            if (skinPending)
            {
                if (Gui.PrimaryButton(applySkin, "スキンを適用")) ApplySkinNow(skin);
            }
            else Gui.DrawCentered($"{names[index]} を使っています", applySkin, 22, Gui.SubTextColor);
            cursorY += 56;

            // ----- プレビュー(選んでいるスキンの音符。まだ切り替えていないスキンも、そのスキンの画像で見せる) -----
            skinPreview.Load(skin.GetCurrentSkinPath());
            string info = skinPreview.DisplayName;
            if (skinPreview.Version.Length > 0) info += $"　ver.{skinPreview.Version}";
            NoteRow(FitText(info, RightEdge - LabelX, 22));
            if (skinPreview.Description.Length > 0) NoteRow(FitText(skinPreview.Description, RightEdge - LabelX, 22));

            float previewY = ToScreenY(cursorY);
            Rectangle previewBox = new Rectangle(LabelX, previewY, RightEdge - LabelX, 110);
            SmoothShapes.Rounded(previewBox, 0.12f, 8, Gui.BackgroundColor);
            if (!skinPreview.Draw(previewBox))
                Gui.DrawCentered("画像を読み込めませんでした", previewBox, 22, ErrorColor);
            cursorY += 122;
        }

        // ===== 行の部品 =====

        // ===== カード(同じ仲間の行を、少し明るい角丸の箱にまとめる) =====
        //   11/12 たけらい「設定画面がごちゃごちゃしてる」→ 見くらべの絵を見て「A(カード＋横並びの切り替え)」
        //   前は、線で区切った行が上から下へずっと並んでいて、どこまでが同じ仲間か分かりにくかった。
        //   見出し(Section)と小見出し(SubHeader)のあとから、次の見出しまでを1つのカードにする。
        //   行は上から順に描いていくので、箱の高さは描き終わるまで分からない。そこで、前のフレームで分かった高さで箱を先に描く
        //   (開いた直後や、行が増えた瞬間の1フレームだけ、箱が無い・高さが古いことがある)
        private const float CardPadTop = 14f, CardPadBottom = 4f, CardGap = 26f, CardRadius = 16f;
        private static Color CardColor => UiTheme.P.Card;          // パネルより少し明るく、ボタンの箱より暗い
        private static Color CardLineColor => UiTheme.P.CardLine;      // カードの中の、行と行の間の線
        private readonly Dictionary<string, float> cardHeights = new();
        private string? cardKey;
        private float cardTop;
        private int cardRows;

        private void BeginCard(string key)
        {
            EndCard();
            cardKey = key;
            cardTop = cursorY;
            cardRows = 0;
            if (cardHeights.TryGetValue(key, out float height) && height > CardPadTop + CardPadBottom + 1f)
                SmoothShapes.Rectangle(new Rectangle(SectionX, ToScreenY(cardTop), RightEdge + 14f - SectionX, height), CardRadius, CardColor);
            cursorY += CardPadTop;
        }

        private void EndCard()
        {
            if (cardKey == null) return;
            bool empty = cursorY - cardTop <= CardPadTop + 1f;
            if (empty) cursorY = cardTop;                 // 中身が無いカードは、場所を取らない
            else cursorY += CardPadBottom;
            cardHeights[cardKey] = cursorY - cardTop;
            cardKey = null;
            if (!empty) cursorY += CardGap;
        }

        // 行と行の間の線(カードの中で2行目からだけ引く。前は全部の行の下に引いていた)
        private void RowDivider(float y)
        {
            if (cardRows++ > 0)
                Raylib.DrawLine((int)LabelX, (int)(y - 6), (int)RightEdge, (int)(y - 6), CardLineColor);
        }

        // 見出し
        private string sectionTitle = "";   // 今描いている見出し（小見出しのカードの名前を、見出しごとに分けるため）
        private void Section(Category category, string title)
        {
            EndCard();
            sectionTitle = title;
            if (category != Category.General && !fullScreen) cursorY += 24;
            sectionPos[category] = cursorY;
            float y = ToScreenY(cursorY);
            Gui.Header(title, SectionX, y);
            FontManager.Draw($"{(int)category + 1:00} / {VisibleCategories.Length:00}", RightEdge - 93, y + 15, 20, Gui.SubTextColor);
            Raylib.DrawLine((int)SectionX, (int)(y + 55), (int)RightEdge,
                (int)(y + 55), Gui.BorderColor);
            cursorY += 72;
            BeginCard("sec:" + title);
        }

        // チェックボックスの行
        private bool CheckboxRow(string label, bool value)
        {
            float y = ToScreenY(cursorY);
            bool result = Gui.Checkbox(new Rectangle(LabelX, y, RightEdge - LabelX, 40), label, value);
            RememberRow(label, y);
            RowDivider(y);
            cursorY += RowHeight;
            return result;
        }

        // スライダーの行
        private int SliderRow(string id, string label, int value, int min, int max)
        {
            float y = ToScreenY(cursorY);
            Gui.Label(label, LabelX, y + 4, 26);
            RememberRow(label, y);

            Rectangle bar = new Rectangle(ControlX, y + 6, BoxX - ControlX - 12, 30);
            Rectangle box = new Rectangle(BoxX, y + 3, RightEdge - BoxX, 36);
            int result = Gui.Slider(id, bar, box, value, min, max);

            RowDivider(y);
            cursorY += RowHeight;
            return result;
        }

        // 動かせない行（灰色の字で、今の状態だけ見せる）
        private void DisabledRow(string label, string value)
        {
            float y = ToScreenY(cursorY);
            FontManager.Draw(label, LabelX, y + 4, 26, Gui.SubTextColor);
            RememberRow(label, y);
            Rectangle box = new Rectangle(ControlX, y + 3, RightEdge - ControlX, 36);
            Gui.DrawCentered(value, box, 22, Gui.SubTextColor);
            RowDivider(y);
            cursorY += RowHeight;
        }

        private static readonly GraphicsBackendType[] BackendTypes = { GraphicsBackendType.DirectX11, GraphicsBackendType.Vulkan, GraphicsBackendType.OpenGL };
        private static readonly string[] BackendNames = { "DirectX 11", "Vulkan", "OpenGL" };

        // プルダウンの行
        private int DropdownRow(string id, string label, string[] options, int selected)
        {
            float y = ToScreenY(cursorY);
            Gui.Label(label, LabelX, y + 4, 26);
            RememberRow(label, y);

            // 2〜4個で字が短いものは、横に並んだ切り替えにする(1回で変えられる)。それ以外はプルダウンのまま
            Rectangle control = new Rectangle(ControlX, y + 1, RightEdge - ControlX, 40);
            int result = Gui.FitsSegmented(control, options)
                ? Gui.Segmented(id, control, options, selected)
                : Gui.Dropdown(id, new Rectangle(ControlX, y + 3, RightEdge - ControlX, 36), options, selected);

            RowDivider(y);
            cursorY += RowHeight;
            return result;
        }

        // ===== ツールチップ（Motion Prompt #054。11/10 たけらい「説明書きは #054 のツールチップがいい」） =====
        // Motion Prompt #054 · MIT License (c) 2026 Shohei Ohtaki · https://showheyohtaki.github.io/motion-prompt/#054
        //   行の下にずらっと並べていた説明文を、項目名の横の「?」に乗せたときだけ黒い吹き出しで出す。
        //   動きは #054 のまま：乗せて 0.1秒 → 0.2秒で出る（easeOutCubic・下から 6px 上がる）、離して 0.1秒 → 0.2秒で消える（等速）。
        //   吹き出し #26282c・角の丸み 8・下向きの三角 16×8・白い字。「?」は青い輪（半径13・太さ2.2）
        //   大きさは 1920×1080 の画面に合わせて、字を 15px → 22px にした（そのぶん吹き出しも広い）
        private const float TipShowDelay = 0.1f, TipShowSeconds = 0.2f, TipHideDelay = 0.1f, TipHideSeconds = 0.2f;
        private const float TipRise = 6f, TipFont = 22f, TipLine = 30f, TipMaxWidth = 520f;
        private static readonly Color TipColor = new(0x26, 0x28, 0x2c, 255);
        private float rowLabelRight, rowY;
        private bool rowValid;
        private readonly List<(Rectangle Icon, string Text)> tips = new();
        private int hoveredTip = -1;           // このフレームでカーソルが乗っている「?」
        private string? tipText;               // 出している（出しかけ・消しかけの）説明
        private Rectangle tipIcon;
        private bool tipHovering;
        private float tipTime, tipAlphaAtLeave;

        // 項目の行を描いたら、そのラベルの右はしを覚える（このあとの HelpTip の「?」を置く場所）
        private void RememberRow(string label, float y)
        {
            rowLabelRight = LabelX + FontManager.Measure(label, 26).X;
            rowY = y;
            rowValid = true;
        }

        // 説明文：直前の項目の横に「?」を出して、乗せたら吹き出し。同じ行に2つ以上なら1つの吹き出しにまとめる
        private void HelpTip(string text)
        {
            if (!rowValid) { NoteRow(text); return; }
            Rectangle icon = new(rowLabelRight + 10f, rowY + 4f, 30f, 30f);
            if (tips.Count > 0 && tips[^1].Icon.Y == icon.Y && tips[^1].Icon.X == icon.X)
            {
                tips[^1] = (icon, tips[^1].Text + "\n" + text);
                return;
            }
            tips.Add((icon, text));
            bool hover = Gui.IsMouseOver(icon) && !Gui.IsPopupOpen;
            if (hover) hoveredTip = tips.Count - 1;
            Vector2 center = new(icon.X + 15f, icon.Y + 15f);
            if (hover) SmoothShapes.Circle(center, 14f, Gui.WithAlpha(Gui.AccentColor, 0.25f));
            SmoothShapes.Ring(center, 11.9f, 14.1f, Gui.AccentColor);
            Gui.DrawCentered("?", icon, 18f, Gui.AccentColor);
        }

        // 吹き出しを描く（中身を全部描いたあと、いちばん上に）
        private void DrawTips()
        {
            float dt = Gui.FrameDt;
            if (hoveredTip >= 0)
            {
                var (icon, text) = tips[hoveredTip];
                if (!tipHovering || tipText != text) { tipHovering = true; tipTime = tipText == text ? TipShowDelay + TipShowSeconds * CurrentTipAlpha() : 0f; }
                tipText = text;
                tipIcon = icon;
            }
            else if (tipHovering)
            {
                tipAlphaAtLeave = CurrentTipAlpha();
                tipHovering = false;
                tipTime = 0f;
            }
            tipTime += dt;

            float a = CurrentTipAlpha();
            if (tipText == null || a <= 0f) { if (!tipHovering) tipText = null; return; }

            List<string> lines = new();
            foreach (string part in tipText.Split('\n')) lines.AddRange(SettingTexts.Wrap(part, TipMaxWidth, TipFont));
            float width = lines.Max(l => FontManager.Measure(l, TipFont).X) + 32f;
            float height = lines.Count * TipLine + 16f;
            float cx = tipIcon.X + tipIcon.Width / 2f;
            float x = Math.Clamp(cx - width / 2f, SidebarWidth + 8f, CurrentWidth - width - 8f);
            bool below = tipIcon.Y - 8f - height < HeaderHeight;   // 上に入らないときは下に出す（三角も上向き）
            float rise = TipRise * (1f - a);
            float y = below ? tipIcon.Y + tipIcon.Height + 8f + rise : tipIcon.Y - 8f - height + rise;

            Color bg = Gui.WithAlpha(TipColor, a);
            Rectangle box = new(x, y, width, height);
            SmoothShapes.Rounded(box, 16f / Math.Min(width, height), 8, bg);
            if (below)
                SmoothShapes.Triangle(new Vector2(cx, y - 8f), new Vector2(cx - 8f, y), new Vector2(cx + 8f, y), bg);
            else
                SmoothShapes.Triangle(new Vector2(cx - 8f, y + height), new Vector2(cx, y + height + 8f), new Vector2(cx + 8f, y + height), bg);
            for (int i = 0; i < lines.Count; i++)
                FontManager.Draw(lines[i], x + 16f, y + 8f + i * TipLine + 3f, TipFont, Gui.WithAlpha(Color.White, a));
        }

        private float CurrentTipAlpha()
        {
            if (tipText == null) return 0f;
            if (tipHovering)
            {
                float s = Math.Clamp((tipTime - TipShowDelay) / TipShowSeconds, 0f, 1f);
                return 1f - (1f - s) * (1f - s) * (1f - s);   // easeOutCubic
            }
            float h = Math.Clamp((tipTime - TipHideDelay) / TipHideSeconds, 0f, 1f);
            return tipAlphaAtLeave * (1f - h);
        }

        // 小さい説明文の行
        private void NoteRow(string text)
        {
            cursorY += SettingTexts.DrawWrapped(text, LabelX, ToScreenY(cursorY),
                RightEdge - LabelX, 22, Gui.SubTextColor, 30) + 6;
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
            // 小見出しは桜色の小さい字。その下から、次の見出しまでが1つのカード
            EndCard();
            FontManager.Draw(text, SectionX + 6, ToScreenY(cursorY), 23, Gui.SakuraColor);
            cursorY += 36;
            // カードの高さは名前で覚えている。11/26「プリセット」が「音」と「キー」の両方にあって、高さが混ざっていた
            //   → 見出しごとに分ける（例：sub:音/プリセット）
            BeginCard("sub:" + sectionTitle + "/" + text);
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
            SmoothShapes.Rounded(header, 0.16f, 8, hover ? Gui.BoxHoverColor : Gui.BoxColor);

            FontManager.Draw(open ? "▼" : "▶", LabelX, y + 11, 20, Gui.SubTextColor);
            FontManager.Draw(KeyConfig.GetDrumPartName(part), LabelX + 34, y + 6, 26, Gui.TextColor);

            // 閉じているときは、右側に登録されているキーをまとめて表示
            if (!open)
            {
                if (keys.Count == 0) FontManager.Draw("未割り当て", ControlX, y + 8, 24, ErrorColor);
                else FontManager.Draw(FitText(string.Join(", ", keys), RightEdge - ControlX - 12, 24),
                    ControlX, y + 8, 24, Gui.SubTextColor);
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
            else if (keys.Count >= KeyConfig.MaxKeysPerPart)
            {
                // 4つ入っていたら追加ボタンの代わりに説明（11/26 たけらい）
                FontManager.Draw($"{KeyConfig.MaxKeysPerPart} つまでです（×で外すと追加できます）", LabelX + 34, addY + 8, 22, Gui.SubTextColor);
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
            SmoothShapes.Rounded(box, 0.18f, 8, Gui.BoxColor);
            Gui.DrawCentered(keyText, box, 24, Gui.SubTextColor);

            cursorY += RowHeight;
        }

        // キー入力待ちの箱(点滅)
        private void DrawWaitingBox(Rectangle area, string text)
        {
            Gui.Glow(area, 10, Gui.AccentColor, 0.8f);
            SmoothShapes.Rectangle(area, 10, Gui.BoxColor);
            SmoothShapes.Border(area, 10, 2, Gui.AccentColor);

            if ((blinkTimer % 1f) < 0.7f)
            {
                Gui.DrawCentered(text, area, 22, Gui.AccentColor);
            }
        }

        // 結果メッセージを出す
        // ===== プリセット（Configs/PresetConfig.cs。11/26 たけらい「音オフセットとかキー配置のプリセットを作ろう」） =====
        //   今選んでいるプリセットに、自動で保存する（ConfigManager.SaveAll → SavePresets）。切り替えたらすぐ反映して保存
        private readonly PlayerNameInput presetNameInput = new();
        private int presetNaming = -1;   // 名前を入れているプリセット：0 キー・1 音・-1 入れていない

        private void DrawAudioPresetRows()
        {
            SubHeader("プリセット");
            PresetConfig p = ConfigManager.Presets;
            int chosen = DropdownRow("audioPreset", "音の出力のプリセット", p.AudioLabels(), p.AudioActive);
            HelpTip("出力方式・バッファ・ASIO の機器・音オフセット・譜面オフセットを、3つまで覚えておけます。選んでいる間に変えた値は、そのプリセットに入ります（イヤホン用・スピーカー用など）");
            if (chosen != p.AudioActive) SwitchAudioPreset(chosen);
            PresetNameButton(1, PresetConfig.Label(p.Audio[p.AudioActive].Name, p.AudioActive));
            SubHeader("音量");   // 音量は、プリセットとは別のカードに（プリセットには入らない）
        }

        private void DrawKeyPresetRows()
        {
            SubHeader("プリセット");
            PresetConfig p = ConfigManager.Presets;
            int chosen = DropdownRow("keyPreset", "キー配置のプリセット", p.KeyLabels(), p.KeyActive);
            HelpTip("叩くキー（1P・2P）を、3つまで覚えておけます。選んでいる間に変えたキーは、そのプリセットに入ります（キーボード用・太鼓コン用など）");
            if (chosen != p.KeyActive) SwitchKeyPreset(chosen);
            PresetNameButton(0, PresetConfig.Label(p.Keys[p.KeyActive].Name, p.KeyActive));
        }

        private void PresetNameButton(int kind, string label)
        {
            Rectangle button = new Rectangle(LabelX, ToScreenY(cursorY), RightEdge - LabelX, 44);
            if (Gui.Button(button, $"名前を変える（今：{label}）"))
            {
                PresetConfig p = ConfigManager.Presets;
                string now = kind == 0 ? p.Keys[p.KeyActive].Name : p.Audio[p.AudioActive].Name;
                presetNameInput.Open(now, "プリセットの名前", $"{PresetConfig.NameLimit}文字まで。空にして Enter で「プリセット○」に戻ります", PresetConfig.NameLimit, allowEmpty: true);
                presetNaming = kind;
            }
            cursorY += 56;
        }

        private void FinishPresetName()
        {
            PresetConfig p = ConfigManager.Presets;
            string name = NamePlateConfig.CleanText(presetNameInput.Value, PresetConfig.NameLimit);
            if (presetNaming == 0) p.Keys[p.KeyActive].Name = name;
            else p.Audio[p.AudioActive].Name = name;
            presetNaming = -1;
            ConfigManager.SavePresets();
            ShowMessage("プリセットの名前を変えました", SuccessColor);
        }

        private void SwitchAudioPreset(int index)
        {
            PresetConfig p = ConfigManager.Presets;
            // 今の値（全画面なら、まだ適用していない下書きも）を、前のプリセットに入れておく
            PresetConfig.CaptureAudio(p.Audio[p.AudioActive], ActiveGame, ActiveSound);
            SoundConfig live = ConfigManager.Sound;
            var before = (live.OutputType, live.BassBufferMs, live.WasapiBufferMs, live.AsioDevice);
            p.AudioActive = index;
            p.LoadAudio(index, ConfigManager.Game, ConfigManager.Sound);
            if (fullScreen)
            {
                p.LoadAudio(index, draftGame!, draftSound!);
                p.LoadAudio(index, savedGame!, savedSound!);   // 「変更があります」にしない（もう保存した）
            }
            ConfigManager.SaveAll();
            bool outputChanged = before != (live.OutputType, live.BassBufferMs, live.WasapiBufferMs, live.AsioDevice);
            if (outputChanged && !InPlay) AudioManager.Restart();
            ShowMessage($"音の出力のプリセットを「{PresetConfig.Label(p.Audio[index].Name, index)}」にしました", SuccessColor);
        }

        private void SwitchKeyPreset(int index)
        {
            PresetConfig p = ConfigManager.Presets;
            p.CaptureKeys(p.KeyActive, ActiveKey);
            p.KeyActive = index;
            p.LoadKeys(index, ConfigManager.Key);
            if (fullScreen)
            {
                p.LoadKeys(index, draftKey!);
                p.LoadKeys(index, savedKey!);
            }
            ConfigManager.SaveAll();
            ShowMessage($"キー配置のプリセットを「{PresetConfig.Label(p.Keys[index].Name, index)}」にしました", SuccessColor);
        }

        // 裏コマンド：d・e・b・u・g を順に打ったか数える
        private void UpdateSecret(float dt)
        {
            if (secretClickTimer > 0f && (secretClickTimer -= dt) <= 0f) secretClickCount = 0;
            if (secretIndex > 0 && (secretKeyTimer -= dt) <= 0f) secretIndex = 0;
            // 文字の入力中・キーの割り当て待ち・ドロップダウンを開いているときは数えない
            if (Gui.IsEditing || Gui.IsPopupOpen || IsWaitingKey) { secretIndex = 0; return; }
            // キーの順番待ちの列（GetKeyPressed）は使わない。ほかの画面のキー入力を横取りしないように
            if (Raylib.IsKeyPressed(SecretWord[secretIndex]))
            {
                secretIndex++;
                secretKeyTimer = SecretKeyGap;
                if (secretIndex == SecretWord.Length) { secretIndex = 0; ToggleDebugMenu(); }
                return;
            }
            if (secretIndex == 0) return;
            for (KeyboardKey k = KeyboardKey.A; k <= KeyboardKey.Z; k++)
            {
                if (!Raylib.IsKeyPressed(k)) continue;
                secretIndex = k == SecretWord[0] ? 1 : 0;   // ちがう文字を打ったら、最初から
                secretKeyTimer = SecretKeyGap;
                break;
            }
        }

        // 上の「設定」の文字をクリックした（7回で裏コマンド）
        private void CountSecretClick()
        {
            if (secretClickCount == 0) secretClickTimer = SecretClickWindow;
            if (++secretClickCount < SecretClicks) return;
            secretClickCount = 0;
            ToggleDebugMenu();
        }

        // 「デバッグ」カテゴリを出す・隠す。すぐファイルに保存する（適用を押さなくても残る）
        private void ToggleDebugMenu()
        {
            bool show = !ConfigManager.Game.DebugMenu;
            ConfigManager.Game.DebugMenu = show;
            if (draftGame != null) draftGame.DebugMenu = show;
            if (savedGame != null) savedGame.DebugMenu = show;   // 「変更があります」にしない
            ConfigManager.SaveGame();
            if (!show && selectedCategory == Category.Debug)
            {
                selectedCategory = Category.General;
                scroll = scrollTarget = contentHeight = 0f;
            }
            clickedCategory = null;
            sectionPos.Clear();
            Gui.Reset();
            MenuSounds.Don();
            ShowMessage(show ? "デバッグのカテゴリを出しました" : "デバッグのカテゴリを隠しました", SuccessColor);
        }

        private void ShowMessage(string text, Color color)
        {
            message = text;
            messageColor = color;
            messageTimer = 3f; // 3秒表示
            // うまくいったお知らせ（適用・保存・切り替えなど）は、トーストでも出す
            if (color.R == SuccessColor.R && color.G == SuccessColor.G && color.B == SuccessColor.B) { toastText = text; toastAge = 0f; }
        }

        // ===== 適用したときのトースト（11/27 たけらい「適応したときわかりずらいからトースト出してほしい」→ ②「端からスッと」） =====
        //   F1 のパネル（画面の左）：左上に、左の端から入ってくる。全画面の設定：右上に、右の端から入ってくる
        //   0.35秒で入る（easeOutCubic）→ 1.6秒そのまま → 0.3秒で消える。色は UIの色のトーストと同じ
        private const float ToastIn = 0.35f, ToastHold = 1.6f, ToastOut = 0.3f;
        private string toastText = "";
        private float toastAge = -1f;
        private void DrawToast()
        {
            if (toastAge < 0f || toastText.Length == 0) return;
            UiPalette p = UiTheme.P;
            const float size = 28f, height = 72f, radius = 22f, margin = 24f;
            float inT = Math.Clamp(toastAge / ToastIn, 0f, 1f);
            float slide = 1f - MathF.Pow(1f - inT, 3f);
            float alpha = Math.Clamp((ToastIn + ToastHold + ToastOut - toastAge) / ToastOut, 0f, 1f);
            if (alpha <= 0f) return;
            float width = FontManager.Measure(toastText, size).X + 96f;
            float y = HeaderHeight + 14f;
            float x = fullScreen
                ? Screen.Width - margin - width + (1f - slide) * (width + margin + 20f)   // 右の端から
                : margin - (1f - slide) * (width + margin + 20f);                          // 左の端から
            Rectangle box = new Rectangle(MathF.Round(x), y, MathF.Round(width), height);
            SmoothShapes.Rectangle(new Rectangle(box.X, box.Y + 8f, box.Width, box.Height), radius, new Color(0, 0, 0, (int)(110 * alpha)));   // 影
            UiTheme.FillGradient(box, radius, p.ToastFrom, p.ToastTo, alpha);
            SmoothShapes.Border(box, radius, 2f, Gui.WithAlpha(p.ToastBorder, alpha));
            SmoothShapes.Rectangle(new Rectangle(box.X + 24f, box.Y + 20f, 6f, height - 40f), 3f, Gui.WithAlpha(p.ToastBar, alpha));      // 左の棒
            Rectangle ink = FontManager.MeasureInk(toastText, size);
            FontManager.Draw(toastText, box.X + 48f, box.Y + (height - ink.Height) / 2f - ink.Y, size, Gui.WithAlpha(p.ToastText, alpha));
        }

        // 結果メッセージを描く(スクロールしても見えるように、パネルの下に固定)
        private void DrawMessage()
        {
            if (messageTimer <= 0f) return;

            // 操作説明の1段上に出す
            Rectangle bar = new Rectangle(SidebarWidth, Screen.Height - HintHeight - 64, CurrentWidth - SidebarWidth, 64);
            UiTheme.FillPanelPart(PanelArea, bar);
            FontManager.Draw(FitText(message, CurrentWidth - LabelX - 20, 26), LabelX, bar.Y + 18, 26, messageColor);
        }

        // 一番下の操作説明(今の状況に合わせて変える)
        private void DrawHint()
        {
            string hint;
            if (resetStep != 0) hint = "Esc：やめる";
            else if (confirmOpen) hint = "Esc：設定に戻る";
            else if (unsavedOpen) hint = "Esc：確認を閉じる";
            else if (restartOpen) hint = "Esc：あとで";
            else if (waitingTarget != null) hint = "キーを押すと登録　Esc：キャンセル";
            else if (Gui.IsEditing) hint = "数字を入力　Enter：決定　Esc：キャンセル";
            else if (Gui.IsSliderFocused) hint = "←→：±1　Shift＋←→：±10　数字クリック：直接入力";
            else hint = fullScreen ? "Esc：戻る　スライダーは←→でも調整できます" : "Esc：閉じる　スライダーは←→でも調整できます";

            Rectangle bar = new Rectangle(0, Screen.Height - HintHeight, CurrentWidth, HintHeight);
            UiTheme.FillBackgroundPart(BackArea, bar);
            Raylib.DrawLine(0, (int)bar.Y, (int)CurrentWidth, (int)bar.Y, Gui.BorderColor);
            FontManager.Draw(hint, fullScreen ? LabelX : 24, bar.Y + 26, 20, Gui.SubTextColor);
            if (fullScreen)
            {
                if (HasPendingChanges)
                    FontManager.Draw("未適用の変更があります", RightEdge - 550, bar.Y + 28, 22, Gui.AccentColor);
                Rectangle apply = new Rectangle(RightEdge - 220, bar.Y + 14, 220, 58);
                if (HasPendingChanges)
                {
                    if (Gui.PrimaryButton(apply, "適用")) ApplyFullSettings();
                }
                else
                {
                    SmoothShapes.Rounded(apply, 0.18f, 8, Gui.BoxColor);
                    Gui.DrawCentered("適用", apply, 26, Gui.SubTextColor);
                }
            }
        }

        // ［戻る］を押したとき、1項目でも未適用なら二択を出す
        private void DrawUnsavedDialog()
        {
            Raylib.DrawRectangle(0, 0, Screen.Width, Screen.Height, new Color(0, 0, 0, 175));
            Rectangle box = new Rectangle(530, 360, 860, 330);
            UiTheme.FillPanel(box, 20);
            SmoothShapes.Border(box, 20, 1.5f, Gui.BorderColor);

            Rectangle close = new Rectangle(box.X + 20, box.Y + 18, 50, 48);
            if (Gui.Button(close, "×")) unsavedOpen = false;
            FontManager.Draw("保存していない設定があります", box.X + 115, box.Y + 48, 38, Gui.TextColor);
            FontManager.Draw("変更を保存してから戻りますか？", box.X + 115, box.Y + 118, 26, Gui.SubTextColor);

            Rectangle discard = new Rectangle(box.X + 42, box.Y + 230, 365, 65);
            Rectangle save = new Rectangle(box.X + 453, box.Y + 230, 365, 65);
            if (Gui.Button(discard, "保存しないで戻る")) DiscardAndClose();
            else if (Gui.PrimaryButton(save, "保存して戻る"))
            {
                unsavedOpen = false;
                ApplyFullSettings();
                if (!restartOpen) RequestClose();   // 再起動の確認を出したときは、それに答えてもらう
            }
        }

        // 閉じるときの確認ダイアログ
        private void DrawConfirmDialog()
        {
            // 全体を暗くする
            Raylib.DrawRectangle(0, 0, Screen.Width, Screen.Height, new Color(0, 0, 0, 160));

            Rectangle box = new Rectangle(560, 340, 800, 400);
            UiTheme.FillPanel(box, 20);
            SmoothShapes.Border(box, 20, 1.5f, Gui.BorderColor);

            string names = string.Join("・", confirmEmptyParts.Select(KeyConfig.GetDrumPartName));
            FontManager.Draw("！ キーが設定されていない場所があります", box.X + 40, box.Y + 40, 34, ErrorColor);
            FontManager.Draw($"{names} にキーがありません。", box.X + 40, box.Y + 120, 30, Gui.TextColor);
            FontManager.Draw("このままだと演奏できません。", box.X + 40, box.Y + 165, 30, Gui.TextColor);
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

        // Windowsの「フォルダの選択」画面を出す(Native AOT でも動くように Win32 を直接呼ぶ。Platform/FolderPicker.cs)
        private void PickSkinsFolder()
        {
            string? picked = Platform.FolderPicker.Pick("スキンフォルダ（スキンが入っているフォルダ）を選んでください");
            if (picked != null)
            {
                ChangeSkinsFolder(picked);
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

        // スキンフォルダを保存し、全画面の適用・F1 の終了時に画像を読み直す
        private void SetSkinsFolder(string folder, string? skinName)
        {
            SkinConfig skin = ActiveSkin;
            string beforeFolder = skin.SkinsFolder;
            string beforeName = skin.SkinName;
            skin.SkinsFolder = folder;
            visibleSkinNames = skin.GetSkinList();
            if (skinName != null && visibleSkinNames.Contains(skinName)) skin.SkinName = skinName;
            else if (!visibleSkinNames.Contains(skin.SkinName)) skin.SkinName = visibleSkinNames.FirstOrDefault() ?? "";
            if (fullScreen)
            {
                int count = visibleSkinNames.Count;
                ShowMessage(count == 0 ? "このフォルダにスキンが見つかりませんでした"
                    : $"スキンフォルダを選びました（{count}個、［適用］で反映）", count == 0 ? ErrorColor : SuccessColor);
                return;
            }
            if (beforeFolder == skin.SkinsFolder && beforeName == skin.SkinName) return;
            skin.Fix();

            if (visibleSkinNames.Count == 0)
                ShowMessage("このフォルダにスキンが見つかりませんでした", ErrorColor);
            else
                ShowMessage($"スキンフォルダを選びました（{visibleSkinNames.Count}個、［スキンを適用］で {skin.SkinName} に切り替わります）", SuccessColor);
        }

        private static bool SameSkinPath(string? a, string? b) =>
            a != null && b != null && string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

        // ［スキンを適用］：選んでいるスキンだけを、すぐ反映して保存する（ほかの設定はそのまま）
        private void ApplySkinNow(SkinConfig skin)
        {
            if (InPlay) { ShowMessage("演奏中はスキンを切り替えられません", ErrorColor); return; }
            ConfigManager.Skin.SkinsFolder = skin.SkinsFolder;
            ConfigManager.Skin.SkinName = skin.SkinName;
            ConfigManager.SaveSkin();
            SkinManager.Reload();
            // もう反映したので、閉じるときや［適用］で読み直さないように「保存ずみ」も合わせる
            if (savedSkin != null) { savedSkin.SkinsFolder = skin.SkinsFolder; savedSkin.SkinName = skin.SkinName; }
            ShowMessage($"{skin.SkinName} に切り替えました", SuccessColor);
        }

        // スキン一覧を読み直す
        private void RefreshSkins()
        {
            visibleSkinNames = ActiveSkin.GetSkinList();
            if (fullScreen)
            {
                ShowMessage($"スキンが {visibleSkinNames.Count}個見つかりました（［適用］で切り替え）", SuccessColor);
                return;
            }
            // 一覧だけ読み直す。選んでいるのが今使っているスキンのときだけ、画像も読み直す(切り替わりはしない)
            //   ほかのスキンを選んでいるときは、［スキンを適用］を押すまで切り替えない
            if (!InPlay && SameSkinPath(ActiveSkin.GetCurrentSkinPath(), SkinManager.CurrentPath)) SkinManager.Reload();
            if (visibleSkinNames.Count == 0)
            {
                ShowMessage("スキンが見つかりませんでした", ErrorColor);
            }
            else
            {
                ShowMessage($"スキン一覧を更新しました（{visibleSkinNames.Count}個、使用中：{SkinManager.CurrentName}）", SuccessColor);
            }
        }

        // スキンフォルダをエクスプローラーで開く
        private void OpenSkinsFolder()
        {
            string folder = ActiveSkin.GetSkinsFolderPath();
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
        private float switchShift; // カテゴリを変えた直後の、中身のずれ(px)
        private float ToScreenY(float contentY)
        {
            return HeaderHeight + contentY - scroll + switchShift;
        }

        // ===== スクロール =====

        private void UpdateScroll(Rectangle content)
        {
            float maxScroll = Math.Max(0f, contentHeight - content.Height);
            lastMaxScroll = maxScroll;

            // スクロールバーをつまむか、レールを押すとその場所へ移動する
            if (maxScroll > 0f && !Gui.IsPopupOpen)
            {
                Rectangle track = new Rectangle(content.X + content.Width - 12, content.Y, 10, content.Height);
                float thumbHeight = Math.Max(64f, track.Height * content.Height / contentHeight);
                var mouse = Screen.GetMousePosition();
                if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mouse, track))
                {
                    float thumbY = track.Y + (track.Height - thumbHeight) * scroll / maxScroll;
                    scrollGrabOffset = mouse.Y >= thumbY && mouse.Y <= thumbY + thumbHeight
                        ? mouse.Y - thumbY : thumbHeight / 2f;
                    scrollDragging = true;
                }

                if (scrollDragging && Raylib.IsMouseButtonDown(MouseButton.Left))
                {
                    float travel = track.Height - thumbHeight;
                    if (travel > 0f)
                        scroll = scrollTarget = Math.Clamp((mouse.Y - track.Y - scrollGrabOffset) / travel, 0f, 1f) * maxScroll;
                }
            }
            if (!Raylib.IsMouseButtonDown(MouseButton.Left)) scrollDragging = false;

            // マウスホイール(プルダウンが開いている間は無効)
            float wheel = Raylib.GetMouseWheelMove();
            if (!scrollDragging && !Gui.IsPopupOpen && wheel != 0 && Gui.IsMouseOver(content))
            {
                scrollTarget -= wheel * 100f;
                clickedCategory = null;
            }

            // 行きすぎないようにする
            scrollTarget = Math.Clamp(scrollTarget, 0f, maxScroll);

            // なめらかに動かす
            if (!scrollDragging) scroll += (scrollTarget - scroll) * Math.Min(1f, Raylib.GetFrameTime() * 15f);
            if (Math.Abs(scrollTarget - scroll) < 0.5f) scroll = scrollTarget;
        }

        // 今見ているカテゴリ(左の文字を目立たせる用)
        private float lastMaxScroll;   // いちばん下までスクロールできる量（UpdateScroll で覚える）

        private Category GetCurrentCategory()
        {
            if (fullScreen) return selectedCategory;
            if (clickedCategory != null) return clickedCategory.Value;

            // 11/26 一番下までスクロールしたら、最後のカテゴリ（スキン）にする。
            //   スキンは短いので、見出しが上まで来る前に一番下に着いてしまい、ずっと「キー」が光っていた
            if (lastMaxScroll > 0f && scroll >= lastMaxScroll - 2f && sectionPos.Count > 0)
                return sectionPos.MaxBy(pair => pair.Value).Key;

            Category current = Category.General;
            float best = -1f;
            foreach (var pair in sectionPos)
            {
                // 11/26：見出しが画面の上の方（上から 150px まで）に来たら、そのカテゴリにする
                //   前は一番上まで来ないと切り替わらず、「音」が見えているのに「画面」が光っていた
                if (pair.Value <= scroll + 150f && pair.Value > best)
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
            UiTheme.FillBackgroundPart(BackArea, new Rectangle(0, 0, CurrentWidth, HeaderHeight));
            Raylib.DrawLine(0, (int)HeaderHeight, (int)CurrentWidth, (int)HeaderHeight, Gui.BorderColor);
            if (fullScreen)
            {
                DrawProductLabel($"TakeTaikoSimulator  {AppVersionText}", 34, 28);
                FontManager.Draw("設定", SectionX, 24, 40, Gui.TextColor);
                if (Gui.Clicked(new Rectangle(SectionX - 8, 18, 100, 56))) CountSecretClick();
                FontManager.Draw("演奏環境", SectionX + 120, 39, 22, Gui.SubTextColor);
                if (Gui.Button(new Rectangle(CurrentWidth - 176, 22, 145, 56), "戻る")) RequestClose();
            }
            else
            {
                FontManager.Draw("設定", 24, 17, 38, Gui.TextColor);
                if (Gui.Clicked(new Rectangle(16, 12, 92, 50))) CountSecretClick();
                DrawProductLabel($"TakeTaikoSimulator  {AppVersionText}", 57, 19);
                if (Gui.Button(new Rectangle(PanelWidth - 68, 18, 48, 48), "×")) RequestClose();
            }
        }

        // アプリ名の横のバージョン（11/27 たけらい「アプリ名のところにバージョン表示あったらいい」。前の「/ F1」は「F1 で開いた」の意味だった）
        //   配る exe は Release-X.cmd が -p:Version=1.0.21.1 で作るので、その番号が出る。最後が .0 なら省く（1.0.21.0 → 1.0.21）
        private static readonly string AppVersionText = MakeVersionText();
        private static string MakeVersionText()
        {
            Version? v = typeof(SettingsOverlay).Assembly.GetName().Version;
            string text = v == null ? "" : v.Revision > 0 ? $"v{v.Major}.{v.Minor}.{v.Build}.{v.Revision}" : $"v{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
#if DEBUG
            text += "（開発版）";
#endif
            return text;
        }

        private void DrawProductLabel(string text, float y, float size)
        {
            // 正式名称が長くなっても、左の帯から文字をはみ出させない。
            float width = FontManager.Measure(text, size).X;
            float fitted = width > 0 ? size * Math.Min(1, (SidebarWidth - 52) / width) : size;
            FontManager.Draw(text, 26, y, fitted, Gui.SubTextColor);
        }

        private void DrawSidebar()
        {
            UiTheme.FillBackgroundPart(BackArea, new Rectangle(0, HeaderHeight, SidebarWidth, Screen.Height - HeaderHeight - HintHeight));
            FontManager.Draw("カテゴリ", 28, HeaderHeight + 20, 19, Gui.SubTextColor);
            Category current = GetCurrentCategory();
            Category[] categories = VisibleCategories;

            // 選んでいるカテゴリの帯：次の場所までスッと動く(約0.1秒)
            Rectangle CategoryArea(int i) => fullScreen
                ? new Rectangle(18, HeaderHeight + 62 + i * 68, SidebarWidth - 36, 58)
                : new Rectangle(12, HeaderHeight + 58 + i * 62, SidebarWidth - 24, 54);
            Rectangle target = CategoryArea(Math.Max(0, Array.IndexOf(categories, current)));
            if (indicatorY < 0f) { indicatorY = indicatorFrom = indicatorTo = target.Y; indicatorTime = 99f; }
            if (target.Y != indicatorTo) { indicatorFrom = indicatorY; indicatorTo = target.Y; indicatorTime = 0f; }
            indicatorTime += Gui.FrameDt;
            float k = Math.Clamp(indicatorTime / IndicatorSeconds, 0f, 1f);
            float e = k < 0.5f ? 4f * k * k * k : 1f - MathF.Pow(-2f * k + 2f, 3f) / 2f;   // easeInOutCubic
            indicatorY = indicatorFrom + (indicatorTo - indicatorFrom) * e;

            for (int i = 0; i < categories.Length; i++)
            {
                Rectangle area = CategoryArea(i);
                bool hover = !Gui.IsPopupOpen && Gui.IsMouseOver(area) && categories[i] != current;
                float h = Gui.Approach($"side:{i}", hover ? 1f : 0f, 18f);
                if (h > 0.01f) SmoothShapes.Rounded(area, 0.12f, 8, Gui.WithAlpha(Gui.BoxColor, h));
            }
            Rectangle band = new Rectangle(target.X, indicatorY, target.Width, target.Height);
            SmoothShapes.Rounded(band, 0.12f, 8, Gui.BoxHoverColor);
            SmoothShapes.Rounded(new Rectangle(band.X + 4, band.Y + 12, 5, band.Height - 24), 1f, 4, Gui.AccentColor);

            for (int i = 0; i < categories.Length; i++)
            {
                Category category = categories[i];
                Rectangle area = CategoryArea(i);

                FontManager.Draw($"{i + 1:00}", area.X + 16, area.Y + (fullScreen ? 17 : 16), 19,
                    category == current ? Gui.AccentColor : Gui.SubTextColor);
                FontManager.Draw(CategoryNames[(int)category], area.X + (fullScreen ? 66 : 53), area.Y + (fullScreen ? 13 : 13),
                    fullScreen ? 27 : 23, category == current ? Gui.TextColor : Gui.SubTextColor);

                if (Gui.Clicked(area) && fullScreen && category != selectedCategory)
                {
                    switchDir = (int)category > (int)selectedCategory ? 1f : -1f;
                    switchTime = 0f;
                    selectedCategory = category;
                    scroll = scrollTarget = contentHeight = 0f;
                    scrollDragging = false;
                    Gui.Reset();
                }
                else if (!fullScreen && Gui.Clicked(area) && sectionPos.TryGetValue(category, out float pos))
                {
                    scrollTarget = pos;
                    clickedCategory = category;
                }
            }

            // 終了操作は残す。装飾アイコンの代わりに文字で明示する
            if (!fullScreen)
            {
                Rectangle quit = new Rectangle(12, Screen.Height - HintHeight - 74, SidebarWidth - 24, 52);
                if (Gui.Button(quit, "終了")) Game.ShouldExit = true;
            }
        }
    }
}


