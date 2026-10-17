using System.Diagnostics;
using Raylib_cs;
using TaikoSim.Charts;
using TaikoSim.Configs;
using TaikoSim.Graphics;
using TaikoSim.Input;
using TaikoSim.UI;

namespace TaikoSim.Scenes
{
    // 選曲画面(仮)
    // カテゴリ → 曲 → 難易度 の3段。クリック(Enter)で開いて、難易度をクリック(Enter)すると演奏画面へ
    // 検索の箱に文字を入れると、カテゴリ関係なく当てはまる曲だけが並ぶ
    public class SongSelectScene : Scene
    {
        // ===== 配置(1920×1080) =====
        private const float ListX = 80;
        private const float ListRight = 1840;
        private const float ListTop = 170;
        private const float ListBottom = 990;

        private const float CategoryRowHeight = 72;
        private const float SongRowHeight = 62;
        private const float CourseRowHeight = 50;
        private const float SongIndent = 60;    // 曲の行の字下げ
        private const float CourseIndent = 130; // 難易度の行の字下げ

        // 検索の箱
        private static readonly Rectangle SearchBox = new Rectangle(260, 60, 840, 56);
        private const int SearchMaxLength = 40;

        // 色
        private static readonly Color RowColor = new Color(30, 34, 48, 255);
        private static readonly Color RowHoverColor = new Color(42, 48, 68, 255);
        private static readonly Color CursorColor = new Color(255, 200, 60, 255);
        private static readonly Color ErrorColor = new Color(255, 110, 100, 255);

        // 画面の1行
        //   Song < 0 → カテゴリの行、Course < 0 → 曲の行、それ以外 → 難易度の行
        //   Cat はいつも「その曲が入っているカテゴリの番号」(検索中も同じ)
        private readonly record struct Row(int Cat, int Song, int Course, float Y, float Height)
        {
            public bool IsCategory => Song < 0;
            public bool IsSong => Song >= 0 && Course < 0;
            public bool IsCourse => Course >= 0;
        }

        // 演奏から戻ってきたときに、同じ場所を選んだままにするため static にしている
        private static int cursorCat = 0;     // 選んでいる行(カテゴリ・曲・難易度の番号)
        private static int cursorSong = -1;
        private static int cursorCourse = -1;
        private static int openCat = -1;      // 開いているカテゴリ(-1 なら全部閉じている)
        private static int openSongCat = -1;  // 難易度を開いている曲(カテゴリの番号と曲の番号)
        private static int openSong = -1;
        private static float scroll = 0f;

        // ===== 検索 =====
        private static string searchText = "";                  // 打った文字
        private static bool searchFocused = false;              // 検索の箱に入力中か
        private static readonly List<(int Cat, int Song)> results = new(); // 当てはまった曲
        private float caretTimer = 0f;                          // 入力中の「|」の点滅

        // 検索中か(箱に文字が入っている間)
        private static bool IsSearching => searchText.Length > 0;

        private float scrollTarget = 0f;
        private readonly List<Row> rows = new();

        // 設定パネルが開いている間は、後ろのこの画面をクリックできないようにする
        private bool active = true;
        private static readonly Rectangle NoClickArea = new Rectangle(0, 0, 0, 0);

        // 前のフレームで曲を読み込み中だったか(読み終わった瞬間に気づくため)
        private bool wasScanning = false;

        // ===== 入る・更新 =====

        public override void Enter()
        {
            // 曲はふつう起動したときの読み込み画面で読み終わっている
            // 念のため、まだ1回も読んでいなければここで読み始める
            if (!SongList.IsScanned && !SongList.IsScanning) SongList.StartScan();
            wasScanning = SongList.IsScanning;
            ValidateCursor();
            searchFocused = false; // 演奏から戻ってきたときは入力中にしない(検索の結果はそのまま)
            scrollTarget = scroll;
        }

        // 一覧を作り直す(開いているものと検索は全部やめる)
        // 読むのは裏でやるので、この画面は止まらない。読み終わるまでは「読み込み中」を出す
        private void Refresh()
        {
            if (SongList.IsScanning) return; // 読み込み中にもう一度押されても何もしない

            ClearSearch();
            openCat = -1;
            CloseSong();
            SetCursorToTop();
            scroll = 0;
            scrollTarget = 0;

            SongList.StartScan();
            wasScanning = true;
        }

        public override void Update(float dt)
        {
            caretTimer += dt;

            // 曲を読み込み中は、読み終わるまで操作できない(Esc でホームに戻るのはOK)
            // ※新しい一覧への入れ替えは、Game がこの画面より先に SongList.Update() でやっている
            bool scanning = SongList.IsScanning;
            if (wasScanning && !scanning)
            {
                // 読み終わった → 新しい一覧の一番上から(一覧を更新したときは、開いているものは全部閉じてある)
                SetCursorToTop();
                ValidateCursor();
            }
            wasScanning = scanning;
            if (scanning)
            {
                searchFocused = false;
                DiscardTypedChars();
                return;
            }

            // 検索キー(F3)で検索の箱へ
            if (InputManager.IsSystemPressed(SystemAction.Search))
            {
                searchFocused = true;
                caretTimer = 0f;
            }

            // 文字の入力(入力中じゃないときは、たまった文字を捨てる)
            if (searchFocused) UpdateSearchInput();
            else DiscardTypedChars();

            if (SongList.Categories.Count == 0) return;
            BuildRows();

            // ↑↓：1行ずつ移動(押しっぱなしで連続、端まで行くと反対側へ)
            if (KeyPressed(KeyboardKey.Down)) MoveCursor(+1);
            if (KeyPressed(KeyboardKey.Up)) MoveCursor(-1);

            // 入力中は ←→Enter を文字の入力の方に使う
            if (searchFocused) return;

            // Enter・→：開く(難易度なら演奏へ)
            if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter) || Raylib.IsKeyPressed(KeyboardKey.Right))
            {
                if (rows.Count > 0) Decide(cursorCat, cursorSong, cursorCourse);
            }

            // ←：1段閉じる
            if (Raylib.IsKeyPressed(KeyboardKey.Left))
            {
                CloseOneLevel();
            }
        }

        // Esc：入力中なら検索をやめる → 1段閉じる → 検索中なら検索をやめる → ホームへ
        public override void OnBack()
        {
            if (searchFocused)
            {
                ClearSearch();
                return;
            }
            if (CloseOneLevel()) return;
            if (IsSearching)
            {
                ClearSearch();
                return;
            }
            SceneManager.ChangeScene(new HomeScene());
        }

        // .tja がドロップされたら、今まで通りテスト表示
        public override void OnFilesDropped(string[] paths)
        {
            string? tja = paths.FirstOrDefault(TestPlayScene.IsTja);
            if (tja != null) SceneManager.ChangeScene(new TestPlayScene(tja));
        }

        // ===== 検索 =====

        // 検索の箱に入力中の処理
        private void UpdateSearchInput()
        {
            string before = searchText;

            // 打った文字を足す(日本語入力で確定した文字もここに来る)
            int c = Raylib.GetCharPressed();
            while (c > 0)
            {
                // 32 より小さいのは制御文字。0xD800〜0xDFFF は文字として使えない番号
                bool usable = c >= 32 && c <= 0x10FFFF && (c < 0xD800 || c > 0xDFFF);
                if (usable && searchText.Length < SearchMaxLength)
                {
                    searchText += char.ConvertFromUtf32(c);
                }
                c = Raylib.GetCharPressed();
            }

            // BackSpace：1文字消す(押しっぱなしで連続)
            if ((Raylib.IsKeyPressed(KeyboardKey.Backspace) || Raylib.IsKeyPressedRepeat(KeyboardKey.Backspace)) && searchText.Length > 0)
            {
                // 絵文字みたいな2つで1文字の文字も、まとめて消す
                int remove = searchText.Length >= 2 && char.IsLowSurrogate(searchText[^1]) ? 2 : 1;
                searchText = searchText[..^remove];
            }

            // Enter：入力をおわる(結果はそのまま)
            if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter))
            {
                searchFocused = false;
            }

            if (searchText != before) UpdateResults();
        }

        // 入力中じゃないときに打たれた文字は使わないので捨てる(あとで箱を開いたときに勝手に入らないように)
        private static void DiscardTypedChars()
        {
            while (Raylib.GetCharPressed() > 0) { }
        }

        // 検索の結果を作り直す
        private void UpdateResults()
        {
            results.Clear();
            CloseSong();

            if (IsSearching)
            {
                string query = SongSearch.Normalize(searchText);
                List<SongCategory> categories = SongList.Categories;
                for (int c = 0; c < categories.Count; c++)
                {
                    for (int s = 0; s < categories[c].Songs.Count; s++)
                    {
                        if (SongSearch.Matches(categories[c].Songs[s].SearchKey, query)) results.Add((c, s));
                    }
                }
            }

            // カーソルは結果の一番上へ(結果が無ければそのまま)
            if (IsSearching && results.Count > 0) SetCursor(results[0].Cat, results[0].Song, -1);
            else if (!IsSearching)
            {
                if (openCat >= 0) SetCursor(openCat, -1, -1);
                else SetCursorToTop();
            }
            scroll = 0;
            scrollTarget = 0;
        }

        // 検索をやめて、元のカテゴリの一覧に戻る
        private void ClearSearch()
        {
            bool wasSearching = IsSearching;
            searchText = "";
            searchFocused = false;
            if (wasSearching) UpdateResults();
            else results.Clear();
        }

        // ===== 操作 =====

        // 決定(カテゴリ・曲なら開く／閉じる、難易度なら演奏)
        private void Decide(int cat, int song, int course)
        {
            SongCategory category = SongList.Categories[cat];

            // ----- カテゴリ -----
            if (song < 0)
            {
                CloseSong();
                if (openCat == cat)
                {
                    openCat = -1;
                    SetCursor(cat, -1, -1);
                }
                else
                {
                    openCat = cat;
                    SetCursor(cat, category.Songs.Count > 0 ? 0 : -1, -1); // 開いたら最初の曲へ
                }
                return;
            }

            // ----- 曲 -----
            SongEntry entry = category.Songs[song];
            if (course < 0)
            {
                if (entry.HasError) return; // 読めなかった曲は開けない

                if (openSongCat == cat && openSong == song)
                {
                    CloseSong();
                    SetCursor(cat, song, -1);
                }
                else
                {
                    openSongCat = cat;
                    openSong = song;
                    SetCursor(cat, song, entry.Courses.Count > 0 ? 0 : -1); // 開いたら最初の難易度へ
                }
                return;
            }

            // ----- 難易度 → 演奏へ(戻ってきたら選曲画面に戻る) -----
            SetCursor(cat, song, course);
            CourseType type = entry.Courses[course].Type;
            SceneManager.ChangeScene(new TestPlayScene(entry.TjaPath, type, returnToSelect: true));
        }

        // 1段閉じる(難易度 → カテゴリ の順。検索中はカテゴリは無い)。閉じるものが無ければ false
        private static bool CloseOneLevel()
        {
            if (openSong >= 0)
            {
                SetCursor(openSongCat, openSong, -1);
                CloseSong();
                return true;
            }
            if (!IsSearching && openCat >= 0)
            {
                // カーソルがそのカテゴリの中にあれば、カテゴリの行へ戻す
                // (カテゴリに入らない曲の上にあるときは、カーソルはそのまま)
                if (cursorCat == openCat) SetCursor(openCat, -1, -1);
                openCat = -1;
                return true;
            }
            return false;
        }

        // カーソルを一覧の一番上へ
        // ふつうは最初のカテゴリの行。カテゴリが無くて、カテゴリに入らない曲しかないときは最初の曲
        private static void SetCursorToTop()
        {
            List<SongCategory> categories = SongList.Categories;
            if (categories.Count > 0 && categories[0].IsLoose) SetCursor(0, 0, -1);
            else SetCursor(0, -1, -1);
        }

        private static void CloseSong()
        {
            openSongCat = -1;
            openSong = -1;
        }

        private static bool IsSongOpen(int cat, int song) => openSongCat == cat && openSong == song;

        // 上下に1行動かす(今見えている行の並びで動く)
        private void MoveCursor(int direction)
        {
            if (rows.Count == 0) return;
            int index = FindCursorRow();
            index = (index + direction + rows.Count) % rows.Count;
            SetCursor(rows[index].Cat, rows[index].Song, rows[index].Course);
        }

        private int FindCursorRow()
        {
            int index = rows.FindIndex(r => r.Cat == cursorCat && r.Song == cursorSong && r.Course == cursorCourse);
            return Math.Max(0, index);
        }

        private static void SetCursor(int cat, int song, int course)
        {
            cursorCat = cat;
            cursorSong = song;
            cursorCourse = course;
        }

        // 一覧を作り直したあとなどに、番号が一覧からはみ出していたら最初に戻す
        private static void ValidateCursor()
        {
            List<SongCategory> categories = SongList.Categories;

            bool SongExists(int cat, int song) =>
                cat >= 0 && cat < categories.Count && song >= 0 && song < categories[cat].Songs.Count;

            bool ok = cursorCat >= 0 && cursorCat < categories.Count
                && (cursorSong < 0 || SongExists(cursorCat, cursorSong))
                && !(cursorSong < 0 && categories[cursorCat].IsLoose) // 入れ物にはカテゴリの行が無い
                && (cursorCourse < 0 || (cursorSong >= 0 && cursorCourse < categories[cursorCat].Songs[cursorSong].Courses.Count))
                && openCat < categories.Count
                && !(openCat >= 0 && categories[openCat].IsLoose)
                && (openSong < 0 || SongExists(openSongCat, openSong))
                && results.All(r => SongExists(r.Cat, r.Song));

            if (!ok)
            {
                searchText = "";
                results.Clear();
                openCat = -1;
                CloseSong();
                SetCursorToTop();
                scroll = 0;
            }
        }

        private static bool KeyPressed(KeyboardKey key)
        {
            return Raylib.IsKeyPressed(key) || Raylib.IsKeyPressedRepeat(key);
        }

        // 今の状態から行の並びを作る
        //   ふつう：カテゴリ ＋ 開いているカテゴリの曲 ＋ 開いている曲の難易度
        //   検索中：当てはまった曲 ＋ 開いている曲の難易度
        private void BuildRows()
        {
            rows.Clear();
            List<SongCategory> categories = SongList.Categories;
            float y = 0;

            if (IsSearching)
            {
                foreach (var (c, s) in results)
                {
                    AddSongRows(categories, c, s, ref y);
                }
                return;
            }

            for (int c = 0; c < categories.Count; c++)
            {
                // カテゴリに入らない曲は、カテゴリの行を出さずにそのまま並べる
                if (categories[c].IsLoose)
                {
                    for (int s = 0; s < categories[c].Songs.Count; s++)
                    {
                        AddSongRows(categories, c, s, ref y);
                    }
                    continue;
                }

                rows.Add(new Row(c, -1, -1, y, CategoryRowHeight));
                y += CategoryRowHeight + 8;
                if (c != openCat) continue;

                for (int s = 0; s < categories[c].Songs.Count; s++)
                {
                    AddSongRows(categories, c, s, ref y);
                }
                y += 10;
            }
        }

        // 曲の行(＋開いていれば難易度の行)を足す
        private void AddSongRows(List<SongCategory> categories, int c, int s, ref float y)
        {
            rows.Add(new Row(c, s, -1, y, SongRowHeight));
            y += SongRowHeight + 6;
            if (!IsSongOpen(c, s)) return;

            for (int k = 0; k < categories[c].Songs[s].Courses.Count; k++)
            {
                rows.Add(new Row(c, s, k, y, CourseRowHeight));
                y += CourseRowHeight + 4;
            }
            y += 6;
        }

        // ===== 描画 =====

        public override void Draw()
        {
            Gui.BeginFrame();
            active = !Game.IsSettingsOpen;
            Clip(null);

            Raylib.ClearBackground(new Color(20, 22, 32, 255));
            DrawContent();

            // 設定パネルの方に影響しないように、クリックできる範囲を元に戻す
            Gui.ResetClip();
        }

        // クリックできる範囲を決める(null なら画面全体)。設定パネルが開いている間はどこも押せない
        private void Clip(Rectangle? area)
        {
            if (!active) Gui.SetClip(NoClickArea);
            else if (area is Rectangle r) Gui.SetClip(r);
            else Gui.ResetClip();
        }

        private void DrawContent()
        {
            // ----- 上 -----
            FontManager.Draw("選曲", ListX, 50, 64, Color.White);
            DrawSearchBox();

            if (Gui.Button(new Rectangle(1440, 60, 190, 56), "一覧を更新"))
            {
                Refresh();
            }
            if (Gui.Button(new Rectangle(1650, 60, 190, 56), "フォルダを開く"))
            {
                OpenSongsFolder();
            }

            // 曲を読み込み中は、一覧のかわりに進み具合を出す
            if (SongList.IsScanning)
            {
                DrawScanning();
                DrawFooter();
                return;
            }

            List<SongCategory> categories = SongList.Categories;
            int categoryCount = categories.Count(c => !c.IsLoose); // カテゴリに入らない曲の入れ物は数えない
            string countText = $"{categoryCount}カテゴリ　{SongList.SongCount}曲";
            float countWidth = FontManager.Measure(countText, 26).X;
            FontManager.Draw(countText, 1410 - countWidth, 74, 26, Gui.SubTextColor);

            if (categories.Count == 0)
            {
                DrawEmpty();
                DrawFooter();
                return;
            }

            // 検索中は、結果の数を出す
            if (IsSearching)
            {
                string resultText = results.Count > 0
                    ? $"「{searchText}」の検索結果：{results.Count}曲"
                    : $"「{searchText}」に当てはまる曲はありません";
                FontManager.Draw(resultText, ListX, 128, 26, results.Count > 0 ? Color.White : ErrorColor);
            }

            // ----- 一覧 -----
            BuildRows();
            UpdateScroll();

            Raylib.BeginScissorMode((int)ListX, (int)ListTop, (int)(ListRight - ListX), (int)(ListBottom - ListTop));
            Clip(new Rectangle(ListX, ListTop, ListRight - ListX, ListBottom - ListTop));

            foreach (Row row in rows)
            {
                float y = ListTop + row.Y - scroll;
                if (y + row.Height < ListTop || y > ListBottom) continue; // 見えない行は描かない

                SongCategory category = categories[row.Cat];
                if (row.IsCategory) DrawCategoryRow(category, row, y);
                else if (row.IsSong) DrawSongRow(category, category.Songs[row.Song], row, y);
                else DrawCourseRow(category, category.Songs[row.Song], row, y);
            }

            Clip(null);
            Raylib.EndScissorMode();

            DrawFooter();
        }

        // 検索の箱
        private void DrawSearchBox()
        {
            // クリックで入力開始、ほかの場所をクリックで入力おわり
            if (active && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                bool inside = Gui.IsMouseOver(SearchBox);
                if (inside && !searchFocused) caretTimer = 0f;
                searchFocused = inside;
            }

            bool hover = Gui.IsMouseOver(SearchBox);
            Color back = searchFocused ? new Color(18, 22, 36, 255) : (hover ? Gui.BoxHoverColor : Gui.BoxColor);
            Raylib.DrawRectangleRec(SearchBox, back);
            if (searchFocused) Raylib.DrawRectangleLinesEx(SearchBox, 2, Gui.AccentColor);

            float textX = SearchBox.X + 20;
            float textY = SearchBox.Y + 13;

            if (searchText.Length > 0 || searchFocused)
            {
                // 入力中は「|」を点滅させる
                string caret = searchFocused && (caretTimer % 1f) < 0.5f ? "|" : "";
                FontManager.Draw(searchText + caret, textX, textY, 28, Color.White);
            }
            else
            {
                KeyboardKey searchKey = ConfigManager.Key.SystemKeys.GetValueOrDefault(SystemAction.Search, KeyboardKey.F3);
                FontManager.Draw($"曲名・アーティスト・フォルダ名で検索（{searchKey}）", textX, textY, 26, Gui.SubTextColor);
            }
        }

        private static bool IsCursor(Row row)
        {
            return row.Cat == cursorCat && row.Song == cursorSong && row.Course == cursorCourse;
        }

        // カテゴリの行
        private void DrawCategoryRow(SongCategory category, Row row, float y)
        {
            Rectangle area = new Rectangle(ListX, y, ListRight - ListX, row.Height);
            bool hover = Gui.IsMouseOver(area);

            // カテゴリの色を暗くしたものを背景に、左に元の色の帯
            Color back = Raylib.ColorBrightness(category.Color, hover ? -0.3f : -0.45f);
            Raylib.DrawRectangleRec(area, back);
            Raylib.DrawRectangle((int)area.X, (int)area.Y, 14, (int)area.Height, category.Color);
            if (IsCursor(row)) Raylib.DrawRectangleLinesEx(area, 3, CursorColor);

            bool open = row.Cat == openCat;
            FontManager.Draw(open ? "▼" : "▶", ListX + 34, y + 25, 22, Color.LightGray);
            FontManager.Draw(category.Title, ListX + 74, y + 14, 38, Color.White);

            string countText = $"{category.Songs.Count}曲";
            float countWidth = FontManager.Measure(countText, 26).X;
            FontManager.Draw(countText, ListRight - countWidth - 28, y + 22, 26, Color.LightGray);

            if (Gui.Clicked(area)) Decide(row.Cat, -1, -1);
        }

        // 曲の行(検索中は字下げなしで、右にカテゴリの名前を出す)
        private void DrawSongRow(SongCategory category, SongEntry song, Row row, float y)
        {
            float indent = IsSearching || category.IsLoose ? 0 : SongIndent; // 検索中とカテゴリに入らない曲は字下げなし
            Rectangle area = new Rectangle(ListX + indent, y, ListRight - ListX - indent, row.Height);
            bool hover = Gui.IsMouseOver(area);

            Raylib.DrawRectangleRec(area, hover ? RowHoverColor : RowColor);
            if (IsSearching) Raylib.DrawRectangle((int)area.X, (int)area.Y, 10, (int)area.Height, category.Color); // カテゴリの色
            if (IsCursor(row)) Raylib.DrawRectangleLinesEx(area, 3, CursorColor);

            bool open = IsSongOpen(row.Cat, row.Song);
            FontManager.Draw(open ? "▼" : "▶", area.X + 24, y + 20, 22, Gui.SubTextColor);
            FontManager.Draw(song.Title, area.X + 64, y + 12, 32, song.HasError ? ErrorColor : Color.White);

            // 右側：エラー → (検索中は)カテゴリ名 → サブタイトル
            string rightText = song.HasError ? song.Error
                : (IsSearching && !category.IsLoose ? $"{song.Subtitle}　｜　{category.Title}" : song.Subtitle);
            if (rightText.Length > 0)
            {
                float rightWidth = FontManager.Measure(rightText, 20).X;
                FontManager.Draw(rightText, ListRight - rightWidth - 24, y + 22, 20, song.HasError ? ErrorColor : Gui.SubTextColor);
            }

            if (Gui.Clicked(area)) Decide(row.Cat, row.Song, -1);
        }

        // 難易度の行
        private void DrawCourseRow(SongCategory category, SongEntry song, Row row, float y)
        {
            var (type, level) = song.Courses[row.Course];
            float indent = IsSearching || category.IsLoose ? CourseIndent - SongIndent : CourseIndent;
            Rectangle area = new Rectangle(ListX + indent, y, ListRight - ListX - indent, row.Height);
            bool hover = Gui.IsMouseOver(area);

            Raylib.DrawRectangleRec(area, hover ? RowHoverColor : new Color(24, 27, 38, 255));
            Raylib.DrawRectangle((int)area.X, (int)area.Y, 10, (int)area.Height, GetCourseColor(type)); // 左の色の帯
            if (IsCursor(row)) Raylib.DrawRectangleLinesEx(area, 3, CursorColor);

            FontManager.Draw(Chart.GetCourseName(type), area.X + 34, y + 9, 30, Color.White);
            FontManager.Draw($"★{level}", area.X + 300, y + 9, 30, CursorColor);

            if (Gui.Clicked(area)) Decide(row.Cat, row.Song, row.Course);
        }

        // 難易度の色(本家っぽく)
        private static Color GetCourseColor(CourseType type) => type switch
        {
            CourseType.Easy => new Color(240, 120, 60, 255),
            CourseType.Normal => new Color(110, 190, 70, 255),
            CourseType.Hard => new Color(80, 150, 230, 255),
            CourseType.Oni => new Color(230, 70, 140, 255),
            CourseType.Edit => new Color(150, 90, 220, 255),
            CourseType.Tower => new Color(200, 170, 60, 255),
            _ => new Color(200, 200, 200, 255),
        };

        // 曲を読み込み中(「読み込んだ数 / 曲ファイルの総数」とバー)
        private static void DrawScanning()
        {
            int total = SongList.ScanTotal;
            int done = Math.Min(SongList.ScanDone, Math.Max(total, 0));

            string text = total < 0 ? "曲ファイルを探しています…" : $"曲を読み込み中…　{done} / {total}";
            FontManager.Draw(text, ListX, 260, 40, Color.White);

            float progress = total > 0 ? (float)done / total : 0f;
            Rectangle bar = new Rectangle(ListX, 330, ListRight - ListX, 20);
            Raylib.DrawRectangleRec(bar, RowColor);
            Raylib.DrawRectangleRec(new Rectangle(bar.X, bar.Y, bar.Width * progress, bar.Height), CursorColor);
        }

        // 曲が1つも無いとき
        private static void DrawEmpty()
        {
            FontManager.Draw("曲がありません", ListX, 260, 40, ErrorColor);
            FontManager.Draw("下のフォルダに、曲のフォルダ（.tja と .ogg が入ったもの）を入れてください。", ListX, 330, 28, Color.White);
            FontManager.Draw(SongList.SongsFolder, ListX, 380, 24, Gui.SubTextColor);
            FontManager.Draw("カテゴリ分けしたいときは、box.def の入ったフォルダの中に入れてください。", ListX, 430, 28, Color.White);
            FontManager.Draw("入れたら［一覧を更新］を押してください。", ListX, 480, 28, Color.White);
        }

        // 下の操作説明
        private void DrawFooter()
        {
            KeyboardKey searchKey = ConfigManager.Key.SystemKeys.GetValueOrDefault(SystemAction.Search, KeyboardKey.F3);

            string hint;
            if (SongList.IsScanning) hint = "曲を読み込み中…　Esc：ホームへ（読み込みは続きます）";
            else if (searchFocused) hint = "文字を入れて検索　↑↓：選ぶ　Enter：入力をおわる　Esc：検索をやめる";
            else if (cursorCourse >= 0) hint = "↑↓：選ぶ　Enter：演奏する　←・Esc：閉じる";
            else if (IsSearching) hint = $"↑↓：選ぶ　Enter・→：開く　←：閉じる　Esc：検索をやめる　{searchKey}：検索の箱へ";
            else hint = $"↑↓：選ぶ　Enter・→：開く　←・Esc：閉じる（何も開いていなければホームへ）　{searchKey}：検索";

            FontManager.Draw(hint, ListX, 1020, 26, Gui.SubTextColor);
        }

        // ===== スクロール =====

        private void UpdateScroll()
        {
            float listHeight = ListBottom - ListTop;
            float contentHeight = rows.Count > 0 ? rows[^1].Y + rows[^1].Height : 0;
            float maxScroll = Math.Max(0, contentHeight - listHeight);

            // ホイール(設定パネルが開いている間は動かさない)
            float wheel = active ? Raylib.GetMouseWheelMove() : 0f;
            if (wheel != 0) scrollTarget -= wheel * 120f;

            // キーで動かしたときは、選んでいる行が見えるようにする
            if (active && rows.Count > 0 && WasKeyMoved())
            {
                Row row = rows[FindCursorRow()];
                if (row.Y < scrollTarget) scrollTarget = row.Y - 10;
                if (row.Y + row.Height > scrollTarget + listHeight) scrollTarget = row.Y + row.Height - listHeight + 10;
            }

            scrollTarget = Math.Clamp(scrollTarget, 0, maxScroll);
            scroll += (scrollTarget - scroll) * Math.Min(1f, Raylib.GetFrameTime() * 15f);
            if (Math.Abs(scrollTarget - scroll) < 0.5f) scroll = scrollTarget;
        }

        // このフレームでカーソルがキーで動いたか
        private static bool WasKeyMoved()
        {
            return KeyPressed(KeyboardKey.Up) || KeyPressed(KeyboardKey.Down)
                || Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter)
                || Raylib.IsKeyPressed(KeyboardKey.Right) || Raylib.IsKeyPressed(KeyboardKey.Left)
                || Raylib.IsKeyPressed(KeyboardKey.Escape);
        }

        // Songs フォルダをエクスプローラーで開く
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
    }
}