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
    //   板の見た目と動きは、モード選択・選曲画面と同じ UI/BarList を使い回す。
    //   dan.def が複数あるときは、←→ でカテゴリを切り替えられる（TaikøNauts と同じ考え方）。
    //   操作（TaikøNauts の「操作説明」に合わせた）：
    //     カッ・↑↓・ホイール：えらぶ／ドン・Enter・クリック：決定／Esc：モード選択へ／F2：段位リストの再読み込み
    public sealed class DanSelectScene : Scene
    {
        private readonly BarList list = new(MenuSkin.ModeFolder);

        // 前に開いていた場所を覚えておく（段位から戻ってきたとき同じところに居る）
        private static int categoryCursor;
        private static int courseCursor;

        private DanCourse? deciding;             // 決定して、枠が点滅している段位
        private string toast = "";
        private float toastTimer;

        // 段位プレートの画像（dan.json の danPlatePath。スキンではなく段位のフォルダから直接読む）
        private readonly Dictionary<string, Texture2D> plates = new(StringComparer.OrdinalIgnoreCase);

        // 左の説明パネル
        private const float PanelX = 30f, PanelY = 150f, PanelWidth = 440f, PanelHeight = 760f;

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
            list.Reset(courseCursor);
            deciding = null;
            MenuSkin.Preload(MenuSkin.ModeFolder, MenuSkin.BarImages.Concat(new[] { "Background.png", "Background/0.png" }));
        }

        public override void Exit()
        {
            foreach (Texture2D t in plates.Values) if (t.Id != 0) Raylib.UnloadTexture(t);
            plates.Clear();
        }

        public override void OnBack() => BackToMode();

        private static void BackToMode() => SceneManager.ToModeFromSongSelect(new ModeSelectScene());

        public override void Update(float dt)
        {
            DanList.Update();
            if (toastTimer > 0f) toastTimer -= dt;

            // 読み込み中は何もできない
            if (DanList.IsScanning || !DanList.IsScanned) return;

            courseCursor = Math.Clamp(courseCursor, 0, Math.Max(0, Courses.Count - 1));
            list.Update(dt, courseCursor);

            // 決定したあとの点滅中。終わったら段位を始める
            if (deciding != null)
            {
                if (list.DecideTime < BarList.BlinkEnd) return;
                DanCourse course = deciding;
                deciding = null;
                list.EndDecide();
                Start(course);
                return;
            }

            if (Raylib.IsKeyPressed(KeyboardKey.F2))
            {
                DanList.Reload();
                ShowToast("段位リストを読み込み直しています…");
                return;
            }

            if (Courses.Count == 0) return;

            // ←→ でカテゴリを切り替え（dan.def が複数あるときだけ）
            if (Categories.Count > 1)
            {
                int categoryMove = 0;
                if (KeyPressed(KeyboardKey.Left)) categoryMove--;
                if (KeyPressed(KeyboardKey.Right)) categoryMove++;
                if (categoryMove != 0)
                {
                    categoryCursor = (categoryCursor + categoryMove + Categories.Count) % Categories.Count;
                    courseCursor = 0;
                    list.Reset(0);
                    MenuSounds.Ka();
                    ShowToast($"カテゴリ：{Category?.Title}");
                    return;
                }
            }

            // カッ(左縁)・↑ で上、カッ(右縁)・↓ で下
            int move = 0;
            if (InputManager.IsDrumPressed(DrumPart.LeftKa) || KeyPressed(KeyboardKey.Up)) move--;
            if (InputManager.IsDrumPressed(DrumPart.RightKa) || KeyPressed(KeyboardKey.Down)) move++;
            float wheel = Raylib.GetMouseWheelMove();
            if (wheel > 0) move--;
            else if (wheel < 0) move++;
            if (move != 0) Move(move);

            if (InputManager.IsDonPressed() || Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter))
                Decide();
        }

        private static bool KeyPressed(KeyboardKey key) => Raylib.IsKeyPressed(key) || Raylib.IsKeyPressedRepeat(key);

        private void Move(int direction)
        {
            // 段位一覧は、先頭と末尾で止まる（モード選択と同じ。ぐるっと回らない）
            int next = Math.Clamp(courseCursor + Math.Sign(direction), 0, Courses.Count - 1);
            if (next == courseCursor) return;
            courseCursor = next;
            list.Collapse();
            MenuSounds.Ka();
        }

        private void Decide()
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
            deciding = course;
            list.StartDecide();
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

            if (Courses.Count == 0)
            {
                DrawEmpty();
                return;
            }

            DrawList();
            DrawInfoPanel();
            DrawHeader();
            DrawFooter();
            if (toastTimer > 0f) DrawBanner(toast);
        }

        private void DrawBackground()
        {
            Rectangle screen = new(0, 0, Screen.Width, Screen.Height);
            if (MenuSkin.Whole(MenuSkin.ModeFolder + "Background.png", screen, Color.White)) return;
            if (MenuSkin.Whole(MenuSkin.ModeFolder + "Background/0.png", screen, Color.White)) return;
            Raylib.DrawRectangleGradientV(0, 0, Screen.Width, Screen.Height,
                new Color(28, 32, 58, 255), new Color(14, 16, 30, 255));
        }

        private void DrawHeader()
        {
            Outlined("段位道場", new Rectangle(40, 24, 420, 64), 52, Color.White, new Color(12, 12, 16, 255));
            if (Categories.Count > 1 && Category != null)
                Text($"◀ {Category.Title} ▶（{categoryCursor + 1}/{Categories.Count}）", 44, 96, 26, new Color(225, 228, 240, 255));
            else if (Category != null)
                Text(Category.Title, 44, 96, 26, new Color(225, 228, 240, 255));
        }

        private void DrawFooter()
        {
            string hint = Categories.Count > 1
                ? "カッ・↑↓：えらぶ　←→：カテゴリ　ドン・Enter：決定　Esc：モード選択へ　F2：読み込み直す"
                : "カッ・↑↓：えらぶ　ドン・Enter：決定　Esc：モード選択へ　F2：読み込み直す";
            var measured = Measure(hint, 24);
            Raylib.DrawRectangleRounded(new Rectangle(28, 1023, measured.X + 32, measured.Y + 14), 0.5f, 8, new Color(0, 0, 0, 150));
            Text(hint, 44, 1030, 24, new Color(220, 224, 236, 255));
        }

        private static void DrawBanner(string text)
        {
            float width = Measure(text, 26).X;
            Raylib.DrawRectangle(0, 130, (int)width + 40, 46, new Color(0, 0, 0, 210));
            Text(text, 20, 140, 26, new Color(255, 200, 60, 255));
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

        // 段位の板（斜めに並べる）
        private void DrawList()
        {
            bool clickable = deciding == null;
            Rectangle? selectedArea = null;
            DanCourse? selectedCourse = null;

            for (int pos = 0; pos < Courses.Count; pos++)
            {
                DanCourse course = Courses[pos];
                float distance = pos - list.Shown;
                Rectangle area = BarList.BarArea(distance);
                area.Y += Math.Clamp(distance, -1f, 1f) * 50f;   // モード選択と同じく上下を広めに
                if (area.Y > Screen.Height || area.Y + area.Height < 0) continue;

                bool selected = pos == courseCursor;
                if (selected)
                {
                    Rectangle opened = list.OpenArea(area);
                    float spread = 23f * EaseOut(list.Expand);
                    opened.Y -= spread;
                    opened.Height += spread * 2;
                    selectedArea = opened;
                    selectedCourse = course;
                }
                else DrawBar(course, area, 0f);

                if (clickable && Gui.Clicked(selected && selectedArea is Rectangle open ? open : area))
                {
                    if (selected) Decide();
                    else
                    {
                        courseCursor = pos;
                        list.Collapse();
                        MenuSounds.Ka();
                    }
                }
            }

            if (selectedArea is Rectangle sel && selectedCourse != null)
            {
                DrawBar(selectedCourse, sel, list.Expand);
                list.DrawFrame(sel, 1f);
            }
        }

        private void DrawBar(DanCourse course, Rectangle area, float open)
        {
            bool unlocked = DanUnlock.IsUnlocked(course, out _);
            bool usable = course.Playable && unlocked;

            // 段位が上がるほど赤くする（danIndex は 五級=0 … 初段=5 … のような並び）
            Color baseColor = DanColor(course.DanIndex);
            Color fill = usable ? baseColor : Raylib.ColorBrightness(baseColor, -0.5f);
            Rectangle inner = list.DrawBody(area, fill, 1f, plain: false);
            Color ink = Raylib.ColorBrightness(fill, -0.65f);
            Color text = usable ? Color.White : new Color(200, 200, 212, 255);

            // 段位プレートの画像があれば左に出す
            float textLeft = inner.X + 24;
            if (Plate(course) is Texture2D plate && plate.Id != 0)
            {
                float h = Math.Min(inner.Height - 16f, 96f);
                float w = h * plate.Width / Math.Max(1, plate.Height);
                Raylib.DrawTexturePro(plate, new Rectangle(0, 0, plate.Width, plate.Height),
                    new Rectangle(inner.X + 16, inner.Y + (inner.Height - h) / 2f, w, h),
                    System.Numerics.Vector2.Zero, 0f, usable ? Color.White : new Color(255, 255, 255, 140));
                textLeft = inner.X + 16 + w + 20;
            }

            if (open <= 0f)
            {
                Outlined(course.Title, new Rectangle(textLeft, inner.Y, inner.Width - (textLeft - inner.X) - 24, inner.Height), 50, text, ink);
                if (!unlocked) Text("かぎ", inner.X + inner.Width - 86, inner.Y + inner.Height / 2 - 16, 28, new Color(255, 220, 120, 255));
                return;
            }

            // 開いた板：段位名と、課題曲の数・ゲージ条件
            Outlined(course.Title, new Rectangle(textLeft, inner.Y + 26, inner.Width - (textLeft - inner.X) - 40, 92), 72, text, ink);

            string summary = unlocked
                ? $"課題曲 {course.Songs.Count} 曲　合格ゲージ {course.Gauge.Red}%"
                : "まだ解禁されていません";
            Rectangle pill = new(inner.X + 60, inner.Y + inner.Height - 108, inner.Width - 120, 68);
            Raylib.DrawRectangleRounded(pill, 1f, 12, Fade(new Color(255, 255, 255, 210), open));
            Centered(summary, new Rectangle(pill.X + 20, pill.Y, pill.Width - 40, pill.Height), 30, Fade(ink, open));
        }

        // 左の説明パネル：課題曲と合格条件
        private void DrawInfoPanel()
        {
            if (Current is not DanCourse course) return;

            Rectangle panel = new(PanelX, PanelY, PanelWidth, PanelHeight);
            Raylib.DrawRectangleRounded(panel, Roundness(panel, 18f), 10, new Color(10, 12, 24, 205));

            float x = PanelX + 22f, y = PanelY + 18f;
            Text(course.Title, x, y, 36, Color.White);
            y += 50f;

            if (course.MiniPlateText.Length > 0)
            {
                TextFit(course.MiniPlateText, x, y, PanelWidth - 44, 24, new Color(255, 220, 120, 255));
                y += 36f;
            }

            if (!DanUnlock.IsUnlocked(course, out string unlockError) && course.Requirement != null)
            {
                Text("解禁条件", x, y, 26, new Color(255, 200, 80, 255));
                y += 34f;
                y = WrapText(DanUnlock.Describe(course.Requirement), x, y, PanelWidth - 44, 22, new Color(235, 220, 190, 255));
                y += 10f;
            }
            else if (unlockError.Length > 0)
            {
                y = WrapText($"※ {unlockError}", x, y, PanelWidth - 44, 20, new Color(255, 180, 140, 255));
                y += 6f;
            }

            if (course.Error.Length > 0)
            {
                y = WrapText($"⚠ {course.Error}", x, y, PanelWidth - 44, 20, new Color(255, 140, 140, 255));
                y += 6f;
            }

            // 課題曲
            Text("課題曲", x, y, 26, new Color(160, 200, 255, 255));
            y += 34f;
            for (int i = 0; i < course.Songs.Count; i++)
            {
                DanSong song = course.Songs[i];
                string name = song.IsHidden ? "？？？" : song.Title.Length > 0 ? song.Title : song.RelativePath;
                TextFit($"{i + 1}. {name}", x, y, PanelWidth - 44, 24, Color.White);
                y += 30f;
                string detail = song.IsHidden ? "" : $"　　{CourseText(song.Course)}　★{song.Level}　{song.Genre}";
                if (detail.Length > 0)
                {
                    TextFit(detail, x, y, PanelWidth - 44, 20, new Color(190, 196, 214, 255));
                    y += 26f;
                }
            }
            y += 12f;

            // 合格条件
            Text("合格条件", x, y, 26, new Color(255, 180, 180, 255));
            y += 34f;
            Text($"魂ゲージ {course.Gauge.Red}% 以上" + (course.HasGoldGauge ? $"（金 {course.Gauge.Gold}%）" : ""),
                x, y, 22, new Color(225, 228, 240, 255));
            y += 28f;

            foreach (DanExam exam in course.Exams)
            {
                string range = exam.IsLess ? "未満" : "以上";
                if (!exam.PerSong)
                {
                    DanThreshold t = exam.Thresholds[0];
                    string gold = exam.HasGold(0) ? $"（金 {t.Gold}）" : "";
                    TextFit($"{exam.Label} {t.Red} {range}{gold}", x, y, PanelWidth - 44, 22, new Color(225, 228, 240, 255));
                    y += 28f;
                }
                else
                {
                    TextFit($"{exam.Label}（曲ごと）{range}", x, y, PanelWidth - 44, 22, new Color(225, 228, 240, 255));
                    y += 26f;
                    for (int i = 0; i < exam.Thresholds.Length; i++)
                    {
                        DanThreshold t = exam.Thresholds[i];
                        string gold = exam.HasGold(i) ? $" / 金 {t.Gold}" : "";
                        TextFit($"　　{i + 1}曲目：{t.Red}{gold}", x, y, PanelWidth - 44, 20, new Color(190, 196, 214, 255));
                        y += 24f;
                    }
                }
                if (y > PanelY + PanelHeight - 40f) break;
            }

            // 自己ベスト
            DanClearRecord best = SongHistory.GetDanRecord(SongHistory.KeyFor(course.JsonPath));
            if (best.Combo != SongClearType.None)
            {
                string label = best.Gold ? "金合格" : "赤合格";
                if (best.Combo == SongClearType.FullCombo) label += "・フルコンボ";
                else if (best.Combo == SongClearType.AllPerfect) label += "・全良";
                Text($"自己ベスト：{label}", x, PanelY + PanelHeight - 36f, 22,
                    best.Gold ? new Color(255, 215, 90, 255) : new Color(255, 150, 150, 255));
            }
        }

        // 折り返して書く。書き終わった次の y を返す
        private static float WrapText(string text, float x, float y, float maxWidth, float size, Color color)
        {
            string rest = text;
            int guard = 0;
            while (rest.Length > 0 && guard++ < 12)
            {
                int take = rest.Length;
                while (take > 1 && Measure(rest[..take], size).X > maxWidth) take--;
                Text(rest[..take], x, y, size, color);
                y += size + 6f;
                rest = rest[take..];
            }
            return y;
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

        // 段位が上がるほど赤く、下は青く
        private static Color DanColor(int danIndex)
        {
            float t = Math.Clamp(danIndex / 14f, 0f, 1f);
            return new Color(
                (byte)(70 + 150 * t),
                (byte)(110 - 50 * t),
                (byte)(175 - 95 * t),
                (byte)255);
        }

        private static string CourseText(CourseType course) => course switch
        {
            CourseType.Easy => "かんたん",
            CourseType.Normal => "ふつう",
            CourseType.Hard => "むずかしい",
            CourseType.Oni => "おに",
            CourseType.Edit => "裏おに",
            _ => course.ToString(),
        };
    }
}
