using System.Diagnostics;
using Raylib_cs;
using TaikoSim.Charts;
using TaikoSim.Audio;
using TaikoSim.Configs;
using TaikoSim.Graphics;
using TaikoSim.Input;
using TaikoSim.Scenes;
using TaikoSim.UI;

namespace TaikoSim.Overlays
{
    // モード選択からは全画面、F1 からは左側のパネルとして開く
    public class SettingsOverlay
    {
        // ===== 大きさ(1920×1080の座標) =====
        public const int PanelWidth = 820; // F1 のパネル幅
        private bool fullScreen;
        public bool IsFullScreen => IsOpen && fullScreen;
        private float HeaderHeight => fullScreen ? 112 : 96;
        private float SidebarWidth => fullScreen ? 300 : 170;
        private float HintHeight => fullScreen ? 100 : 86;
        private float CurrentWidth => fullScreen ? Screen.Width : PanelWidth;

        // 中身の並び
        private float SectionX => fullScreen ? 350 : 190;
        private float LabelX => fullScreen ? 370 : 205;
        private float ControlX => fullScreen ? 1050 : 490;
        private float BoxX => fullScreen ? 1510 : 685;
        private float RightEdge => fullScreen ? 1780 : 790;
        private float RowHeight => fullScreen ? 70 : 58;

        // カテゴリ(左のアイコンの順番)
        private enum Category { General, Window, Sound, Play, Key, Skin }
        private static readonly string[] CategoryNames = { "一般", "画面", "音", "演奏", "キー", "スキン" };
        private Category selectedCategory = Category.General;

        // 全画面では、適用を押すまで本番の設定に触れない
        private GameConfig? draftGame, savedGame;
        private SoundConfig? draftSound, savedSound;
        private KeyConfig? draftKey, savedKey;
        private SkinConfig? draftSkin, savedSkin;
        private bool unsavedOpen;
        private GameConfig ActiveGame => fullScreen ? draftGame! : ConfigManager.Game;
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

        public void Open(bool fullScreen = false)
        {
            this.fullScreen = fullScreen;
            IsOpen = true;
            Gui.Reset();
            selectedCategory = Category.General;
            scroll = scrollTarget = contentHeight = 0f;
            unsavedOpen = false;
            if (fullScreen)
            {
                savedGame = ConfigManager.Copy(ConfigManager.Game);
                savedSound = ConfigManager.Copy(ConfigManager.Sound);
                savedKey = ConfigManager.Copy(ConfigManager.Key);
                savedSkin = ConfigManager.Copy(ConfigManager.Skin);
                draftGame = ConfigManager.Copy(savedGame);
                draftSound = ConfigManager.Copy(savedSound);
                draftKey = ConfigManager.Copy(savedKey);
                draftSkin = ConfigManager.Copy(savedSkin);
            }
            LoadVolumes(); // 音量のバーは、今の設定の値から始める

            // スキンを入れたあとに開いたときのために、スキン一覧を読み直す
            SkinManager.Reload();
        }

        public void Close()
        {
            bool wasFullScreen = fullScreen;
            IsOpen = false;
            Gui.Reset();
            waitingTarget = null;
            messageTimer = 0f;
            confirmOpen = false;
            unsavedOpen = false;
            fullScreen = false;
            draftGame = savedGame = null;
            draftSound = savedSound = null;
            draftKey = savedKey = null;
            draftSkin = savedSkin = null;
            if (!wasFullScreen) ConfigManager.SaveAll(); // F1 は今までどおり閉じたときに保存
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
                || beforeGame.Fullscreen != draftGame.Fullscreen || beforeGame.FullscreenType != draftGame.FullscreenType)
                Screen.RequestApply();
            if (beforeGame.VSync != draftGame.VSync || beforeGame.TargetFps != draftGame.TargetFps)
                Screen.ApplyFrameSettings();
            if (beforeSkin.SkinsFolder != draftSkin.SkinsFolder || beforeSkin.SkinName != draftSkin.SkinName)
                SkinManager.Reload();

            bool outputChanged = beforeSound.OutputType != draftSound.OutputType
                || beforeSound.BassBufferMs != draftSound.BassBufferMs
                || beforeSound.WasapiBufferMs != draftSound.WasapiBufferMs
                || beforeSound.AsioDevice != draftSound.AsioDevice;
            if (outputChanged) AudioManager.Restart();
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
            blinkTimer += dt;
            if (messageTimer > 0f) messageTimer -= dt;

            // 「フォルダを選択」が押されていたら、Windowsの選択画面を出す
            if (pickFolderRequested)
            {
                pickFolderRequested = false;
                PickSkinsFolder();
                return;
            }

            // 未保存の確認は Esc で閉じて、編集中の画面に戻る
            if (unsavedOpen)
            {
                if (InputManager.IsBackPressed()) unsavedOpen = false;
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
            Gui.BeginFrame();

            Rectangle content = new Rectangle(SidebarWidth + 16, HeaderHeight + 18,
                CurrentWidth - SidebarWidth - 40, Screen.Height - HeaderHeight - HintHeight - 36);
            if (!confirmOpen && !unsavedOpen) UpdateScroll(content);

            if (fullScreen) Raylib.DrawRectangle(0, 0, Screen.Width, Screen.Height, Gui.BackgroundColor);
            else Raylib.DrawRectangle(0, 0, Screen.Width, Screen.Height, new Color(0, 0, 0, 170));

            Raylib.DrawRectangle(0, 0, (int)CurrentWidth, Screen.Height, Gui.PanelColor);
            Raylib.DrawRectangleRec(new Rectangle(SidebarWidth + 8, HeaderHeight + 8,
                CurrentWidth - SidebarWidth - 16, Screen.Height - HeaderHeight - HintHeight - 16),
                Gui.BackgroundColor);

            Raylib.BeginScissorMode((int)content.X, (int)content.Y, (int)content.Width, (int)content.Height);
            Gui.SetClip(confirmOpen || unsavedOpen ? NoClickArea : content);

            cursorY = 20;
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
            }
            contentHeight = cursorY + 40;

            Gui.ResetClip();
            Raylib.EndScissorMode();
            DrawScrollbar(content);

            if (confirmOpen || unsavedOpen) Gui.SetClip(NoClickArea);
            DrawHeader();
            DrawSidebar();
            DrawHint();
            Gui.ResetClip();

            DrawMessage();

            if (confirmOpen) DrawConfirmDialog();
            else if (unsavedOpen) DrawUnsavedDialog();
            else Gui.DrawPopups();
        }

        private void DrawScrollbar(Rectangle content)
        {
            if (contentHeight <= content.Height) return;
            float max = contentHeight - content.Height;
            Rectangle track = new Rectangle(content.X + content.Width - 8, content.Y, 7, content.Height);
            Raylib.DrawRectangleRounded(track, 1f, 8, Gui.BorderColor);
            float thumbHeight = Math.Max(64, track.Height * content.Height / contentHeight);
            Rectangle thumb = new Rectangle(track.X, track.Y + (track.Height - thumbHeight) * scroll / max,
                track.Width, thumbHeight);
            Raylib.DrawRectangleRounded(thumb, 1f, 8, Gui.AccentColor);
        }

        // ===== 各カテゴリ =====

        private void DrawGeneralSection()
        {
            Section(Category.General, "一般設定");

            GameConfig game = ActiveGame;
            game.ShowFps = CheckboxRow("FPS表示", game.ShowFps);

            // ----- 曲(前は選曲画面の上にあったボタン) -----
            SubHeader("曲");
            string songText = SongList.IsScanning
                ? (SongList.ScanTotal < 0 ? "曲ファイルを探しています…" : $"読み込み中…　{SongList.ScanDone} / {SongList.ScanTotal}")
                : $"{SongList.SongCount}曲" + (SongList.ErrorCount > 0 ? $"（読めなかった曲 {SongList.ErrorCount}）" : "");
            FontManager.Draw(songText, LabelX, ToScreenY(cursorY) + 6, 24, SongList.IsScanning ? Gui.AccentColor : Color.White);
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
            Section(Category.Window, "ウィンドウ設定");

            GameConfig game = ActiveGame;
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
                if (!fullScreen) Screen.ApplyFrameSettings();
            }
            DescRows(SettingTexts.VSync);

            // 目標フレームレート
            int fps = SliderRow("targetFps", "目標フレームレート", game.TargetFps, 0, 360);
            if (fps != game.TargetFps)
            {
                game.TargetFps = fps;
                game.Fix(); // 1〜29 は 30 にする
                if (!fullScreen) Screen.ApplyFrameSettings();
            }
            DescRows(SettingTexts.TargetFps);

            // ウィンドウの大きさやフルスクリーンは、次のフレームの最初に反映する
            if (windowChanged && !fullScreen)
            {
                Screen.RequestApply();
            }
        }

        private void DrawSoundSection()
        {
            Section(Category.Sound, "サウンド設定");

            SoundConfig sound = ActiveSound;

            // 全画面では値だけ編集中にして、下の［適用］でまとめて反映する
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
            string volumeLabel = volumePending ? "音量を適用（まだ反映されていません）" : "音量を適用";
            if (!fullScreen && Gui.Button(new Rectangle(LabelX, ToScreenY(cursorY), RightEdge - LabelX, 40), volumeLabel) && volumePending)
            {
                ApplyVolumes();
            }
            if (!fullScreen) cursorY += 52;

            DrawAudioOutputRows(sound);
        }

        // ----- 音量(［音量を適用］を押すまで、ここに置いておく) -----
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

        // バーの値を設定に入れて、保存して、今鳴っている曲(選曲画面のプレビュー・リザルトの曲)にかけ直す
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
            ConfigManager.SaveSound();
            SceneManager.Current?.ApplyVolume();
            ShowMessage("音量を適用しました", SuccessColor);
        }

        // ----- 音の出力(出力方式・バッファ) -----

        // プルダウンの中身(AudioOutputType の番号と同じ並び)
        private static readonly string[] OutputTypeNames = { "BASS（標準）", "DirectSound", "WASAPI 共有", "WASAPI 排他", "ASIO" };

        // 方式ごとの説明
        private static string GetOutputDescription(AudioOutputType type) => type switch
        {
            AudioOutputType.Bass => "BASS におまかせで鳴らします（中では WASAPI 共有）。いちばん安定しています。",
            AudioOutputType.DirectSound => "昔の方式です。ほかの方式でうまく鳴らないときに使ってください。遅延は少し増えます。",
            AudioOutputType.WasapiShared => "Windows の普通の出し方です。ほかのアプリの音も一緒に鳴ります。※ bassmix.dll と basswasapi.dll が必要です",
            AudioOutputType.WasapiExclusive => "TaikoSim が音の出口を独り占めします。遅延がかなり減るかわりに、起動中はほかのアプリの音が出なくなります。※ bassmix.dll と basswasapi.dll が必要です",
            AudioOutputType.Asio => "ASIO ドライバで出します。いちばん速いですが、対応した機器（オーディオインターフェースなど）が必要です。※ bassmix.dll と bassasio.dll が必要です",
            _ => "",
        };

        private void DrawAudioOutputRows(SoundConfig sound)
        {
            cursorY += 10;
            SubHeader("音の出力");

            // ----- 出力方式 -----
            int selected = Math.Clamp((int)sound.OutputType, 0, OutputTypeNames.Length - 1);
            sound.OutputType = (AudioOutputType)DropdownRow("outputType", "出力方式", OutputTypeNames, selected);
            DescRows(GetOutputDescription(sound.OutputType));

            // ----- バッファ(方式ごと) -----
            switch (sound.OutputType)
            {
                case AudioOutputType.Bass:
                case AudioOutputType.DirectSound:
                    sound.BassBufferMs = SliderRow("bassBuffer", "バッファ[ms]", sound.BassBufferMs, AudioManager.BufferMin, AudioManager.BufferMax);
                    NoteRow("0 で自動。小さいほど遅れが減りますが、小さすぎると音がブツブツ切れます");
                    break;

                case AudioOutputType.WasapiShared:
                case AudioOutputType.WasapiExclusive:
                    sound.WasapiBufferMs = SliderRow("wasapiBuffer", "バッファ[ms]", sound.WasapiBufferMs, AudioManager.BufferMin, AudioManager.BufferMax);
                    NoteRow("0 で自動。小さいほど遅れが減りますが、小さすぎると音がブツブツ切れます");
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
                    NoteRow("バッファの長さは、ASIO の機器の設定画面で変えてください");
                    break;
            }

            // ----- 今の状態 -----
            DescRows($"今の出力：{AudioManager.Status}");

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
            Section(Category.Play, "プレイ設定");

            GameConfig game = ActiveGame;

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

            KeyConfig key = ActiveKey;

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

            SkinConfig skin = ActiveSkin;

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
            List<string> names = fullScreen ? skin.GetSkinList() : SkinManager.SkinNames;
            if (names.Count == 0)
            {
                Gui.Label("使うスキン", LabelX, ToScreenY(cursorY) + 4, 26);
                FontManager.Draw("スキンがありません", ControlX, ToScreenY(cursorY) + 6, 24, ErrorColor);
                cursorY += RowHeight;
                NoteRow("スキンフォルダにスキン（A-Style など）を入れてください");
                return;
            }

            int index = names.IndexOf(fullScreen ? skin.SkinName : SkinManager.CurrentName);
            if (index < 0) index = 0;
            int newIndex = DropdownRow("skinName", "使うスキン", names.ToArray(), index);
            if (newIndex != index && newIndex >= 0)
            {
                skin.SkinName = names[newIndex];
                if (!fullScreen)
                {
                    ConfigManager.SaveSkin(); // F1 は今までどおり、その場で保存して切り替え
                    SkinManager.Reload();
                }
                ShowMessage(fullScreen ? "スキンは［適用］で切り替わります" : $"スキンを {SkinManager.CurrentName} にしました", SuccessColor);
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
            if (category != Category.General && !fullScreen) cursorY += 30;
            sectionPos[category] = cursorY;
            Gui.Header(title, SectionX, ToScreenY(cursorY));
            Raylib.DrawLine((int)SectionX, (int)(ToScreenY(cursorY) + 53), (int)RightEdge,
                (int)(ToScreenY(cursorY) + 53), Gui.BorderColor);
            cursorY += 64;
        }

        // チェックボックスの行
        private bool CheckboxRow(string label, bool value)
        {
            float y = ToScreenY(cursorY);
            bool result = Gui.Checkbox(new Rectangle(LabelX, y, RightEdge - LabelX, 40), label, value);
            Raylib.DrawLine((int)LabelX, (int)(y + RowHeight - 6), (int)RightEdge, (int)(y + RowHeight - 6), Gui.BorderColor);
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

            Raylib.DrawLine((int)LabelX, (int)(y + RowHeight - 6), (int)RightEdge, (int)(y + RowHeight - 6), Gui.BorderColor);
            cursorY += RowHeight;
            return result;
        }

        // プルダウンの行
        private int DropdownRow(string id, string label, string[] options, int selected)
        {
            float y = ToScreenY(cursorY);
            Gui.Label(label, LabelX, y + 4, 26);

            int result = Gui.Dropdown(id, new Rectangle(ControlX, y + 3, RightEdge - ControlX, 36), options, selected);

            Raylib.DrawLine((int)LabelX, (int)(y + RowHeight - 6), (int)RightEdge, (int)(y + RowHeight - 6), Gui.BorderColor);
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
            Raylib.DrawRectangleRounded(header, 0.16f, 8, hover ? Gui.BoxHoverColor : Gui.BoxColor);

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
            Raylib.DrawRectangleRounded(box, 0.18f, 8, Gui.BoxColor);
            Gui.DrawCentered(keyText, box, 24, Gui.SubTextColor);

            cursorY += RowHeight;
        }

        // キー入力待ちの箱(点滅)
        private void DrawWaitingBox(Rectangle area, string text)
        {
            Raylib.DrawRectangleRounded(area, 0.18f, 8, Gui.BoxColor);
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
            Rectangle bar = new Rectangle(SidebarWidth, Screen.Height - HintHeight - 64, CurrentWidth - SidebarWidth, 64);
            Raylib.DrawRectangleRec(bar, Gui.PanelColor);
            FontManager.Draw(FitText(message, CurrentWidth - LabelX - 20, 26), LabelX, bar.Y + 18, 26, messageColor);
        }

        // 一番下の操作説明(今の状況に合わせて変える)
        private void DrawHint()
        {
            string hint;
            if (confirmOpen) hint = "Esc：設定に戻る";
            else if (unsavedOpen) hint = "Esc：確認を閉じる";
            else if (waitingTarget != null) hint = "キーを押すと登録　Esc：キャンセル";
            else if (Gui.IsEditing) hint = "数字を入力　Enter：決定　Esc：キャンセル";
            else if (Gui.IsSliderFocused) hint = "←→：±1　Shift＋←→：±10　数字クリック：直接入力";
            else hint = fullScreen ? "Esc：戻る　スライダーは←→でも調整できます" : "Esc：閉じる　スライダーは←→でも調整できます";

            Rectangle bar = new Rectangle(0, Screen.Height - HintHeight, CurrentWidth, HintHeight);
            Raylib.DrawRectangleRec(bar, Gui.BackgroundColor);
            Raylib.DrawLine(0, (int)bar.Y, (int)CurrentWidth, (int)bar.Y, Gui.BorderColor);
            FontManager.Draw(hint, fullScreen ? 40 : 24, bar.Y + 30, 20, Gui.SubTextColor);
            if (fullScreen)
            {
                if (HasPendingChanges)
                    FontManager.Draw("未適用の変更があります", 1170, bar.Y + 32, 22, Gui.AccentColor);
                Rectangle apply = new Rectangle(1540, bar.Y + 18, 225, 60);
                if (HasPendingChanges)
                {
                    if (Gui.PrimaryButton(apply, "適用")) ApplyFullSettings();
                }
                else
                {
                    Raylib.DrawRectangleRounded(apply, 0.18f, 8, Gui.BoxColor);
                    Gui.DrawCentered("適用", apply, 26, Gui.SubTextColor);
                }
            }
        }

        // ［戻る］を押したとき、1項目でも未適用なら二択を出す
        private void DrawUnsavedDialog()
        {
            Raylib.DrawRectangle(0, 0, Screen.Width, Screen.Height, new Color(0, 0, 0, 175));
            Rectangle box = new Rectangle(530, 360, 860, 330);
            Raylib.DrawRectangleRounded(box, 0.025f, 8, Gui.PanelColor);
            Raylib.DrawRectangleLinesEx(box, 1, Gui.BorderColor);

            Rectangle close = new Rectangle(box.X + 20, box.Y + 18, 50, 48);
            if (Gui.Button(close, "×")) unsavedOpen = false;
            FontManager.Draw("保存していない設定があります", box.X + 115, box.Y + 48, 38, Color.White);
            FontManager.Draw("変更を保存してから戻りますか？", box.X + 115, box.Y + 118, 26, Gui.SubTextColor);

            Rectangle discard = new Rectangle(box.X + 42, box.Y + 230, 365, 65);
            Rectangle save = new Rectangle(box.X + 453, box.Y + 230, 365, 65);
            if (Gui.Button(discard, "保存しないで戻る")) DiscardAndClose();
            else if (Gui.PrimaryButton(save, "保存して戻る"))
            {
                unsavedOpen = false;
                ApplyFullSettings();
                RequestClose();
            }
        }

        // 閉じるときの確認ダイアログ
        private void DrawConfirmDialog()
        {
            // 全体を暗くする
            Raylib.DrawRectangle(0, 0, Screen.Width, Screen.Height, new Color(0, 0, 0, 160));

            Rectangle box = new Rectangle(560, 340, 800, 400);
            Raylib.DrawRectangleRounded(box, 0.025f, 8, Gui.PanelColor);
            Raylib.DrawRectangleLinesEx(box, 1, Gui.BorderColor);

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
            string current = ActiveSkin.GetSkinsFolderPath();

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
            SkinConfig skin = ActiveSkin;
            skin.SkinsFolder = folder;
            if (skinName != null) skin.SkinName = skinName;
            if (fullScreen)
            {
                int count = skin.GetSkinList().Count;
                ShowMessage(count == 0 ? "このフォルダにスキンが見つかりませんでした"
                    : $"スキンフォルダを選びました（{count}個、［適用］で反映）", count == 0 ? ErrorColor : SuccessColor);
                return;
            }
            skin.Fix();
            ConfigManager.SaveSkin(); // F1 はすぐ保存
            SkinManager.Reload();

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
            if (fullScreen)
            {
                ShowMessage($"スキンが {ActiveSkin.GetSkinList().Count}個見つかりました（［適用］で切り替え）", SuccessColor);
                return;
            }
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
            if (fullScreen) return selectedCategory;
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
            Raylib.DrawRectangle(0, 0, (int)CurrentWidth, (int)HeaderHeight, Gui.BackgroundColor);
            Raylib.DrawLine(0, (int)HeaderHeight, (int)CurrentWidth, (int)HeaderHeight, Gui.BorderColor);
            if (fullScreen)
            {
                if (Gui.PrimaryButton(new Rectangle(32, 22, 188, 62), "← 戻る")) RequestClose();
                FontManager.Draw("設定", 720, 29, 45, Color.White);
                FontManager.Draw("モード選択 / 設定", 900, 43, 28, Gui.SubTextColor);
            }
            else
            {
                FontManager.Draw("設定", 30, 24, 42, Color.White);
                FontManager.Draw("F1", 150, 39, 24, Gui.SubTextColor);
                if (Gui.Button(new Rectangle(745, 20, 55, 55), "×")) RequestClose();
            }
        }

        private void DrawSidebar()
        {
            Color background = Gui.PanelColor;
            Raylib.DrawRectangle(0, (int)HeaderHeight, (int)SidebarWidth, Screen.Height - (int)HeaderHeight - (int)HintHeight, background);
            Category current = GetCurrentCategory();
            Category[] categories = Enum.GetValues<Category>();

            for (int i = 0; i < categories.Length; i++)
            {
                Category category = categories[i];
                Rectangle area = fullScreen
                    ? new Rectangle(25, 150 + i * 88, SidebarWidth - 50, 74)
                    : new Rectangle(12, 132 + i * 93, SidebarWidth - 24, 74);
                bool hover = !Gui.IsPopupOpen && Gui.IsMouseOver(area);

                Color back = background;
                if (category == current) back = new Color(24, 48, 83, 255);
                else if (hover) back = Gui.BoxHoverColor;
                Raylib.DrawRectangleRounded(area, 0.15f, 8, back);
                if (category == current) Raylib.DrawRectangle((int)area.X, (int)area.Y, 6, (int)area.Height, Gui.AccentColor);

                float iconX = fullScreen ? area.X + 44 : area.X + 35;
                DrawCategoryIcon(category, iconX, area.Y + area.Height / 2f, back);
                FontManager.Draw(CategoryNames[i], iconX + (fullScreen ? 47 : 31), area.Y + 21, fullScreen ? 28 : 24,
                    category == current ? new Color(126, 177, 255, 255) : Color.White);

                if (Gui.Clicked(area) && fullScreen)
                {
                    selectedCategory = category;
                    scroll = scrollTarget = contentHeight = 0f;
                    Gui.Reset();
                }
                else if (Gui.Clicked(area) && sectionPos.TryGetValue(category, out float pos))
                {
                    scrollTarget = pos;
                    clickedCategory = category;
                }
            }

            // F1 にあった終了ボタンは残す
            if (!fullScreen)
            {
                Rectangle power = new Rectangle(12, Screen.Height - HintHeight - 90, SidebarWidth - 24, 70);
                if (Gui.IsMouseOver(power)) Raylib.DrawRectangleRounded(power, 0.15f, 8, new Color(70, 25, 25, 255));
                Icons.Power(SidebarWidth / 2f, power.Y + power.Height / 2f, new Color(230, 70, 60, 255));
                if (Gui.Clicked(power)) Game.ShouldExit = true;
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
