using Raylib_cs;
using TaikoSim.Audio;
using TaikoSim.Charts;
using TaikoSim.Configs;
using TaikoSim.Graphics;
using TaikoSim.Input;
using TaikoSim.UI;
using static TaikoSim.UI.MenuDraw;

namespace TaikoSim.Scenes
{
    // 段位選択画面（モード選択の「段位道場」から入る）
    //   見た目は TaikøNauts の動画（10/31 の 2026-10-31_01-44-20.mp4）を測った値に合わせている。
    //     ・上に段位の札が横一列（幅68px・x=190 から。選んでいる札だけ少し大きい）
    //     ・左に段位プレートの縦看板（dan.json の danPlatePath）
    //     ・中央にクリーム色の大パネル：課題曲3行（1st/2nd/3rd の色玉つき）と合格条件
    //     ・決定すると「この段位に挑戦するのか？」の確認が出る
    //   操作：カッ・←→・ホイール：えらぶ／ドン・Enter・クリック：決定／Esc：モード選択へ／F2：読み込み直し
    public sealed class DanSelectScene : Scene
    {
        // 前に開いていた場所を覚えておく（段位から戻ってきたとき同じところに居る）
        private static int categoryCursor;
        private static int courseCursor;

        // ===== 測った値（1920×1080） =====
        private const float TileX = 190f;        // 札の左はし
        private const float TilePitch = 68f;     // 札の間隔（＝札の幅）
        private const float TileY = 38f;         // 札の上
        private const float TileHeight = 152f;
        private const float TileGrow = 1.1f;     // 選んでいる札の大きさ

        private const float PanelX = 365f, PanelY = 215f, PanelWidth = 1480f, PanelHeight = 815f;
        private const float PanelBorder = 40f;   // 赤い枠の太さ

        private const float SongTop = 240f, SongStep = 110f, SongHeight = 90f;
        private const float SongLeft = 420f, SongRight = 1790f;
        private const float BallSize = 85f;      // 1st/2nd/3rd の色玉
        private const float LevelLeft = 525f, LevelSize = 85f;
        private const float TitleLeft = 635f;

        // 合格条件（動画：タブ y568〜612、条件の箱は y636 から 133px ずつ・高さ110。
        //   ラベルは箱の上辺にまたがって乗る。全体を1つの枠線が囲む）
        private const float CondTabY = 568f;
        private const float CondFrameTop = 605f, CondFrameBottom = 1010f;
        private const float CondTop = 636f;
        private const float GaugeBoxX = 437f, GaugeBoxWidth = 320f, GaugeBoxHeight = 149f;
        private const float CondBoxX = 775f, CondBoxWidth = 990f;
        private const float CondBoxHeight = 110f, CondBoxStep = 133f;

        private const float PlateX = 205f, PlateY = 320f, PlateHeight = 320f;

        // 色（動画から拾った）
        private static readonly Color Cream = new(250, 242, 218, 255);
        private static readonly Color PanelRed = new(214, 66, 54, 255);
        private static readonly Color Ink = new(44, 32, 26, 255);
        private static readonly Color TabBrown = new(96, 66, 44, 255);
        private static readonly Color BoxTan = new(236, 206, 164, 255);
        // 1st / 2nd / 3rd の色玉（課題曲と曲ごと条件で同じ色を使う）
        private static readonly Color[] SongColors =
        {
            new(150, 214, 108, 255),   // 1st 緑
            new(118, 198, 234, 255),   // 2nd 水色
            new(246, 160, 184, 255),   // 3rd ピンク
        };

        // 決定したときの点滅
        private DanCourse? deciding;
        private float decideTimer;
        private const float DecideBlink = 0.53f;

        // 確認ポップアップ
        private DanCourse? confirming;
        private bool confirmYes = true;
        private float confirmAge;

        private string toast = "";
        private float toastTimer;

        // 段位プレートの画像（スキンではなく段位のフォルダから直接読む）
        private readonly Dictionary<string, Texture2D> plates = new(StringComparer.OrdinalIgnoreCase);

        private static List<DanCategory> Categories => DanList.Categories;

        private static DanCategory? Category =>
            Categories.Count == 0 ? null : Categories[Math.Clamp(categoryCursor, 0, Categories.Count - 1)];

        private static List<DanCourse> Courses => Category?.Courses ?? new List<DanCourse>();

        private static DanCourse? Current =>
            Courses.Count == 0 ? null : Courses[Math.Clamp(courseCursor, 0, Courses.Count - 1)];

        public override void Enter()
        {
            if (!DanList.IsScanned && !DanList.IsScanning) DanList.StartScan();
            categoryCursor = Math.Clamp(categoryCursor, 0, Math.Max(0, Categories.Count - 1));
            courseCursor = Math.Clamp(courseCursor, 0, Math.Max(0, Courses.Count - 1));
            deciding = null;
            confirming = null;
        }

        public override void Exit()
        {
            foreach (Texture2D t in plates.Values) if (t.Id != 0) Raylib.UnloadTexture(t);
            plates.Clear();
        }

        public override void OnBack()
        {
            if (confirming != null) { CloseConfirm(); return; }
            SceneManager.ToModeFromSongSelect(new ModeSelectScene());
        }

        // ===== 更新 =====

        public override void Update(float dt)
        {
            DanList.Update();
            if (toastTimer > 0f) toastTimer -= dt;
            if (DanList.IsScanning || !DanList.IsScanned) return;

            courseCursor = Math.Clamp(courseCursor, 0, Math.Max(0, Courses.Count - 1));

            // 決定の点滅中。終わったら段位を始める
            if (deciding != null)
            {
                decideTimer += dt;
                if (decideTimer < DecideBlink) return;
                DanCourse course = deciding;
                deciding = null;
                Start(course);
                return;
            }

            if (confirming != null) { UpdateConfirm(dt); return; }

            if (Raylib.IsKeyPressed(KeyboardKey.F2))
            {
                DanList.Reload();
                ShowToast("段位リストを読み込み直しています…");
                return;
            }
            if (Courses.Count == 0) return;

            // ↑↓ でカテゴリを切り替え（dan.def が複数あるときだけ。←→ は段位の移動に使う）
            if (Categories.Count > 1)
            {
                int categoryMove = 0;
                if (KeyPressed(KeyboardKey.Up)) categoryMove--;
                if (KeyPressed(KeyboardKey.Down)) categoryMove++;
                if (categoryMove != 0)
                {
                    categoryCursor = (categoryCursor + categoryMove + Categories.Count) % Categories.Count;
                    courseCursor = 0;
                    MenuSounds.Ka();
                    ShowToast($"カテゴリ：{Category?.Title}");
                    return;
                }
            }

            // 札は横に並んでいるので、カッ(左縁)・← で左、カッ(右縁)・→ で右
            int move = 0;
            if (InputManager.IsDrumPressed(DrumPart.LeftKa) || KeyPressed(KeyboardKey.Left)) move--;
            if (InputManager.IsDrumPressed(DrumPart.RightKa) || KeyPressed(KeyboardKey.Right)) move++;
            float wheel = Raylib.GetMouseWheelMove();
            if (wheel > 0) move--;
            else if (wheel < 0) move++;
            if (move != 0) Move(move);

            if (InputManager.IsDonPressed() || Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter))
                OpenConfirm();
        }

        private static bool KeyPressed(KeyboardKey key) => Raylib.IsKeyPressed(key) || Raylib.IsKeyPressedRepeat(key);

        private void Move(int direction)
        {
            // 先頭と末尾で止まる（ぐるっと回らない）
            int next = Math.Clamp(courseCursor + Math.Sign(direction), 0, Courses.Count - 1);
            if (next == courseCursor) return;
            courseCursor = next;
            MenuSounds.Ka();
        }

        // ===== 確認ポップアップ =====

        private void OpenConfirm()
        {
            if (Current is not DanCourse course) return;

            if (!course.Playable)
            {
                MenuSounds.Ka();
                ShowToast(course.Error.Length > 0 ? course.Error : "この段位は遊べません");
                return;
            }
            if (!DanUnlock.IsUnlocked(course, out _))
            {
                MenuSounds.Ka();
                ShowToast("まだ解禁されていません");
                return;
            }

            MenuSounds.Don();
            confirming = course;
            confirmYes = true;
            confirmAge = 0f;
        }

        private void CloseConfirm()
        {
            confirming = null;
            MenuSounds.Ka();
        }

        private void UpdateConfirm(float dt)
        {
            confirmAge += dt;

            if (InputManager.IsDrumPressed(DrumPart.LeftKa) || InputManager.IsDrumPressed(DrumPart.RightKa)
                || KeyPressed(KeyboardKey.Left) || KeyPressed(KeyboardKey.Right))
            {
                confirmYes = !confirmYes;
                MenuSounds.Ka();
            }

            if (InputManager.IsDonPressed() || Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter))
            {
                DanCourse course = confirming!;
                confirming = null;
                if (confirmYes)
                {
                    MenuSounds.Don();
                    deciding = course;
                    decideTimer = 0f;
                }
                else MenuSounds.Ka();
            }
        }

        private static void Start(DanCourse course)
        {
            DanSong first = course.Songs[0];
            string title = first.Title.Length > 0 ? first.Title : Path.GetFileNameWithoutExtension(first.FullPath);
            SceneManager.ToPlayWithLoading(new PlayScene(course), title, course.Title);
        }

        private void ShowToast(string text)
        {
            toast = text;
            toastTimer = 2.5f;
        }

        // ===== 描画 =====

        public override void Draw()
        {
            Gui.BeginFrame();
            DrawBackground();

            if (DanList.IsScanning || !DanList.IsScanned)
            {
                Centered("段位を読み込み中…", new Rectangle(0, 460, Screen.Width, 80), 44, Color.White);
                return;
            }
            if (Courses.Count == 0) { DrawEmpty(); return; }

            DrawTiles();
            DrawPlate();
            DrawPanel();
            DrawFooter();

            if (confirming != null) DrawConfirm();
            if (toastTimer > 0f) DrawBanner(toast);
        }

        private static void DrawBackground()
        {
            Rectangle screen = new(0, 0, Screen.Width, Screen.Height);
            if (MenuSkin.Whole("Image/01.Entry/DanSelect/Background.png", screen, Color.White)) return;
            if (MenuSkin.Whole(MenuSkin.ModeFolder + "Background.png", screen, Color.White)) return;
            Raylib.DrawRectangleGradientV(0, 0, Screen.Width, Screen.Height,
                new Color(44, 30, 24, 255), new Color(20, 14, 12, 255));
        }

        // 上の段位の札（横一列）
        private void DrawTiles()
        {
            // 札が多すぎて画面からはみ出すときは、選んでいる札が真ん中に来るようにずらす
            float total = Courses.Count * TilePitch;
            float offset = 0f;
            float maxRight = Screen.Width - 250f;   // 右上のクレジット表示のぶんを空ける
            if (TileX + total > maxRight)
            {
                float want = TileX + courseCursor * TilePitch - (maxRight - TileX) / 2f;
                offset = -Math.Clamp(want - TileX, 0f, TileX + total - maxRight);
            }

            for (int i = 0; i < Courses.Count; i++)
            {
                float x = TileX + offset + i * TilePitch;
                if (x + TilePitch < 0 || x > Screen.Width) continue;
                DrawTile(Courses[i], x, i == courseCursor);
            }
        }

        private void DrawTile(DanCourse course, float x, bool selected)
        {
            Color baseColor = TileColor(course.DanIndex);
            bool usable = course.Playable && DanUnlock.IsUnlocked(course, out _);
            Color fill = usable ? baseColor : Raylib.ColorBrightness(baseColor, -0.45f);

            float w = TilePitch, h = TileHeight, y = TileY;
            if (selected)
            {
                // 選んでいる札だけ少し大きく、ちょっと下げて（動画では幅75px・y48〜219）
                w *= TileGrow;
                h *= TileGrow;
                x -= (w - TilePitch) / 2f;
                y += 10f;
            }

            Rectangle area = new(x + 2f, y, w - 4f, h);
            float round = Roundness(area, 10f);

            // 影 → 本体 → ふち
            Raylib.DrawRectangleRounded(new Rectangle(area.X + 3f, area.Y + 4f, area.Width, area.Height), round, 8,
                new Color(0, 0, 0, 90));
            Raylib.DrawRectangleRounded(area, round, 8, fill);
            Raylib.DrawRectangleRoundedLinesEx(area, round, 8, selected ? 4f : 2f,
                selected ? Color.White : Raylib.ColorBrightness(fill, -0.5f));

            // 段位名を縦書き（1文字ずつ下に並べる）
            string name = course.Title;
            float charSize = Math.Min(30f, (area.Height - 24f) / Math.Max(1, name.Length));
            float cy = area.Y + (area.Height - charSize * name.Length) / 2f;
            Color text = usable ? Color.White : new Color(205, 205, 215, 255);
            foreach (char c in name)
            {
                Outlined(c.ToString(), new Rectangle(area.X, cy, area.Width, charSize), charSize * 0.96f,
                    text, Raylib.ColorBrightness(fill, -0.65f));
                cy += charSize;
            }
        }

        // 札の色（動画の色分け：級＝茶、初段〜五段＝青緑、六段〜十段＝赤、玄人〜達人＝灰、外伝＝濃緑）
        private static Color TileColor(int danIndex) => danIndex switch
        {
            <= 4 => new Color(150, 118, 70, 255),     // 五級〜一級
            <= 9 => new Color(72, 158, 160, 255),     // 初段〜五段
            <= 14 => new Color(226, 76, 60, 255),     // 六段〜十段
            <= 18 => new Color(126, 128, 138, 255),   // 玄人〜達人
            _ => new Color(58, 104, 76, 255),         // 外伝
        };

        // 左の段位プレート（縦看板）
        private void DrawPlate()
        {
            if (Current is not DanCourse course) return;
            if (Plate(course) is not Texture2D plate || plate.Id == 0) return;

            float h = PlateHeight;
            float w = h * plate.Width / Math.Max(1, plate.Height);
            Raylib.DrawTexturePro(plate, new Rectangle(0, 0, plate.Width, plate.Height),
                new Rectangle(PlateX, PlateY, w, h), System.Numerics.Vector2.Zero, 0f, Color.White);
        }

        // 中央のクリーム色の大パネル
        private void DrawPanel()
        {
            if (Current is not DanCourse course) return;

            Rectangle outer = new(PanelX, PanelY, PanelWidth, PanelHeight);
            Raylib.DrawRectangleRounded(outer, Roundness(outer, 14f), 10, PanelRed);
            Rectangle inner = new(PanelX + PanelBorder, PanelY + 14f, PanelWidth - PanelBorder * 2f, PanelHeight - 28f);
            Raylib.DrawRectangleRounded(inner, Roundness(inner, 10f), 10, Cream);

            DrawSongRows(course);
            DrawConditions(course);
        }

        private void DrawSongRows(DanCourse course)
        {
            for (int i = 0; i < course.Songs.Count && i < 5; i++)
            {
                DanSong song = course.Songs[i];
                float y = SongTop + i * SongStep;
                Rectangle row = new(SongLeft, y, SongRight - SongLeft, SongHeight);
                Raylib.DrawRectangleRounded(row, Roundness(row, 16f), 10, new Color(255, 255, 255, 235));

                // 1st / 2nd / 3rd の色玉
                Color ball = SongColors[Math.Min(i, SongColors.Length - 1)];
                Rectangle ballBox = new(SongLeft + 5f, y + (SongHeight - BallSize) / 2f, BallSize, BallSize);
                Raylib.DrawCircleV(new System.Numerics.Vector2(ballBox.X + BallSize / 2f, ballBox.Y + BallSize / 2f),
                    BallSize / 2f, ball);
                Outlined(Ordinal(i), ballBox, 28f, Color.White, Raylib.ColorBrightness(ball, -0.5f));

                // 難易度と★
                Rectangle levelBox = new(LevelLeft, y + (SongHeight - LevelSize) / 2f, LevelSize, LevelSize);
                Raylib.DrawRectangleRounded(levelBox, Roundness(levelBox, 10f), 8, CourseColor(song.Course));
                Outlined($"★{song.Level}", levelBox, 26f, Color.White, new Color(24, 20, 28, 255));

                // 曲名（伏せる指定なら「？？？」）
                string name = song.IsHidden ? "？？？" : song.Title.Length > 0 ? song.Title : song.RelativePath;
                TextFit(name, TitleLeft, y + 22f, SongRight - TitleLeft - 24f, 42f, Ink);
            }
        }

        private void DrawConditions(DanCourse course)
        {
            // 「合格条件」の茶色いタブ
            // 全体を囲む枠線 → 「合格条件」の茶色いタブ
            Rectangle frame = new(SongLeft, CondFrameTop, SongRight - SongLeft, CondFrameBottom - CondFrameTop);
            Raylib.DrawRectangleRoundedLinesEx(frame, Roundness(frame, 18f), 10, 3f, new Color(120, 96, 72, 255));
            Rectangle tab = new(SongLeft + 5f, CondTabY, 405f, 44f);
            Raylib.DrawRectangleRounded(tab, Roundness(tab, 10f), 8, TabBrown);
            Centered("合格条件", tab, 30f, Cream);

            // 魂ゲージは左に独立
            DrawCondBox(new Rectangle(GaugeBoxX, CondTop, GaugeBoxWidth, GaugeBoxHeight),
                "魂ゲージ", GaugeNeedText(course), null, 0);

            // ほかの条件は右に縦積み
            float y = CondTop;
            for (int i = 0; i < course.Exams.Count; i++)
            {
                DanExam exam = course.Exams[i];
                if (y + CondBoxHeight > CondFrameBottom + 4f) break;   // 入りきらないぶんは出さない
                DrawCondBox(new Rectangle(CondBoxX, y, CondBoxWidth, CondBoxHeight), exam.Label, "", exam, 0);
                y += CondBoxStep;
            }
        }

        private static string GaugeNeedText(DanCourse course)
        {
            if (course.Gauge.Red <= 0) return "条件なし";
            return course.HasGoldGauge ? $"{course.Gauge.Red} ％以上（金 {course.Gauge.Gold}）" : $"{course.Gauge.Red} ％以上";
        }

        // 条件1つの箱：上に茶色いラベル、中に「○○ 未満／以上」、下に空の進捗バー
        private void DrawCondBox(Rectangle box, string label, string need, DanExam? exam, int songIndex)
        {
            Raylib.DrawRectangleRounded(box, Roundness(box, 12f), 8, new Color(255, 255, 255, 215));
            Raylib.DrawRectangleRoundedLinesEx(box, Roundness(box, 12f), 8, 2f, new Color(170, 150, 128, 255));

            // ラベルは箱の上辺にまたがって乗せる
            Rectangle tab = new(box.X + 22f, box.Y - 17f, Math.Min(296f, box.Width - 44f), 34f);
            Raylib.DrawRectangleRounded(tab, Roundness(tab, 9f), 8, BoxTan);
            Centered(label, tab, 22f, Ink);

            if (exam == null)
            {
                // 魂ゲージ
                Text(need, box.X + 36f, box.Y + 28f, 32f, Ink);
                Rectangle bar = new(box.X + 14f, box.Y + box.Height - 34f, box.Width - 28f, 24f);
                Raylib.DrawRectangleRounded(bar, 1f, 8, new Color(255, 255, 255, 255));
                Raylib.DrawRectangleRoundedLinesEx(bar, 1f, 8, 2f, new Color(190, 180, 160, 255));
                return;
            }

            string range = exam.IsLess ? "未満" : "以上";
            if (!exam.PerSong)
            {
                Text($"{exam.Thresholds[0].Red} {range}", box.X + 150f, box.Y + 22f, 30f, Ink);
                if (exam.HasGold(0))
                    Text($"金 {exam.Thresholds[0].Gold}", box.X + box.Width - 150f, box.Y + 26f, 22f, new Color(170, 130, 40, 255));
                Rectangle bar = new(box.X + 14f, box.Y + box.Height - 30f, box.Width - 28f, 22f);
                Raylib.DrawRectangleRounded(bar, 1f, 8, Color.White);
                Raylib.DrawRectangleRoundedLinesEx(bar, 1f, 8, 2f, new Color(190, 180, 160, 255));
                return;
            }

            // 曲ごと条件：1st / 2nd / 3rd を横に並べる（課題曲の色玉と同じ色）
            int count = Math.Min(exam.Thresholds.Length, 5);
            float cellWidth = (box.Width - 28f) / count;
            for (int i = 0; i < count; i++)
            {
                float cx = box.X + 14f + i * cellWidth;
                Color ball = SongColors[Math.Min(i, SongColors.Length - 1)];
                Rectangle badge = new(cx, box.Y + 20f, 54f, 30f);
                Raylib.DrawRectangleRounded(badge, 1f, 8, ball);
                Centered(Ordinal(i), badge, 20f, Color.White);
                Text($"{exam.Thresholds[i].Red} {range}", cx + 62f, box.Y + 22f, 26f, Ink);

                Rectangle bar = new(cx, box.Y + box.Height - 28f, cellWidth - 12f, 20f);
                Raylib.DrawRectangleRounded(bar, 1f, 8, Color.White);
                Raylib.DrawRectangleRoundedLinesEx(bar, 1f, 8, 2f, new Color(190, 180, 160, 255));
            }
        }

        private static string Ordinal(int index) => index switch
        {
            0 => "1st",
            1 => "2nd",
            2 => "3rd",
            _ => $"{index + 1}th",
        };

        private static Color CourseColor(CourseType course) => course switch
        {
            CourseType.Easy => new Color(228, 72, 92, 255),
            CourseType.Normal => new Color(176, 136, 78, 255),
            CourseType.Hard => new Color(100, 150, 70, 255),
            CourseType.Oni => new Color(136, 78, 180, 255),
            CourseType.Edit => new Color(84, 148, 176, 255),
            _ => new Color(110, 110, 120, 255),
        };

        // 「この段位に挑戦するのか？」
        private void DrawConfirm()
        {
            if (confirming is not DanCourse course) return;
            float t = Math.Clamp(confirmAge / 0.18f, 0f, 1f);

            Raylib.DrawRectangle(0, 0, Screen.Width, Screen.Height, new Color(0, 0, 0, (int)(150 * t)));

            float w = 900f, h = 360f;
            float scale = 0.9f + 0.1f * EaseOut(t);
            Rectangle box = new(Screen.Width / 2f - w * scale / 2f, Screen.Height / 2f - h * scale / 2f, w * scale, h * scale);
            Raylib.DrawRectangleRounded(box, Roundness(box, 20f), 10, PanelRed);
            Rectangle inner = new(box.X + 16f, box.Y + 16f, box.Width - 32f, box.Height - 32f);
            Raylib.DrawRectangleRounded(inner, Roundness(inner, 16f), 10, Cream);

            Centered($"「{course.Title}」に挑戦するのか？", new Rectangle(inner.X, inner.Y + 40f, inner.Width, 60f), 40f, Ink);

            float buttonWidth = 300f, buttonHeight = 86f;
            float by = inner.Y + inner.Height - buttonHeight - 40f;
            DrawConfirmButton(new Rectangle(inner.X + inner.Width / 2f - buttonWidth - 20f, by, buttonWidth, buttonHeight),
                "挑戦する", confirmYes);
            DrawConfirmButton(new Rectangle(inner.X + inner.Width / 2f + 20f, by, buttonWidth, buttonHeight),
                "挑戦しない", !confirmYes);
        }

        private void DrawConfirmButton(Rectangle box, string label, bool selected)
        {
            Color fill = selected ? new Color(253, 253, 0, 255) : new Color(230, 222, 200, 255);
            Raylib.DrawRectangleRounded(box, Roundness(box, 14f), 8, fill);
            Raylib.DrawRectangleRoundedLinesEx(box, Roundness(box, 14f), 8, selected ? 5f : 2f,
                selected ? new Color(214, 66, 54, 255) : new Color(180, 170, 150, 255));
            Centered(label, box, 34f, Ink);

            if (Gui.Clicked(box))
            {
                confirmYes = label == "挑戦する";
                DanCourse course = confirming!;
                confirming = null;
                if (confirmYes) { MenuSounds.Don(); deciding = course; decideTimer = 0f; }
                else MenuSounds.Ka();
            }
        }

        private void DrawFooter()
        {
            string hint = confirming != null
                ? "カッ・←→：えらぶ　ドン・Enter：決定　Esc：やめる"
                : Categories.Count > 1
                    ? "カッ・←→：えらぶ　↑↓：カテゴリ　ドン・Enter：決定　Esc：モード選択へ　F2：読み込み直す"
                    : "カッ・←→：えらぶ　ドン・Enter：決定　Esc：モード選択へ　F2：読み込み直す";
            var measured = Measure(hint, 24);
            Raylib.DrawRectangleRounded(new Rectangle(28, 1023, measured.X + 32, measured.Y + 14), 0.5f, 8, new Color(0, 0, 0, 150));
            Text(hint, 44, 1030, 24, new Color(220, 224, 236, 255));

            if (Category != null && Categories.Count > 1)
                Text($"{Category.Title}（{categoryCursor + 1}/{Categories.Count}）", Screen.Width - 360f, 1030f, 24,
                    new Color(220, 224, 236, 255));
        }

        private static void DrawBanner(string text)
        {
            float width = Measure(text, 26).X;
            Raylib.DrawRectangle(0, 200, (int)width + 40, 46, new Color(0, 0, 0, 210));
            Text(text, 20, 210, 26, new Color(255, 200, 60, 255));
        }

        private void DrawEmpty()
        {
            Text("段位がありません", 160, 360, 40, new Color(255, 120, 120, 255));
            Text("Songs フォルダの中に、段位のフォルダを入れてください。", 160, 430, 28, Color.White);
            Text(SongList.SongsFolder, 160, 480, 24, new Color(255, 236, 160, 255));
            Text("段位をまとめるフォルダに dan.def（中に #TITLE:名前）を置き、", 160, 540, 28, Color.White);
            Text("その下の段位ごとのフォルダに dan.json と譜面・音源を入れます。", 160, 582, 28, Color.White);
            Text("入れたら F2 で読み込み直してください。", 160, 640, 28, Color.White);
            DrawFooter();
        }

        private Texture2D? Plate(DanCourse course)
        {
            if (course.PlatePath.Length == 0) return null;
            string full = Path.Combine(course.FolderPath, course.PlatePath);
            if (plates.TryGetValue(full, out Texture2D cached)) return cached.Id == 0 ? null : cached;

            Texture2D texture = default;
            try
            {
                if (File.Exists(full)) texture = Raylib.LoadTexture(full);
            }
            catch { /* 読めなければ画像なしで描く */ }
            plates[full] = texture;   // 読めなかったことも覚える(毎フレーム試さないように)
            return texture.Id == 0 ? null : texture;
        }
    }
}
