using System.Numerics;
using Raylib_cs;
using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Graphics;
using TakeTaikoSimulator.UI;

namespace TakeTaikoSimulator.Scenes
{
    // 初回起動セットアップ(ようこそ → 画面 → 音量 → 叩くキー → 完了)
    // 一部の場所だけキーが無いときは、その場所だけ聞く
    public class SetupScene : Scene
    {
        // ページ
        private enum Step { Welcome, Display, Sound, Keys, Done }

        // ページの数(ページ番号の表示に使う)
        private const int StepCount = 5;

        // プルダウンの選択肢
        private static readonly string[] ResolutionNames =
            GameConfig.Resolutions.Select(r => $"{r.Width}×{r.Height}").ToArray();

        // 叩く場所(左縁 → 左面 → 右面 → 右縁 の順)
        private static readonly DrumPart[] Parts = Enum.GetValues<DrumPart>();

        // 枠と並び(1920×1080の座標)
        private static readonly Rectangle Panel = new Rectangle(100, 160, 1720, 760);
        private const float Left = 160;
        private const float ControlX = 760;
        private const float BoxX = 1550;
        private const float Right = 1760;
        private const float FooterY = 850;

        // 説明文の大きさ
        private const float DescSize = 22;
        private const float DescLine = 28;   // 説明文の1行の高さ
        private const float DescIndent = 48; // 説明文の字下げ(チェックボックスの文字とそろえる)

        // メッセージの色
        private static readonly Color SuccessColor = new Color(110, 220, 130, 255);
        private static readonly Color ErrorColor = new Color(255, 110, 100, 255);
        private static readonly Color NeutralColor = new Color(205, 190, 196, 255);
        private static readonly Color PendingColor = new Color(125, 103, 112, 255);

        private Step step = Step.Welcome;

        // ===== 新しいプレイヤーデータの「はじめの設定」（11/13 たけらい） =====
        //   名前を入れたあと、起動時と同じこの画面で 音量 → 叩くキー → 完了 を通る（画面のページは PC のものなので出さない）。
        //   終わったら next の画面に戻る（エントリーなら「どちらの太鼓で」、途中参加ならモード選択・選曲）
        //   前は、自前の小さな板（はやさ・音色・音量）だった
        private readonly int playerSlot = -1;
        private readonly Func<Scene>? next;
        private bool PlayerMode => playerSlot >= 0;
        private SoundConfig? soundBefore;   // 途中参加のとき：1人目の音量を、終わったら元に戻す

        public SetupScene() { }

        /// <param name="slot">はじめの設定をするデータ（名前はもう入っている）</param>
        /// <param name="next">終わったあとに出す画面</param>
        public SetupScene(int slot, Func<Scene> next)
        {
            playerSlot = slot;
            this.next = next;
        }

        // 聞く必要がある叩く場所(キーが0個の場所だけ)
        private DrumPart[] partsToAsk = Parts;

        // 初回起動か(1Pの4つとも空なら初回扱い。2Pは初期値を表示して変更できる)
        private bool isFirstRun = true;

        // 叩くキーで、今何番目を聞いているか(partsToAsk.Length なら全部決まった)
        private int keyIndex = 0;
        private DrumPart? changingPart;

        // 点滅用
        private float blinkTimer = 0f;

        // 結果メッセージ
        private string message = "";
        private Color messageColor = Color.White;
        private float messageTimer = 0f;

        // セットアップ中は設定パネルを開かない
        public override bool CanOpenSettings => false;

        // 叩くキーの入力待ち中か(Game がスクショキーを無視するのに使う)
        public override bool IsWaitingKey => step == Step.Keys && (changingPart != null || keyIndex < partsToAsk.Length);

        // ===== 入る・更新 =====

        public override void Enter()
        {
            // キーが0個の場所だけ聞く(他の場所のキーは消さない)
            partsToAsk = ConfigManager.Key.GetEmptyParts().ToArray();
            isFirstRun = partsToAsk.Length == 4;
            keyIndex = 0;

            // 初回はようこそ画面から。一部だけ空なら、いきなり叩くキーの画面
            step = isFirstRun ? Step.Welcome : Step.Keys;
            if (PlayerMode)
            {
                // 新しいデータ：ようこそ → 音量 → キー → 完了。キーは今のものを見せて、変えたい所だけ変える
                isFirstRun = false;
                step = Step.Welcome;
                // 途中参加（もうだれかが遊んでいる）なら、ここで動かす音量はその人のものなので、あとで戻す
                if (LocalSession.Count > 0) soundBefore = ConfigManager.Copy(ConfigManager.Sound);
            }
        }

        public override void Update(float dt)
        {
            blinkTimer += dt;
            if (messageTimer > 0f) messageTimer -= dt;

            // 叩くキーの入力待ち
            if (IsWaitingKey)
            {
                int pressed = Raylib.GetKeyPressed();
                while (pressed != 0)
                {
                    KeyboardKey key = (KeyboardKey)pressed;

                    // Escは「戻る」なので登録しない(戻る処理は OnBack でやる)
                    if (key != KeyConfig.BackKey)
                    {
                        DrumPart part = changingPart ?? partsToAsk[keyIndex];
                        bool changed = changingPart != null;
                        string result;
                        bool accepted = changed ? ConfigManager.Key.TryReplaceDrumKey(part, key, out result)
                            : ConfigManager.Key.TryAddDrumKey(part, key, out result);
                        if (accepted)
                        {
                            ShowMessage(result, SuccessColor);
                            if (changed) changingPart = null;
                            while (keyIndex < partsToAsk.Length && ConfigManager.Key.DrumKeys.GetValueOrDefault(partsToAsk[keyIndex])?.Count > 0) keyIndex++;
                        }
                        else
                        {
                            ShowMessage(result, ErrorColor); // 「そのキーは〇〇で割り当てられています」
                        }
                        break; // 1フレームに1つだけ受け付ける
                    }
                    pressed = Raylib.GetKeyPressed();
                }
                return;
            }

            // Enterで次へ(数字の入力中は、Enterは「数字の決定」に使うので次へ進まない)
            if (!Gui.IsPopupOpen && !Gui.IsEditing && (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter)))
            {
                GoNext();
            }
        }

        // Escが押されたとき(Game から呼ばれる)
        public override void OnBack()
        {
            // 数字の入力中なら、入力をやめるだけ
            if (Gui.IsEditing)
            {
                Gui.CancelEdit();
                return;
            }
            if (Gui.IsPopupOpen)
            {
                Gui.ClosePopup();
                return;
            }
            GoBack();
        }

        // ===== ページ移動 =====

        private void GoNext()
        {
            if (changingPart != null) return;
            switch (step)
            {
                case Step.Welcome:
                    step = PlayerMode ? Step.Sound : Step.Display;
                    break;

                case Step.Display:
                    step = Step.Sound;
                    break;

                case Step.Sound:
                    step = Step.Keys;
                    break;

                case Step.Keys:
                    if (keyIndex >= partsToAsk.Length) step = Step.Done; // 全部決まったときだけ進める
                    break;

                case Step.Done:
                    Finish();
                    break;
            }
            Gui.Reset();
        }

        private void GoBack()
        {
            if (changingPart != null) { changingPart = null; Gui.Reset(); return; }
            switch (step)
            {
                case Step.Welcome:
                    break; // 最初のページは戻れない

                case Step.Display:
                    step = Step.Welcome;
                    break;

                case Step.Sound:
                    step = PlayerMode ? Step.Welcome : Step.Display;
                    break;

                case Step.Keys:
                    if (keyIndex == 0)
                    {
                        // 初回なら音量の設定に戻る。一部だけ空のときは戻る先が無い
                        if (isFirstRun || PlayerMode) step = Step.Sound;
                    }
                    else
                    {
                        // 1つ前の場所をやり直し
                        keyIndex--;
                        DrumPart part = partsToAsk[keyIndex];
                        ConfigManager.Key.ClearDrumKeys(part);
                        ShowMessage($"{KeyConfig.GetDrumPartName(part)}をやり直します", NeutralColor);
                    }
                    break;

                case Step.Done:
                    step = Step.Keys; // 叩くキーは全部入ったまま戻る
                    break;
            }
            Gui.Reset();
        }

        // 戻れるか(戻るボタンを出すか)
        private bool CanGoBack()
        {
            if (step == Step.Welcome) return false;
            if (step == Step.Keys && keyIndex == 0 && !isFirstRun && !PlayerMode) return false;
            return true;
        }

        private void Finish()
        {
            if (PlayerMode)
            {
                // 新しい人の設定：演奏の設定は初期値、音量はこの画面で決めたもの。ファイルに書いてから元の画面へ
                PlayerSettings.Save(playerSlot, PlayerSettings.ForNewData());
                if (soundBefore != null)
                {
                    PlayerSettings.ApplySound(new PlayerSettingsFile
                    {
                        MasterVolume = soundBefore.MasterVolume, SeVolume = soundBefore.SeVolume, MusicVolume = soundBefore.MusicVolume,
                        BgmVolume = soundBefore.BgmVolume, VoiceVolume = soundBefore.VoiceVolume,
                    }, ConfigManager.Sound);
                }
                ConfigManager.SaveAll();   // 叩くキーは全員共通なので、ここで保存
                SceneManager.Simple(next!());
                return;
            }
            ConfigManager.SaveAll();
            SceneManager.Simple(new EntryScene());
        }

        // ===== 描画 =====

        public override void Draw()
        {
            Gui.BeginFrame();

            // 背景
            Raylib.DrawRectangle(0, 0, Screen.Width, Screen.Height, Gui.BackgroundColor);
            SmoothShapes.Rounded(Panel, 0.009f, 8, Gui.PanelColor);

            // 文字と細い進行線だけで、どこまで設定したか分かるようにする
            FontManager.Draw(PlayerMode ? "はじめの設定" : isFirstRun ? "初期設定" : "キーの設定", Left, 32, 38, Color.White);
            if (isFirstRun || PlayerMode)
            {
                DrawProgress();
            }

            switch (step)
            {
                case Step.Welcome: DrawWelcome(); break;
                case Step.Display: DrawDisplay(); break;
                case Step.Sound: DrawSound(); break;
                case Step.Keys: DrawKeys(); break;
                case Step.Done: DrawDone(); break;
            }

            DrawMessage();
            DrawFooter();

            // プルダウンの一覧は一番上に
            Gui.DrawPopups();
        }

        // ===== どこまで進んだか（Motion Prompt #068 ステップインジケーター。11/10） =====
        // Motion Prompt #068 · MIT License (c) 2026 Shohei Ohtaki · https://showheyohtaki.github.io/motion-prompt/#068
        //   5つの丸を灰色の線でつなぐ。終わったページは青い丸に白いチェック、今のページは青い丸に白い番号、まだのページは輪だけ。
        //   次へ進むと、青い線が次の丸まで 0.3秒（easeInOutCubic）で伸びる（戻るときは縮む）。最後のページは全部チェック。
        //   前は「01 ようこそ」の文字と細い線、右上に「1 / 5」だった
        private const float StepLineSeconds = 0.3f, StepRadius = 22f, StepY = 100f;
        private float progressFrom, progressTo, progressTime = 99f;

        private void DrawProgress()
        {
            string[] names = PlayerMode ? new[] { "ようこそ", "音量", "キー", "完了" } : new[] { "ようこそ", "画面", "音量", "キー", "完了" };
            float first = Left + 60f, last = Right - 60f, gap = (last - first) / (names.Length - 1);
            int current = PlayerMode && step > Step.Welcome ? (int)step - 1 : (int)step;   // 画面のページが無いぶん詰める

            // 青い線の伸び（ページが変わったら、今の長さから次の長さへ）
            float target = current;
            float shown = progressFrom + (progressTo - progressFrom) * EaseInOutCubic(Math.Clamp(progressTime / StepLineSeconds, 0f, 1f));
            if (target != progressTo) { progressFrom = shown; progressTo = target; progressTime = 0f; }
            progressTime += Gui.FrameDt;
            shown = progressFrom + (progressTo - progressFrom) * EaseInOutCubic(Math.Clamp(progressTime / StepLineSeconds, 0f, 1f));

            SmoothShapes.Rounded(new Rectangle(first, StepY - 2f, last - first, 4f), 1f, 4, Gui.BorderColor);
            if (shown > 0f) SmoothShapes.Rounded(new Rectangle(first, StepY - 2f, gap * shown, 4f), 1f, 4, Gui.AccentColor);

            bool allDone = step == Step.Done;
            for (int i = 0; i < names.Length; i++)
            {
                float x = first + gap * i;
                bool done = i < current || allDone, now = i == current && !allDone;
                Vector2 c = new(x, StepY);
                if (done || now) SmoothShapes.Circle(c, StepRadius, Gui.AccentColor);
                else
                {
                    SmoothShapes.Circle(c, StepRadius, Gui.PanelColor);
                    SmoothShapes.Ring(c, StepRadius - 2.5f, StepRadius, Gui.BorderColor);
                }
                if (done)
                {
                    // 白いチェック
                    SmoothShapes.Line(new Vector2(x - 9f, StepY + 1f), new Vector2(x - 3f, StepY + 7f), 3.6f, Color.White);
                    SmoothShapes.Line(new Vector2(x - 3f, StepY + 7f), new Vector2(x + 9f, StepY - 6f), 3.6f, Color.White);
                }
                else
                {
                    Gui.DrawCentered((i + 1).ToString(), new Rectangle(x - StepRadius, StepY - StepRadius, StepRadius * 2f, StepRadius * 2f),
                        22f, now ? Color.White : Gui.SubTextColor);
                }
                float w = FontManager.Measure(names[i], 21f).X;
                FontManager.Draw(names[i], x - w / 2f, StepY + 32f, 21f, done || now ? Gui.AccentColor : Gui.SubTextColor);
            }
        }

        private static float EaseInOutCubic(float k) => k < 0.5f ? 4f * k * k * k : 1f - MathF.Pow(-2f * k + 2f, 3f) / 2f;

        // 1ページ目：ようこそ
        private void DrawWelcome()
        {
            if (PlayerMode)
            {
                FontManager.Draw($"{PlayerDataStore.DisplayName(playerSlot)} さん、ようこそ！", Left, 300, 64, Color.White);
                FontManager.Draw("音量と叩くキーを確かめます。演奏の設定（はやさ・音色など）は初期値から始まります。", Left, 430, 32, Color.White);
                FontManager.Draw("あとからいつでも設定（F1）と演奏オプションで変更できます。", Left, 480, 28, Gui.SubTextColor);
                return;
            }
            FontManager.Draw("TaikoSimへようこそ！", Left, 300, 64, Color.White);
            FontManager.Draw("最初に、画面・音量・叩くキーを設定します。", Left, 430, 32, Color.White);
            FontManager.Draw("あとからいつでも設定（F1）で変更できます。", Left, 480, 28, Gui.SubTextColor);
        }

        // 2ページ目：画面
        private void DrawDisplay()
        {
            Title("画面");

            GameConfig game = ConfigManager.Game;
            bool windowChanged = false;
            float descX = Left + DescIndent;
            float descWidth = Right - descX;
            float y = 326;

            // ----- 画面解像度 -----
            FontManager.Draw("画面解像度", Left, y + 6, 28, Color.White);
            int resIndex = Array.FindIndex(GameConfig.Resolutions, r => r.Width == game.WindowWidth && r.Height == game.WindowHeight);
            int newResIndex = Gui.Dropdown("setupResolution", new Rectangle(ControlX, y, Right - ControlX, 44), ResolutionNames, resIndex);
            if (newResIndex != resIndex && newResIndex >= 0)
            {
                (game.WindowWidth, game.WindowHeight) = GameConfig.Resolutions[newResIndex];
                windowChanged = true;
            }
            y += 58;

            // ----- フルスクリーン -----
            bool fullscreen = Gui.Checkbox(new Rectangle(Left, y, Right - Left, 40), "フルスクリーン", game.Fullscreen);
            if (fullscreen != game.Fullscreen)
            {
                game.Fullscreen = fullscreen;
                windowChanged = true;
            }
            y += 46;

            // オフのとき(ウィンドウ)の説明
            if (!game.Fullscreen)
            {
                y += SettingTexts.DrawWrapped(SettingTexts.Window, descX, y, descWidth, DescSize, Gui.SubTextColor, DescLine) + 6;
            }

            // フルスクリーンの説明（方式はえらべない。OpenTaiko と同じ本物のフルスクリーンだけ）
            if (game.Fullscreen)
            {
                y += SettingTexts.DrawWrapped(SettingTexts.Fullscreen, descX, y, descWidth, DescSize, Gui.SubTextColor, DescLine);
                y += SettingTexts.DrawWrapped(SettingTexts.FullscreenNote, descX, y, descWidth, DescSize, Gui.SubTextColor, DescLine) + 10;
            }

            // ----- 垂直同期 -----
            bool vsync = Gui.Checkbox(new Rectangle(Left, y, Right - Left, 40), "垂直同期", game.VSync);
            if (vsync != game.VSync)
            {
                game.VSync = vsync;
                Screen.ApplyFrameSettings();
            }
            y += 46;
            y += SettingTexts.DrawWrapped(SettingTexts.VSync, descX, y, descWidth, DescSize, Gui.SubTextColor, DescLine) + 10;

            // ----- 目標フレームレート -----
            FontManager.Draw("目標フレームレート", Left, y + 6, 28, Color.White);
            int fps = SliderRow("setupTargetFps", y, game.TargetFps, 0, 360);
            if (fps != game.TargetFps)
            {
                game.TargetFps = fps;
                game.Fix(); // 1〜29 は 30 にする
                Screen.ApplyFrameSettings();
            }
            y += 50;
            SettingTexts.DrawWrapped(SettingTexts.TargetFps, descX, y, descWidth, DescSize, Gui.SubTextColor, DescLine);

            // ウィンドウの大きさやフルスクリーンは、次のフレームの最初に反映する
            if (windowChanged)
            {
                Screen.RequestApply();
            }
        }

        // 3ページ目：音量
        private void DrawSound()
        {
            Title("音量");
            FontManager.Draw("それぞれの音の大きさを決めてください（0〜100）。", Left, 330, 28, Color.White);

            SoundConfig sound = ConfigManager.Sound;
            float y = 400;

            sound.MasterVolume = VolumeRow("setupMaster", "Master（全体）", ref y, sound.MasterVolume);
            sound.SeVolume = VolumeRow("setupSe", "SE（効果音）", ref y, sound.SeVolume);
            sound.MusicVolume = VolumeRow("setupMusic", "Music（曲）", ref y, sound.MusicVolume);
            sound.BgmVolume = VolumeRow("setupBgm", "BGM（メニューの曲）", ref y, sound.BgmVolume);
            sound.VoiceVolume = VolumeRow("setupVoice", "Voice（声）", ref y, sound.VoiceVolume);

            FontManager.Draw("※ Master は全体の音量です。ほかの音量は Master と掛け合わせて決まります", Left, y + 6, 22, Gui.SubTextColor);
        }

        // 音量の1行(名前＋スライダー)
        private int VolumeRow(string id, string label, ref float y, int value)
        {
            FontManager.Draw(label, Left, y + 6, 28, Color.White);
            int result = SliderRow(id, y, value, 0, 100);
            y += 64;
            return result;
        }

        // スライダー(ControlX から右端まで。右に数字の箱)
        private static int SliderRow(string id, float y, int value, int min, int max)
        {
            Rectangle bar = new Rectangle(ControlX, y + 7, BoxX - ControlX - 12, 30);
            Rectangle box = new Rectangle(BoxX, y + 4, Right - BoxX, 36);
            return Gui.Slider(id, bar, box, value, min, max);
        }

        // 4ページ目：叩くキー
        private void DrawKeys()
        {
            Title("叩くキー");
            FontManager.Draw("1Pと2Pのキーを確認できます。変更ボタンを押してから、新しいキーを押してください。", Left, 330, 26, Color.White);
            for (int player = 0; player < 2; player++)
            {
                float x = Left + player * 820;
                FontManager.Draw($"{player + 1}P", x, 380, 32, player == 0 ? Gui.AccentColor : Color.SkyBlue);
                for (int i = 0; i < 4; i++)
                {
                    DrumPart part = Parts[player * 4 + i];
                    float y = 430 + i * 76;
                    Rectangle row = new(x, y, 780, 64);
                    int askIndex = Array.IndexOf(partsToAsk, part);
                    bool current = changingPart == part || changingPart == null && askIndex >= 0 && askIndex == keyIndex;
                    var keys = ConfigManager.Key.DrumKeys.GetValueOrDefault(part, new List<KeyboardKey>());
                    SmoothShapes.Rounded(row, .1f, 8, Gui.BoxColor);
                    FontManager.Draw(KeyConfig.GetDrumPartName(part), x + 18, y + 18, 25, Color.White);
                    string value = current ? "キーを押してください" : keys.Count == 0 ? "未登録" : string.Join(", ", keys);
                    FontManager.Draw(value, x + 170, y + 18, current ? 23 : 25, current ? Gui.AccentColor : Gui.SubTextColor);
                    if (Gui.Button(new(x + 650, y + 10, 112, 44), "変更")) changingPart = part;
                }
            }
            FontManager.Draw("2P初期値：カッ C / M　ドン V / N　※ 同じキーを両側に登録することはできません", Left, 760, 24, Gui.SubTextColor);
            if (messageTimer <= 0) FontManager.Draw(keyIndex >= partsToAsk.Length && changingPart == null ? "設定を確認したら「次へ」を押してください" : "Esc：変更をキャンセル／1つ前をやり直す", Left, 802, 24, Gui.SubTextColor);
        }

        // 5ページ目：完了
        private void DrawDone()
        {
            Title("設定完了！");

            GameConfig game = ConfigManager.Game;
            SoundConfig sound = ConfigManager.Sound;
            float y = 340;

            // 画面
            string screenText = $"画面：{game.WindowWidth}×{game.WindowHeight}";
            if (game.Fullscreen) screenText = "画面：フルスクリーン（モニターの解像度）";
            FontManager.Draw(screenText, Left, y, 30, Color.White);
            y += 48;

            // フレームレート
            string fpsText = game.TargetFps == 0 ? "無制限" : $"{game.TargetFps}";
            if (game.VSync) fpsText += "（垂直同期オン）";
            FontManager.Draw($"目標フレームレート：{fpsText}", Left, y, 30, Color.White);
            y += 48;

            // 音量
            FontManager.Draw($"音量：Master {sound.MasterVolume}　SE {sound.SeVolume}　Music {sound.MusicVolume}　BGM {sound.BgmVolume}　Voice {sound.VoiceVolume}",
                Left, y, 30, Color.White);
            y += 60;

            // 8か所を左右2列にして、完了画面の下にはみ出さないようにする。
            for (int player = 0; player < 2; player++)
            {
                for (int i = 0; i < 4; i++)
                {
                    DrumPart part = Parts[player * 4 + i];
                    string keys = string.Join(", ", ConfigManager.Key.DrumKeys.GetValueOrDefault(part, new List<KeyboardKey>()));
                    FontManager.Draw($"{KeyConfig.GetDrumPartName(part)}：{keys}", Left + player * 820, y + i * 44, 28, Color.White);
                }
            }
            y += 196;
            KeyboardKey settingsKey = ConfigManager.Key.SystemKeys.GetValueOrDefault(SystemAction.Settings, KeyboardKey.F1);
            FontManager.Draw($"設定はいつでも {settingsKey} で変更できます。", Left, y, 28, Gui.AccentColor);
            FontManager.Draw(PlayerMode ? "「はじめる」を押すと元の画面に戻ります。" : "「はじめる」を押すとエントリー画面に進みます。", Left, y + 46, 26, Gui.SubTextColor);
        }

        // ページの大きいタイトル
        private static void Title(string text)
        {
            FontManager.Draw(text, Left, 250, 52, Color.White);
        }

        // 下の説明とボタン
        private void DrawFooter()
        {
            // 操作の説明(Escで戻れることなどに気づけるように、今の状況に合わせて変える)
            string hint;
            if (step == Step.Welcome)
            {
                hint = "Enter：次へ";
            }
            else if (IsWaitingKey)
            {
                if (changingPart != null) hint = "Esc：変更をキャンセル";
                else if (keyIndex > 0) hint = $"Esc：{KeyConfig.GetDrumPartName(partsToAsk[keyIndex - 1])}をやり直す";
                else if (isFirstRun || PlayerMode) hint = "Esc：前のページに戻る";
                else hint = ""; // 一部だけ空のときの最初の場所は戻れない
            }
            else if (Gui.IsEditing)
            {
                hint = "数字を入力　Enter：決定　Esc：キャンセル";
            }
            else if (Gui.IsSliderFocused)
            {
                hint = "←→：±1　Shift＋←→：±10　数字クリック：直接入力";
            }
            else
            {
                hint = "Esc：戻る　Enter：次へ";
            }
            if (hint.Length > 0)
            {
                FontManager.Draw(hint, Left, FooterY + 16, 22, Gui.SubTextColor);
            }

            // 戻るボタン
            if (CanGoBack())
            {
                if (Gui.Button(new Rectangle(Right - 420, FooterY, 190, 56), "戻る"))
                {
                    GoBack();
                }
            }

            // 次へボタン(叩くキーが全部決まるまでは押せない)
            bool canNext = step != Step.Keys || changingPart == null && keyIndex >= partsToAsk.Length;
            string nextText = step == Step.Done ? "はじめる" : "次へ";
            Rectangle nextRect = new Rectangle(Right - 210, FooterY, 210, 56);

            if (canNext)
            {
                if (Gui.PrimaryButton(nextRect, nextText))
                {
                    GoNext();
                }
            }
            else
            {
                // 押せない状態(灰色)
                SmoothShapes.Rectangle(nextRect, 10, Gui.TrackColor);
                Gui.DrawCentered(nextText, nextRect, 26, PendingColor);
            }
        }

        // 結果メッセージ
        private void ShowMessage(string text, Color color)
        {
            message = text;
            messageColor = color;
            messageTimer = 3f;
        }

        private void DrawMessage()
        {
            if (messageTimer <= 0f) return;
            FontManager.Draw(message, Left, step == Step.Keys ? 802 : 786, step == Step.Keys ? 24 : 28, messageColor);
        }
    }
}


