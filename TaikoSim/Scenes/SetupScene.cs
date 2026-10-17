using Raylib_cs;
using TaikoSim.Configs;
using TaikoSim.Graphics;
using TaikoSim.UI;

namespace TaikoSim.Scenes
{
    // 初回起動セットアップ(ようこそ → 画面 → 叩くキー → 完了)
    // 一部の場所だけキーが無いときは、その場所だけ聞く
    public class SetupScene : Scene
    {
        // ページ
        private enum Step { Welcome, Display, Keys, Done }

        // プルダウンの選択肢
        private static readonly string[] ResolutionNames =
            GameConfig.Resolutions.Select(r => $"{r.Width}×{r.Height}").ToArray();
        private static readonly string[] FullscreenTypeNames = { "ボーダーレス（枠なし）", "排他フルスクリーン" };

        // 叩く場所(左縁 → 左面 → 右面 → 右縁 の順)
        private static readonly DrumPart[] Parts = Enum.GetValues<DrumPart>();

        // 枠と並び(1920×1080の座標)
        private static readonly Rectangle Panel = new Rectangle(460, 160, 1000, 760);
        private const float Left = 540;      // 中身の左端
        private const float ControlX = 900;  // プルダウンなどの左端
        private const float Right = 1380;    // 中身の右端
        private const float FooterY = 840;   // 下のボタンの位置

        // メッセージの色
        private static readonly Color SuccessColor = new Color(110, 220, 130, 255);
        private static readonly Color ErrorColor = new Color(255, 110, 100, 255);
        private static readonly Color NeutralColor = new Color(200, 205, 215, 255);
        private static readonly Color PendingColor = new Color(110, 115, 130, 255);

        private Step step = Step.Welcome;

        // 聞く必要がある叩く場所(キーが0個の場所だけ)
        private DrumPart[] partsToAsk = Parts;

        // 初回起動か(4つとも空なら初回扱い。一部だけ空なら、その場所だけ聞く)
        private bool isFirstRun = true;

        // 叩くキーで、今何番目を聞いているか(partsToAsk.Length なら全部決まった)
        private int keyIndex = 0;

        // 点滅用
        private float blinkTimer = 0f;

        // 結果メッセージ
        private string message = "";
        private Color messageColor = Color.White;
        private float messageTimer = 0f;

        // セットアップ中は設定パネルを開かない
        public override bool CanOpenSettings => false;

        // 叩くキーの入力待ち中か(Game がスクショキーを無視するのに使う)
        public override bool IsWaitingKey => step == Step.Keys && keyIndex < partsToAsk.Length;

        // ===== 入る・更新 =====

        public override void Enter()
        {
            // キーが0個の場所だけ聞く(他の場所のキーは消さない)
            partsToAsk = ConfigManager.Key.GetEmptyParts().ToArray();
            isFirstRun = partsToAsk.Length == Parts.Length;
            keyIndex = 0;

            // 初回はようこそ画面から。一部だけ空なら、いきなり叩くキーの画面
            step = isFirstRun ? Step.Welcome : Step.Keys;
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
                        DrumPart part = partsToAsk[keyIndex];
                        if (ConfigManager.Key.TryAddDrumKey(part, key, out string result))
                        {
                            ShowMessage(result, SuccessColor);
                            keyIndex++; // 次の場所へ
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

            // Enterで次へ
            if (!Gui.IsPopupOpen && (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter)))
            {
                GoNext();
            }
        }

        // Escが押されたとき(Game から呼ばれる)
        public override void OnBack()
        {
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
            switch (step)
            {
                case Step.Welcome:
                    step = Step.Display;
                    break;

                case Step.Display:
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
            switch (step)
            {
                case Step.Welcome:
                    break; // 最初のページは戻れない

                case Step.Display:
                    step = Step.Welcome;
                    break;

                case Step.Keys:
                    if (keyIndex == 0)
                    {
                        // 初回なら画面の設定に戻る。一部だけ空のときは戻る先が無い
                        if (isFirstRun) step = Step.Display;
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
            if (step == Step.Keys && keyIndex == 0 && !isFirstRun) return false;
            return true;
        }

        private void Finish()
        {
            ConfigManager.SaveAll();
            SceneManager.ChangeScene(new HomeScene());
        }

        // ===== 描画 =====

        public override void Draw()
        {
            Gui.BeginFrame();

            // 背景
            Raylib.DrawRectangleGradientV(0, 0, Screen.Width, Screen.Height, new Color(18, 22, 40, 255), new Color(6, 7, 12, 255));
            Raylib.DrawRectangleRounded(Panel, 0.04f, 8, new Color(24, 28, 40, 250));

            // 上：タイトルとページ番号(ページ番号は初回だけ)
            FontManager.Draw(isFirstRun ? "初期設定" : "キーの設定", Left, Panel.Y + 30, 30, Gui.SubTextColor);
            if (isFirstRun)
            {
                string pageText = $"{(int)step + 1} / 4";
                float pageWidth = FontManager.Measure(pageText, 30).X;
                FontManager.Draw(pageText, Right - pageWidth, Panel.Y + 30, 30, Gui.SubTextColor);
            }

            switch (step)
            {
                case Step.Welcome: DrawWelcome(); break;
                case Step.Display: DrawDisplay(); break;
                case Step.Keys: DrawKeys(); break;
                case Step.Done: DrawDone(); break;
            }

            DrawMessage();
            DrawFooter();

            // プルダウンの一覧は一番上に
            Gui.DrawPopups();
        }

        // 1ページ目：ようこそ
        private void DrawWelcome()
        {
            FontManager.Draw("TaikoSimへようこそ！", Left, 300, 64, Color.White);
            FontManager.Draw("最初に、画面と叩くキーを設定します。", Left, 430, 32, Color.White);
            FontManager.Draw("あとからいつでも設定（F1）で変更できます。", Left, 480, 28, Gui.SubTextColor);
        }

        // 2ページ目：画面
        private void DrawDisplay()
        {
            Title("画面の設定");

            GameConfig game = ConfigManager.Game;
            bool changed = false;
            float y = 380;

            // 画面解像度
            FontManager.Draw("画面解像度", Left, y + 6, 30, Color.White);
            int resIndex = Array.FindIndex(GameConfig.Resolutions, r => r.Width == game.WindowWidth && r.Height == game.WindowHeight);
            int newResIndex = Gui.Dropdown("setupResolution", new Rectangle(ControlX, y, Right - ControlX, 44), ResolutionNames, resIndex);
            if (newResIndex != resIndex && newResIndex >= 0)
            {
                (game.WindowWidth, game.WindowHeight) = GameConfig.Resolutions[newResIndex];
                changed = true;
            }
            y += 80;

            // フルスクリーン
            bool fullscreen = Gui.Checkbox(new Rectangle(Left, y, Right - Left, 44), "フルスクリーン", game.Fullscreen);
            if (fullscreen != game.Fullscreen)
            {
                game.Fullscreen = fullscreen;
                changed = true;
            }
            y += 80;

            // フルスクリーン方式
            FontManager.Draw("フルスクリーン方式", Left, y + 6, 30, Color.White);
            int typeIndex = (int)game.FullscreenType;
            int newTypeIndex = Gui.Dropdown("setupFullscreenType", new Rectangle(ControlX, y, Right - ControlX, 44), FullscreenTypeNames, typeIndex);
            if (newTypeIndex != typeIndex)
            {
                game.FullscreenType = (FullscreenType)newTypeIndex;
                changed = true;
            }
            y += 90;

            FontManager.Draw("変更するとすぐに反映されます", Left, y, 24, Gui.SubTextColor);

            if (changed)
            {
                Screen.RequestApply();
            }
        }

        // 3ページ目：叩くキー
        private void DrawKeys()
        {
            Title("叩くキーの設定");
            string lead = isFirstRun
                ? "太鼓のそれぞれの場所に使うキーを押してください。"
                : "キーが設定されていない場所があります。キーを押してください。";
            FontManager.Draw(lead, Left, 330, 28, Color.White);

            float y = 400;
            foreach (DrumPart part in Parts)
            {
                string name = KeyConfig.GetDrumPartName(part);
                Rectangle row = new Rectangle(Left, y, Right - Left, 64);

                // この場所の状態(聞かなくていい場所は「決まってる」扱い)
                int askIndex = Array.IndexOf(partsToAsk, part);
                bool done = askIndex < 0 || askIndex < keyIndex;
                bool current = askIndex >= 0 && askIndex == keyIndex;

                if (done)
                {
                    // 決まった場所
                    Raylib.DrawRectangleRec(row, new Color(30, 50, 40, 255));
                    string keys = string.Join(", ", ConfigManager.Key.DrumKeys.GetValueOrDefault(part, new List<KeyboardKey>()));
                    FontManager.Draw($"●  {name}：{keys}", Left + 24, y + 14, 32, SuccessColor);
                }
                else if (current)
                {
                    // 今聞いている場所(点滅)
                    Raylib.DrawRectangleRec(row, new Color(18, 22, 36, 255));
                    Raylib.DrawRectangleLinesEx(row, 3, Gui.AccentColor);
                    if ((blinkTimer % 1f) < 0.7f)
                    {
                        FontManager.Draw($"→  {name}のキーを押してください", Left + 24, y + 14, 32, Gui.AccentColor);
                    }
                }
                else
                {
                    // まだの場所
                    Raylib.DrawRectangleRec(row, new Color(28, 31, 42, 255));
                    FontManager.Draw($"○  {name}", Left + 24, y + 14, 32, PendingColor);
                }
                y += 76;
            }

            // 説明
            if (keyIndex >= partsToAsk.Length)
            {
                FontManager.Draw("全部決まりました！「次へ」を押してください", Left, y + 6, 26, SuccessColor);
            }
            else
            {
                FontManager.Draw("※ Esc と、システムキー（F1 など）は使えません", Left, y + 6, 24, Gui.SubTextColor);
            }
            FontManager.Draw("※ 2個目以降のキーは、あとから設定（F1）で追加できます", Left, y + 44, 24, Gui.SubTextColor);
        }

        // 4ページ目：完了
        private void DrawDone()
        {
            Title("設定完了！");

            GameConfig game = ConfigManager.Game;
            float y = 360;

            string screenText = $"画面：{game.WindowWidth}×{game.WindowHeight}";
            if (game.Fullscreen) screenText += game.FullscreenType == FullscreenType.Borderless ? "（ボーダーレス）" : "（排他フルスクリーン）";
            FontManager.Draw(screenText, Left, y, 30, Color.White);
            y += 56;

            foreach (DrumPart part in Parts)
            {
                string keys = string.Join(", ", ConfigManager.Key.DrumKeys.GetValueOrDefault(part, new List<KeyboardKey>()));
                FontManager.Draw($"{KeyConfig.GetDrumPartName(part)}：{keys}", Left, y, 30, Color.White);
                y += 48;
            }
            y += 30;

            KeyboardKey settingsKey = ConfigManager.Key.SystemKeys.GetValueOrDefault(SystemAction.Settings, KeyboardKey.F1);
            FontManager.Draw($"設定はいつでも {settingsKey} で変更できます。", Left, y, 30, Gui.AccentColor);
            FontManager.Draw("「はじめる」を押すとホーム画面に進みます。", Left, y + 50, 26, Gui.SubTextColor);
        }

        // ページの大きいタイトル
        private static void Title(string text)
        {
            FontManager.Draw(text, Left, 250, 52, Color.White);
        }

        // 下の説明とボタン
        private void DrawFooter()
        {
            // 操作の説明(Escで戻れることに気づけるように、今の状況に合わせて変える)
            string hint;
            if (step == Step.Welcome)
            {
                hint = "Enter：次へ";
            }
            else if (IsWaitingKey)
            {
                if (keyIndex > 0) hint = $"Esc：{KeyConfig.GetDrumPartName(partsToAsk[keyIndex - 1])}をやり直す";
                else if (isFirstRun) hint = "Esc：前のページに戻る";
                else hint = ""; // 一部だけ空のときの最初の場所は戻れない
            }
            else
            {
                hint = "Esc：戻る　Enter：次へ";
            }
            if (hint.Length > 0)
            {
                FontManager.Draw(hint, Left, FooterY + 14, 26, Gui.SubTextColor);
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
            bool canNext = step != Step.Keys || keyIndex >= partsToAsk.Length;
            string nextText = step == Step.Done ? "はじめる" : "次へ";
            Rectangle nextRect = new Rectangle(Right - 210, FooterY, 210, 56);

            if (canNext)
            {
                if (Gui.Button(nextRect, nextText))
                {
                    GoNext();
                }
            }
            else
            {
                // 押せない状態(灰色)
                Raylib.DrawRectangleRec(nextRect, new Color(40, 42, 50, 255));
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
            FontManager.Draw(message, Left, 786, 28, messageColor);
        }
    }
}