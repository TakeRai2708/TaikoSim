using System.Numerics;
using System.Text;
using Raylib_cs;
using TaikoSim.Audio;
using TaikoSim.Charts;
using TaikoSim.Configs;
using TaikoSim.Graphics;
using TaikoSim.Input;
using TaikoSim.UI;

namespace TaikoSim.Scenes
{
    // 選曲画面(演奏ゲーム)。TaikøNauts / OpenTaiko の流れに合わせた
    //   1. ジャンルをえらぶ(最近遊んだ曲・最近追加された曲・お気に入り・カテゴリ)
    //   2. 曲をえらぶ(7曲ごとに「とじる」、最後に「ランダム」。OpenTaiko と同じ並び)
    //   3. 難易度をえらぶ(左から 戻る・オプション・かんたん・ふつう・むずかしい・おに)
    //      おにを選んでいるときに、もう一度右(カッ・→)で裏おにに切り替え(OpenTaiko と同じ)。Tab でも切り替え(TaikøNauts と同じ)
    // 操作：カッ(左縁・右縁)・↑↓←→・ホイールで動かす、ドン・Enter・クリックで決定、Esc で1つ戻る
    //       F3：検索、F4：お気に入りに入れる/外す、F2：オプション(難易度をえらんでいるとき)
    // 選んでいた場所は static に覚えておくので、演奏から戻ってきても同じ曲を選んだまま
    public class SongSelectScene : Scene
    {
        // ===== 一覧の項目 =====

        private enum Special { None, Recent, Added, Favorites, Search }
        private enum Kind { Genre, Label, Back, Song, Random }

        private sealed class Item
        {
            public Kind Kind;
            public string Title = "";
            public Color Color;
            public int Cat = -1;               // ジャンル：カテゴリの番号
            public Special Special;            // ジャンル：特別な項目のとき
            public int SongCount;              // ジャンル：曲の数
            public SongEntry? Song;            // 曲
            public string CategoryTitle = "";  // 曲：入っているカテゴリの名前
        }

        // 「最近追加された曲」に出す数
        private const int AddedMax = 30;
        // 「とじる」を入れる間隔(OpenTaiko と同じ7曲ごと)
        private const int BackInterval = 7;

        // ===== 覚えておく場所(static：演奏から戻ってきても同じ) =====
        private static bool inSongs;                   // false：ジャンルをえらぶ、true：曲をえらぶ
        private static int genrePos;                   // ジャンルのカーソル(一覧の数で割った余りが項目の番号。ぐるっと回れる)
        private static int songPos;                    // 曲のカーソル
        private static Special openSpecial = Special.None;
        private static int openCat = -1;
        private static string lastSongKey = "";        // 最後に選んでいた曲(一覧を作り直しても同じ曲に戻すため)
        private static CourseType lastCourse = CourseType.Oni;
        private static string searchText = "";
        private static List<SongCategory>? builtFor;   // 今の一覧を作ったときのカテゴリ(読み直したら作り直す)

        private readonly List<Item> genres = new();
        private readonly List<Item> songs = new();
        private float shown;                           // 表示上のカーソルの位置(だんだん近づける)
        private Color backColor = new Color(30, 40, 70, 255);

        // ===== 難易度をえらぶ =====
        private static readonly CourseType[] PanelCourses = { CourseType.Easy, CourseType.Normal, CourseType.Hard, CourseType.Oni };
        private bool diffOpen;
        private int diffCursor;                        // 0 戻る、1 オプション、2〜5 かんたん〜おに
        private bool ura;
        private SongEntry? diffSong;
        private float diffAnim;                        // 開くアニメ(0→1)
        private readonly PlayOptionsPanel options = new();

        // ===== 検索 =====
        private bool searchFocused;
        private string searchBefore = "";              // 検索の箱を開く前の文字(Esc でやめたら戻す)
        private float caretTimer;
        private static readonly Rectangle SearchBox = new Rectangle(560, 40, 800, 56);
        private const float SearchTextX = 560 + 20, SearchTextY = 40 + 13, SearchFontSize = 28;
        private const int SearchMaxLength = 40;
        private static readonly Color CompositionColor = new Color(255, 230, 150, 255);

        // ===== そのほか =====
        private readonly SongPreview preview = new();
        private string toast = "";
        private float toastTimer;
        private bool wasScanning;
        private bool active = true;
        private static readonly Rectangle NoClickArea = new Rectangle(0, 0, 0, 0);
        private readonly Random random = new();

        // 配置
        private const float CenterX = 960, CenterY = 560;
        private const float BarWidth = 820, BarHeight = 58, Gap = 10;
        private const float GenreSelWidth = 920, GenreSelHeight = 150;
        private const float SongSelWidth = 980, SongSelHeight = 214;

        private static readonly Color BackBoxColor = new Color(81, 48, 9, 255);   // 「とじる」(OpenTaiko の #513009)
        private static readonly Color RandomColor = new Color(110, 80, 160, 255);
        private static readonly Color LabelColor = new Color(20, 24, 36, 220);
        private static readonly Color CursorColor = new Color(255, 210, 60, 255);
        private static readonly Color ErrorColor = new Color(255, 110, 100, 255);

        // ===== 入る・出る =====

        public override void Enter()
        {
            if (!SongList.IsScanned && !SongList.IsScanning) SongList.StartScan();
            wasScanning = SongList.IsScanning;
            searchFocused = false;
            if (!SongList.IsScanning) Rebuild(keepPlace: true);
            shown = inSongs ? songPos : genrePos;
        }

        public override void Exit()
        {
            preview.Dispose();
            options.Close();
        }

        // ===== 一覧を作る =====

        // 曲の場所(Songs から見た .tja の場所) → 曲
        private readonly Dictionary<string, (SongEntry Entry, SongCategory Category)> byKey = new(StringComparer.OrdinalIgnoreCase);

        // 一覧を作り直す。keepPlace なら、開いていたジャンルと選んでいた曲をできるだけそのままにする
        private void Rebuild(bool keepPlace)
        {
            List<SongCategory> categories = SongList.Categories;
            bool changed = !ReferenceEquals(categories, builtFor);
            builtFor = categories;

            byKey.Clear();
            foreach (SongCategory category in categories)
                foreach (SongEntry song in category.Songs)
                    byKey[song.Key] = (song, category);

            BuildGenres();
            if (changed && !keepPlace) { inSongs = false; genrePos = FirstSelectable(genres); }

            // 開いていたジャンルが無くなっていたら、ジャンルの一覧へ
            if (inSongs && (openSpecial == Special.None && (openCat < 0 || openCat >= categories.Count)))
                inSongs = false;
            if (inSongs)
            {
                BuildSongs();
                if (songs.Count <= 1) inSongs = false; // 「とじる」しか無い
                else SelectSong(lastSongKey);
            }
            if (genres.Count > 0 && genres[Mod(genrePos, genres.Count)].Kind == Kind.Label) genrePos = FirstSelectable(genres);
        }

        private void BuildGenres()
        {
            genres.Clear();
            List<SongCategory> categories = SongList.Categories;

            genres.Add(new Item { Kind = Kind.Genre, Special = Special.Recent, Title = "最近遊んだ曲", Color = new Color(70, 110, 170, 255), SongCount = Recent().Count });
            genres.Add(new Item { Kind = Kind.Genre, Special = Special.Added, Title = "最近追加された曲", Color = new Color(60, 140, 150, 255), SongCount = Added().Count });
            genres.Add(new Item { Kind = Kind.Genre, Special = Special.Favorites, Title = "お気に入り", Color = new Color(200, 110, 150, 255), SongCount = Favorites().Count });
            genres.Add(new Item { Kind = Kind.Label, Title = "カテゴリをえらぶ", Color = LabelColor });

            for (int i = 0; i < categories.Count; i++)
            {
                genres.Add(new Item { Kind = Kind.Genre, Cat = i, Title = categories[i].Title, Color = categories[i].Color, SongCount = categories[i].Songs.Count });
            }
        }

        // 開いているジャンルの曲を並べる(7曲ごとに「とじる」、最後に「ランダム」。OpenTaiko の tReinsertBackButtons と同じ)
        private void BuildSongs()
        {
            songs.Clear();
            List<(SongEntry Entry, SongCategory Category)> list = openSpecial switch
            {
                Special.Recent => Recent(),
                Special.Added => Added(),
                Special.Favorites => Favorites(),
                Special.Search => SearchResults(),
                _ => openCat >= 0 && openCat < SongList.Categories.Count
                    ? SongList.Categories[openCat].Songs.Select(s => (s, SongList.Categories[openCat])).ToList()
                    : new(),
            };

            foreach (var (entry, category) in list)
            {
                songs.Add(new Item { Kind = Kind.Song, Song = entry, Title = entry.Title, Color = category.Color, CategoryTitle = category.Title });
            }
            int count = list.Count;
            for (int index = 0; index < count / BackInterval + 1; index++)
            {
                songs.Insert(Math.Min(index * (BackInterval + 1), songs.Count), new Item { Kind = Kind.Back, Title = "とじる", Color = BackBoxColor });
            }
            if (count > 0) songs.Add(new Item { Kind = Kind.Random, Title = "ランダムにえらぶ", Color = RandomColor });
        }

        private List<(SongEntry, SongCategory)> Recent() => Resolve(SongHistory.Recent);
        private List<(SongEntry, SongCategory)> Favorites() => Resolve(SongHistory.Favorites);

        private List<(SongEntry, SongCategory)> Resolve(IEnumerable<string> keys)
        {
            List<(SongEntry, SongCategory)> list = new();
            foreach (string key in keys)
                if (byKey.TryGetValue(key, out var found) && !found.Entry.HasError) list.Add(found);
            return list;
        }

        private List<(SongEntry, SongCategory)> Added()
        {
            return byKey.Values.Where(v => !v.Entry.HasError && v.Entry.WriteTime > 0)
                .OrderByDescending(v => v.Entry.WriteTime).Take(AddedMax)
                .Select(v => (v.Entry, v.Category)).ToList();
        }

        private List<(SongEntry, SongCategory)> SearchResults()
        {
            string query = SongSearch.Normalize(searchText);
            List<(SongEntry, SongCategory)> list = new();
            if (query.Length == 0) return list;
            foreach (SongCategory category in SongList.Categories)
                foreach (SongEntry song in category.Songs)
                    if (SongSearch.Matches(song.SearchKey, query)) list.Add((song, category));
            return list;
        }

        // 曲の一覧で、key の曲を選ぶ(無ければ最初の曲)
        private void SelectSong(string key)
        {
            int index = songs.FindIndex(s => s.Kind == Kind.Song && string.Equals(s.Song!.Key, key, StringComparison.OrdinalIgnoreCase));
            if (index < 0) index = songs.Count > 1 ? 1 : 0;
            songPos = index;
            shown = index;
        }

        private static int FirstSelectable(List<Item> items)
        {
            int index = items.FindIndex(i => i.Kind != Kind.Label);
            return Math.Max(0, index);
        }

        private static int Mod(int value, int count) => count == 0 ? 0 : ((value % count) + count) % count;

        private List<Item> CurrentList => inSongs ? songs : genres;
        private int CurrentPos => inSongs ? songPos : genrePos;
        private Item? CurrentItem => CurrentList.Count == 0 ? null : CurrentList[Mod(CurrentPos, CurrentList.Count)];

        // ===== 更新 =====

        public override void Update(float dt)
        {
            caretTimer += dt;
            if (toastTimer > 0f) toastTimer -= dt;
            PreloadListText();
            preview.Update(dt);
            options.Update(dt);

            // 読み込みが終わったら一覧を作り直す
            bool scanning = SongList.IsScanning;
            if (wasScanning && !scanning) Rebuild(keepPlace: false);
            else if (!scanning && !ReferenceEquals(SongList.Categories, builtFor)) Rebuild(keepPlace: false);
            wasScanning = scanning;

            shown += (CurrentPos - shown) * Math.Min(1f, dt * 16f);
            if (Math.Abs(CurrentPos - shown) < 0.001f) shown = CurrentPos;
            diffAnim = diffOpen ? Math.Min(1f, diffAnim + dt * 6f) : 0f;

            // 背景の色を、選んでいるものの色にだんだん近づける
            Color target = CurrentItem?.Color ?? new Color(30, 40, 70, 255);
            if (diffOpen && diffSong != null && CurrentItem is Item item) target = item.Color;
            float k = Math.Min(1f, dt * 6f);
            backColor = new Color((byte)(backColor.R + (target.R - backColor.R) * k), (byte)(backColor.G + (target.G - backColor.G) * k),
                (byte)(backColor.B + (target.B - backColor.B) * k), (byte)255);

            if (scanning)
            {
                DiscardTypedChars();
                preview.Request(null);
                return;
            }

            // 検索の箱に入力中
            if (searchFocused)
            {
                UpdateSearchInput();
                return;
            }
            if (options.IsOpen)
            {
                // オプションの板が開いている間は、F2 で閉じるだけ
                if (InputManager.IsSystemPressed(SystemAction.QuickOption) && !options.IsTyping) options.Close();
                return;
            }
            DiscardTypedChars();

            // 曲を選んでいるときはプレビューを流す
            preview.Request(inSongs && CurrentItem is { Kind: Kind.Song } now ? now.Song!.TjaPath : null);

            if (diffOpen)
            {
                UpdateDifficulty();
                return;
            }

            // F3：検索
            if (InputManager.IsSystemPressed(SystemAction.Search))
            {
                searchFocused = true;
                searchBefore = searchText;
                searchText = "";
                caretTimer = 0f;
                return;
            }

            // F4：お気に入り
            if (inSongs && InputManager.IsSystemPressed(SystemAction.Favorite) && CurrentItem is { Kind: Kind.Song } fav)
                ToggleFavorite(fav.Song!);

            // 動かす：カッ(左縁)・↑・ホイール上 で上、カッ(右縁)・↓・ホイール下 で下
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

        private void Move(int direction)
        {
            List<Item> list = CurrentList;
            if (list.Count == 0) return;
            int pos = CurrentPos;
            for (int guard = 0; guard < list.Count; guard++)
            {
                pos += Math.Sign(direction);
                if (list[Mod(pos, list.Count)].Kind != Kind.Label) break;
            }
            if (inSongs) songPos = pos; else genrePos = pos;
            if (inSongs && list[Mod(pos, list.Count)] is { Kind: Kind.Song } s) lastSongKey = s.Song!.Key;
            MenuSounds.Ka();
        }

        // 決定
        private void Decide()
        {
            if (CurrentItem is not Item item) return;
            switch (item.Kind)
            {
                case Kind.Genre:
                    if (item.SongCount == 0)
                    {
                        MenuSounds.Ka();
                        ShowToast(item.Special == Special.Favorites ? "お気に入りはまだありません（曲をえらんで F4 で入れられます）" : "曲がありません");
                        return;
                    }
                    MenuSounds.Don();
                    OpenGenre(item.Special, item.Cat);
                    break;

                case Kind.Back:
                    MenuSounds.Don();
                    CloseGenre();
                    break;

                case Kind.Random:
                    List<Item> candidates = songs.Where(s => s.Kind == Kind.Song && !s.Song!.HasError).ToList();
                    if (candidates.Count == 0) return;
                    Item pick = candidates[random.Next(candidates.Count)];
                    songPos = songPos - Mod(songPos, songs.Count) + songs.IndexOf(pick);
                    lastSongKey = pick.Song!.Key;
                    MenuSounds.Don();
                    OpenDifficulty(pick.Song!);
                    break;

                case Kind.Song:
                    if (item.Song!.HasError || item.Song.Courses.Count == 0)
                    {
                        MenuSounds.Ka();
                        ShowToast(item.Song.HasError ? item.Song.Error : "譜面がありません");
                        return;
                    }
                    MenuSounds.Don();
                    OpenDifficulty(item.Song);
                    break;
            }
        }

        private void OpenGenre(Special special, int cat)
        {
            openSpecial = special;
            openCat = cat;
            inSongs = true;
            BuildSongs();
            songPos = songs.Count > 1 ? 1 : 0; // 最初の「とじる」の次(最初の曲)
            if (songs[Mod(songPos, songs.Count)] is { Kind: Kind.Song } first) lastSongKey = first.Song!.Key;
            shown = songPos;
        }

        private void CloseGenre()
        {
            inSongs = false;
            preview.Request(null);
            if (openSpecial == Special.Search)
            {
                searchText = "";
                openSpecial = Special.None;
            }
            BuildGenres(); // 最近遊んだ曲・お気に入りの数が変わっているかもしれない
            shown = genrePos;
        }

        // Esc：オプション → 難易度 → 曲の一覧 → ジャンルの一覧 → モード選択 の順に1つ戻る
        public override void OnBack()
        {
            if (searchFocused)
            {
                searchFocused = false;
                searchText = searchBefore; // 前の検索の結果を見ていたときは、その文字に戻す
                return;
            }
            if (options.IsOpen) { options.Close(); return; }
            if (diffOpen) { CloseDifficulty(); return; }
            if (inSongs) { MenuSounds.Ka(); CloseGenre(); return; }
            SceneManager.ChangeScene(new ModeSelectScene());
        }

        // .tja がドロップされたら、テスト用に譜面を流す
        public override void OnFilesDropped(string[] paths)
        {
            string? tja = paths.FirstOrDefault(PlayScene.IsTja);
            if (tja != null) SceneManager.ChangeScene(new PlayScene(tja));
        }

        private void ToggleFavorite(SongEntry song)
        {
            bool added = SongHistory.ToggleFavorite(song.Key);
            ShowToast(added ? $"お気に入りに入れました：{song.Title}" : $"お気に入りから外しました：{song.Title}");
            MenuSounds.Don();
            if (openSpecial == Special.Favorites && !added)
            {
                // お気に入りの一覧から消したときは、並びを作り直す
                int index = Mod(songPos, songs.Count);
                BuildSongs();
                if (songs.Count <= 1) { CloseGenre(); return; }
                songPos = Math.Min(index, songs.Count - 1);
                shown = songPos;
            }
        }

        private void ShowToast(string text)
        {
            toast = text;
            toastTimer = 2.5f;
        }

        private static bool KeyPressed(KeyboardKey key) => Raylib.IsKeyPressed(key) || Raylib.IsKeyPressedRepeat(key);

        // ===== 難易度をえらぶ =====

        private bool HasCourse(CourseType type) => diffSong != null && diffSong.Courses.Any(c => c.Type == type);
        private int LevelOf(CourseType type) => diffSong?.Courses.FirstOrDefault(c => c.Type == type).Level ?? 0;

        // 2〜5 番目の箱の難易度(おにの箱は、裏のときは裏)
        private CourseType CourseAt(int index) => index == 5 && ura ? CourseType.Edit : PanelCourses[index - 2];

        private void OpenDifficulty(SongEntry song)
        {
            diffSong = song;
            diffOpen = true;
            diffAnim = 0f;
            bool hasOni = HasCourse(CourseType.Oni), hasUra = HasCourse(CourseType.Edit);
            // おにが無くて裏だけある曲は、最初から裏(OpenTaiko と同じ)
            ura = hasUra && (!hasOni || lastCourse == CourseType.Edit);

            // 前に遊んだ難易度があればそこ、無ければ一番むずかしい難易度
            CourseType start = lastCourse == CourseType.Edit ? CourseType.Oni : lastCourse;
            if (!HasCourse(start) && !(start == CourseType.Oni && hasUra))
            {
                start = PanelCourses.LastOrDefault(c => HasCourse(c) || (c == CourseType.Oni && hasUra), CourseType.Easy);
            }
            diffCursor = 2 + Array.IndexOf(PanelCourses, start);
        }

        private void CloseDifficulty()
        {
            MenuSounds.Ka();
            options.Close();
            diffOpen = false;
            diffSong = null;
        }

        private void UpdateDifficulty()
        {
            // F2：オプション
            if (InputManager.IsSystemPressed(SystemAction.QuickOption))
            {
                MenuSounds.Don();
                options.Open();
                return;
            }

            bool right = InputManager.IsDrumPressed(DrumPart.RightKa) || KeyPressed(KeyboardKey.Right);
            bool left = InputManager.IsDrumPressed(DrumPart.LeftKa) || KeyPressed(KeyboardKey.Left);
            if (right) DiffRight();
            else if (left && diffCursor > 0)
            {
                diffCursor--;
                MenuSounds.Ka();
            }

            // Tab：おにを選んでいるときに裏おにへ(TaikøNauts と同じ)
            if (Raylib.IsKeyPressed(KeyboardKey.Tab) && diffCursor == 5) ToggleUra();

            if (InputManager.IsDonPressed() || Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter))
                DecideDifficulty();
        }

        // 右へ。おにの上でさらに右なら裏に切り替え(OpenTaiko の t次に移動 と同じ)
        private void DiffRight()
        {
            if (diffCursor < 5)
            {
                diffCursor++;
                MenuSounds.Ka();
                return;
            }
            ToggleUra();
        }

        private void ToggleUra()
        {
            // おにと裏の両方があるときだけ切り替えられる
            if (!HasCourse(CourseType.Oni) || !HasCourse(CourseType.Edit)) return;
            ura = !ura;
            MenuSounds.Don();
        }

        private void DecideDifficulty()
        {
            if (diffCursor == 0) { CloseDifficulty(); return; }
            if (diffCursor == 1)
            {
                MenuSounds.Don();
                options.Open();
                return;
            }

            CourseType type = CourseAt(diffCursor);
            if (!HasCourse(type) || diffSong == null)
            {
                MenuSounds.Ka();
                return;
            }

            MenuSounds.Don();
            lastCourse = type;
            lastSongKey = diffSong.Key;
            SongHistory.AddPlayed(diffSong.Key);
            preview.Dispose();
            SceneManager.ChangeScene(new PlayScene(diffSong.TjaPath, type, returnToSelect: true));
        }

        // ===== 検索 =====

        private void UpdateSearchInput()
        {
            WindowsIme.RequestTextInput(SearchBox,
                new Vector2(SearchTextX + FontManager.Measure(searchText, SearchFontSize).X, SearchTextY));

            int c = Raylib.GetCharPressed();
            while (c > 0)
            {
                AddSearchChar(c);
                c = Raylib.GetCharPressed();
            }
            foreach (Rune rune in WindowsIme.TakeResultText().EnumerateRunes()) AddSearchChar(rune.Value);

            if (KeyPressed(KeyboardKey.Backspace) && searchText.Length > 0)
            {
                int remove = searchText.Length >= 2 && char.IsLowSurrogate(searchText[^1]) ? 2 : 1;
                searchText = searchText[..^remove];
            }

            // Enter：検索の結果を開く
            if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter))
            {
                if (searchText.Length == 0) { searchFocused = false; return; }
                int count = SearchResults().Count;
                if (count == 0)
                {
                    ShowToast($"「{searchText}」に当てはまる曲はありません");
                    return;
                }
                searchFocused = false;
                options.Close();
                diffOpen = false;
                MenuSounds.Don();
                OpenGenre(Special.Search, -1);
            }
        }

        private static void AddSearchChar(int c)
        {
            bool usable = c >= 32 && c <= 0x10FFFF && (c < 0xD800 || c > 0xDFFF);
            if (usable && searchText.Length < SearchMaxLength) searchText += char.ConvertFromUtf32(c);
        }

        private static void DiscardTypedChars()
        {
            while (Raylib.GetCharPressed() > 0) { }
            WindowsIme.TakeResultText();
        }

        // 曲名などの文字をまとめて読み込んだ一覧(一覧が新しくなったら、もう一度読む)
        private static List<SongCategory>? preloadedCategories;

        // 一覧に出る文字(曲名・サブタイトル・エラー・カテゴリ名)を、まとめて先に読み込む
        //   スクロールで新しい曲名が一度にたくさん出ても、その場で文字を読み込まなくていいように
        private static void PreloadListText()
        {
            List<SongCategory> categories = SongList.Categories;
            if (SongList.IsScanning || ReferenceEquals(categories, preloadedCategories)) return;
            preloadedCategories = categories;

            IEnumerable<string> Texts()
            {
                yield return "最近遊んだ曲最近追加された曲お気に入りカテゴリをえらぶとじるランダムにえらぶ";
                foreach (SongCategory category in categories)
                {
                    yield return category.Title;
                    foreach (SongEntry song in category.Songs)
                    {
                        yield return song.Title;
                        yield return song.Subtitle;
                        yield return song.Error;
                    }
                }
            }
            FontManager.Preload(Texts());
        }

        // ===== 描画 =====

        public override void Draw()
        {
            Gui.BeginFrame();
            active = !Game.IsSettingsOpen;
            if (!active || options.IsOpen || searchFocused) Gui.SetClip(NoClickArea);
            else Gui.ResetClip();

            Raylib.DrawRectangleGradientV(0, 0, Screen.Width, Screen.Height,
                Raylib.ColorBrightness(backColor, -0.55f), Raylib.ColorBrightness(backColor, -0.82f));

            DrawHeader();

            if (SongList.IsScanning) DrawScanning();
            else if (SongList.Categories.Count == 0) DrawEmpty();
            else DrawList();

            if (diffOpen) DrawDifficulty();

            Gui.ResetClip();
            if (!active) Gui.SetClip(NoClickArea);
            options.Draw(active);
            Gui.ResetClip();

            DrawFooter();
            if (toastTimer > 0f) DrawToast();
        }

        private void DrawHeader()
        {
            FontManager.Draw("演奏ゲーム", 60, 36, 44, Color.White);
            string where = inSongs ? (openSpecial == Special.Search ? $"検索：{searchText}" : CurrentGenreTitle()) : "ジャンルをえらぶ";
            FontManager.Draw(where, 64, 92, 28, new Color(230, 230, 240, 255));

            if (searchFocused || searchText.Length > 0 && openSpecial == Special.Search) DrawSearchBox();
            else
            {
                KeyboardKey searchKey = ConfigManager.Key.SystemKeys.GetValueOrDefault(SystemAction.Search, KeyboardKey.F3);
                FontManager.Draw($"{searchKey}：検索", 1700, 50, 24, Gui.SubTextColor);
            }
            string count = $"{SongList.SongCount}曲";
            FontManager.Draw(count, 1860 - FontManager.Measure(count, 24).X, 84, 24, Gui.SubTextColor);
        }

        private string CurrentGenreTitle()
        {
            return openSpecial switch
            {
                Special.Recent => "最近遊んだ曲",
                Special.Added => "最近追加された曲",
                Special.Favorites => "お気に入り",
                _ => openCat >= 0 && openCat < SongList.Categories.Count ? SongList.Categories[openCat].Title : "",
            };
        }

        // 一覧(選んでいるものを真ん中に大きく。上下はぐるっとつながっている)
        private void DrawList()
        {
            List<Item> list = CurrentList;
            if (list.Count == 0) return;

            float selWidth = inSongs ? SongSelWidth : GenreSelWidth;
            float selHeight = inSongs ? SongSelHeight : GenreSelHeight;
            int baseIndex = (int)MathF.Round(shown);

            // 上下に何個まで描くか(一覧が短いときは、同じ項目が2回出ないようにする)
            int before = Math.Min(7, (list.Count - 1) / 2);
            int after = Math.Min(7, list.Count - 1 - before);

            for (int k = -before; k <= after; k++)
            {
                int pos = baseIndex + k;
                Item item = list[Mod(pos, list.Count)];
                float r = pos - shown;
                float grow = Math.Clamp(1f - Math.Abs(r), 0f, 1f);
                float width = BarWidth + (selWidth - BarWidth) * grow;
                float height = BarHeight + (selHeight - BarHeight) * grow;
                float offset = r * (BarHeight + Gap) + Math.Sign(r) * Math.Min(Math.Abs(r), 1f) * (selHeight - BarHeight) / 2f;
                float y = CenterY + offset - height / 2f;
                if (y > 1000 || y + height < 130) continue;

                Rectangle area = new Rectangle(CenterX - width / 2f, y, width, height);
                bool selected = pos == CurrentPos;
                DrawBar(item, area, grow, selected);

                // クリック：選んでいる項目なら決定、ほかなら選ぶ
                if (!diffOpen && item.Kind != Kind.Label && Gui.Clicked(area))
                {
                    if (selected) Decide();
                    else
                    {
                        if (inSongs) songPos = pos; else genrePos = pos;
                        if (item.Kind == Kind.Song) lastSongKey = item.Song!.Key;
                        MenuSounds.Ka();
                    }
                }
            }
        }

        private void DrawBar(Item item, Rectangle area, float grow, bool selected)
        {
            if (item.Kind == Kind.Label)
            {
                Raylib.DrawRectangleRounded(area, 0.4f, 10, LabelColor);
                DrawCenteredFit(item.Title, area, 28, new Color(200, 205, 220, 255));
                return;
            }

            if (selected) Raylib.DrawRectangleRounded(new Rectangle(area.X - 6, area.Y - 6, area.Width + 12, area.Height + 12), 0.2f, 12, CursorColor);
            Raylib.DrawRectangleRounded(area, 0.2f, 12, Raylib.ColorBrightness(item.Color, -0.5f));
            Rectangle inner = new Rectangle(area.X + 5, area.Y + 5, area.Width - 10, area.Height - 10);
            Raylib.DrawRectangleRounded(inner, 0.2f, 12, item.Color);

            bool big = grow > 0.5f;
            switch (item.Kind)
            {
                case Kind.Genre:
                    if (big)
                    {
                        DrawCenteredFit(item.Title, new Rectangle(inner.X, inner.Y + 10, inner.Width, inner.Height * 0.6f), 54, Color.White);
                        string count = item.SongCount > 0 ? $"{item.SongCount}曲" : "曲がありません";
                        DrawCenteredFit(count, new Rectangle(inner.X, inner.Y + inner.Height * 0.62f, inner.Width, inner.Height * 0.3f), 26, new Color(235, 235, 240, 255));
                    }
                    else DrawCenteredFit(item.Title, inner, 30, Color.White);
                    break;

                case Kind.Back:
                case Kind.Random:
                    DrawCenteredFit(item.Title, inner, big ? 44 : 30, Color.White);
                    break;

                case Kind.Song:
                    DrawSongBar(item, inner, big);
                    break;
            }
        }

        private void DrawSongBar(Item item, Rectangle inner, bool big)
        {
            SongEntry song = item.Song!;
            bool favorite = SongHistory.IsFavorite(song.Key);
            Color titleColor = song.HasError ? ErrorColor : Color.White;

            if (!big)
            {
                DrawCenteredFit(song.Title, new Rectangle(inner.X + 40, inner.Y, inner.Width - 80, inner.Height), 28, titleColor);
                if (favorite) FontManager.Draw("♥", inner.X + inner.Width - 36, inner.Y + inner.Height / 2f - 14, 26, new Color(255, 150, 190, 255));
                return;
            }

            // 上：カテゴリ名(特別な一覧のときだけ)とお気に入り
            if (openSpecial != Special.None) FontManager.Draw(item.CategoryTitle, inner.X + 20, inner.Y + 10, 20, new Color(235, 235, 240, 220));
            if (favorite) FontManager.Draw("♥ お気に入り", inner.X + inner.Width - 150, inner.Y + 10, 20, new Color(255, 190, 215, 255));

            // 曲名・サブタイトル
            DrawCenteredFit(song.Title, new Rectangle(inner.X + 20, inner.Y + 34, inner.Width - 40, 52), 44, titleColor);
            string sub = song.HasError ? song.Error : song.Subtitle;
            if (sub.Length > 0)
                DrawCenteredFit(sub, new Rectangle(inner.X + 20, inner.Y + 90, inner.Width - 40, 30), 22, song.HasError ? ErrorColor : new Color(235, 235, 240, 255));

            // 下：難易度と★
            DrawCourseChips(song, new Rectangle(inner.X + 20, inner.Y + inner.Height - 62, inner.Width - 40, 50));
        }

        // 難易度の小さい札を、真ん中にそろえて並べる
        private static void DrawCourseChips(SongEntry song, Rectangle area)
        {
            List<(CourseType Type, int Level)> courses = song.Courses
                .Where(c => c.Type <= CourseType.Edit).OrderBy(c => c.Type).ToList();
            if (courses.Count == 0) return;

            const float chipWidth = 150, gap = 10;
            float total = courses.Count * chipWidth + (courses.Count - 1) * gap;
            float x = area.X + (area.Width - total) / 2f;
            foreach (var (type, level) in courses)
            {
                Rectangle chip = new Rectangle(x, area.Y, chipWidth, area.Height);
                Raylib.DrawRectangleRounded(chip, 0.35f, 8, Raylib.ColorBrightness(CourseColor(type), -0.55f));
                Raylib.DrawRectangleRounded(new Rectangle(chip.X + 3, chip.Y + 3, chip.Width - 6, chip.Height - 6), 0.35f, 8, CourseColor(type));
                string name = type == CourseType.Edit ? "裏" : Chart.GetCourseName(type);
                DrawCenteredFit($"{name} ★{level}", chip, 22, Color.White);
                x += chipWidth + gap;
            }
        }

        // 難易度の色
        private static Color CourseColor(CourseType type) => type switch
        {
            CourseType.Easy => new Color(235, 110, 50, 255),
            CourseType.Normal => new Color(100, 175, 60, 255),
            CourseType.Hard => new Color(60, 130, 200, 255),
            CourseType.Oni => new Color(215, 55, 130, 255),
            CourseType.Edit => new Color(130, 70, 200, 255),
            _ => new Color(150, 150, 160, 255),
        };

        // 難易度をえらぶ板
        private void DrawDifficulty()
        {
            if (diffSong == null) return;
            float t = 1f - MathF.Pow(1f - diffAnim, 3f); // だんだんゆっくり

            Raylib.DrawRectangle(0, 0, Screen.Width, Screen.Height, new Color(0, 0, 0, (int)(150 * t)));
            Rectangle panel = new Rectangle(440, 220 + (1f - t) * 60f, 1040, 620);
            Raylib.DrawRectangleRounded(panel, 0.06f, 12, new Color(18, 22, 34, (int)(245 * t)));
            Raylib.DrawRectangleRounded(new Rectangle(panel.X, panel.Y, panel.Width, 8), 1f, 4, backColor);

            DrawCenteredFit(diffSong.Title, new Rectangle(panel.X + 30, panel.Y + 24, panel.Width - 60, 56), 46, Color.White);
            if (diffSong.Subtitle.Length > 0)
                DrawCenteredFit(diffSong.Subtitle, new Rectangle(panel.X + 30, panel.Y + 84, panel.Width - 60, 30), 22, Gui.SubTextColor);

            // 箱：戻る・オプション・かんたん〜おに
            float[] widths = { 110, 110, 175, 175, 175, 175 };
            const float gap = 14, boxTop = 150, boxHeight = 380;
            float x = panel.X + (panel.Width - (widths.Sum() + gap * (widths.Length - 1))) / 2f;
            for (int i = 0; i < widths.Length; i++)
            {
                bool selected = i == diffCursor;
                float lift = selected ? 14 : 0;
                Rectangle box = new Rectangle(x, panel.Y + boxTop - lift, widths[i], boxHeight);
                DrawDiffBox(i, box, selected);
                if (active && !options.IsOpen && Gui.Clicked(box))
                {
                    if (i == diffCursor) DecideDifficulty();
                    else { diffCursor = i; MenuSounds.Ka(); }
                }
                x += widths[i] + gap;
            }

            string uraHint = HasCourse(CourseType.Oni) && HasCourse(CourseType.Edit) ? "　おにで →・カッ・Tab：裏おに" : "";
            DrawCenteredFit($"カッ・←→：えらぶ　ドン・Enter：決定　Esc：もどる{uraHint}",
                new Rectangle(panel.X + 20, panel.Y + panel.Height - 56, panel.Width - 40, 36), 22, Gui.SubTextColor);
        }

        private void DrawDiffBox(int index, Rectangle box, bool selected)
        {
            if (selected) Raylib.DrawRectangleRounded(new Rectangle(box.X - 6, box.Y - 6, box.Width + 12, box.Height + 12), 0.12f, 10, CursorColor);

            if (index <= 1)
            {
                Color fill = index == 0 ? BackBoxColor : new Color(60, 70, 100, 255);
                Raylib.DrawRectangleRounded(box, 0.12f, 10, fill);
                if (index == 1) Icons.Gear(box.X + box.Width / 2f, box.Y + box.Height / 2f - 40, Color.White, fill);
                else DrawCenteredFit("◀", new Rectangle(box.X, box.Y + box.Height / 2f - 70, box.Width, 60), 44, Color.White);
                DrawCenteredFit(index == 0 ? "もどる" : "オプション", new Rectangle(box.X + 6, box.Y + box.Height / 2f + 10, box.Width - 12, 40), 24, Color.White);
                return;
            }

            CourseType type = CourseAt(index);
            bool exists = HasCourse(type);
            Color color = exists ? CourseColor(type) : new Color(70, 72, 82, 255);
            Raylib.DrawRectangleRounded(box, 0.12f, 10, Raylib.ColorBrightness(color, -0.5f));
            Raylib.DrawRectangleRounded(new Rectangle(box.X + 5, box.Y + 5, box.Width - 10, box.Height - 10), 0.12f, 10, color);

            string name = type == CourseType.Edit ? "おに（裏）" : Chart.GetCourseName(type);
            DrawCenteredFit(name, new Rectangle(box.X + 8, box.Y + 26, box.Width - 16, 44), 32, Color.White);
            if (!exists)
            {
                DrawCenteredFit("なし", new Rectangle(box.X, box.Y + box.Height / 2f - 30, box.Width, 60), 30, new Color(200, 200, 210, 255));
                return;
            }

            int level = LevelOf(type);
            DrawCenteredFit($"★{level}", new Rectangle(box.X, box.Y + 110, box.Width, 90), 64, Color.White);

            // ★を縦に並べる(10個まで)
            int stars = Math.Clamp(level, 0, 10);
            for (int s = 0; s < 10; s++)
            {
                Color star = s < stars ? new Color(255, 230, 110, 255) : new Color(255, 255, 255, 60);
                FontManager.Draw("★", box.X + box.Width / 2f - 12 + (s % 2 == 0 ? -18 : 18), box.Y + 220 + (s / 2) * 28, 24, star);
            }

            // 裏がある曲は、おにの箱に印
            if (index == 5 && HasCourse(CourseType.Oni) && HasCourse(CourseType.Edit))
                DrawCenteredFit(ura ? "裏" : "表", new Rectangle(box.X + box.Width - 46, box.Y + box.Height - 46, 38, 38), 24, new Color(255, 255, 255, 220));
        }

        // 検索の箱
        private void DrawSearchBox()
        {
            Raylib.DrawRectangleRec(SearchBox, searchFocused ? new Color(18, 22, 36, 240) : new Color(18, 22, 36, 180));
            if (searchFocused) Raylib.DrawRectangleLinesEx(SearchBox, 2, Gui.AccentColor);

            string composition = searchFocused ? WindowsIme.CompositionText : "";
            if (composition.Length == 0)
            {
                string caret = searchFocused && (caretTimer % 1f) < 0.5f ? "|" : "";
                // まだ何も入れていないときは、薄い字で説明を出す(「|」はその前に)
                if (searchText.Length == 0)
                    FontManager.Draw("曲名・アーティスト・フォルダ名", SearchTextX + 14, SearchTextY + 1, 26, Gui.SubTextColor);
                FontManager.Draw(searchText + caret, SearchTextX, SearchTextY, SearchFontSize, Color.White);
            }
            else
            {
                FontManager.Draw(searchText, SearchTextX, SearchTextY, SearchFontSize, Color.White);
                float startX = SearchTextX + FontManager.Measure(searchText, SearchFontSize).X;
                Vector2 size = FontManager.Measure(composition, SearchFontSize);
                FontManager.Draw(composition, startX, SearchTextY, SearchFontSize, CompositionColor);
                float lineY = SearchTextY + size.Y + 2;
                Raylib.DrawLineEx(new Vector2(startX, lineY), new Vector2(startX + size.X, lineY), 2, CompositionColor);
                int cursor = Math.Clamp(WindowsIme.CompositionCursor, 0, composition.Length);
                FontManager.Draw("|", startX + FontManager.Measure(composition[..cursor], SearchFontSize).X, SearchTextY, SearchFontSize, Color.White);
            }

            if (searchFocused && searchText.Length > 0)
            {
                int count = SearchResults().Count;
                FontManager.Draw(count > 0 ? $"{count}曲　Enter：開く" : "当てはまる曲はありません", SearchBox.X + SearchBox.Width + 16, SearchTextY + 2, 24,
                    count > 0 ? Color.White : ErrorColor);
            }
        }

        private void DrawFooter()
        {
            string hint;
            if (SongList.IsScanning) hint = "曲を読み込み中…　Esc：モード選択へ（読み込みは続きます）";
            else if (searchFocused) hint = "文字を入れて検索　Enter：結果を開く　Esc：検索をやめる";
            else if (options.IsOpen) hint = "クリックで変える　Esc・F2：オプションを閉じる";
            else if (diffOpen) hint = "";
            else if (inSongs)
            {
                KeyboardKey favKey = ConfigManager.Key.SystemKeys.GetValueOrDefault(SystemAction.Favorite, KeyboardKey.F4);
                hint = $"カッ・↑↓：えらぶ　ドン・Enter：決定　Esc：ジャンルへもどる　{favKey}：お気に入り";
            }
            else hint = "カッ・↑↓：えらぶ　ドン・Enter：決定　Esc：モード選択へ";
            if (hint.Length > 0) FontManager.Draw(hint, 60, 1022, 26, Gui.SubTextColor);
        }

        private void DrawToast()
        {
            float width = FontManager.Measure(toast, 28).X;
            Rectangle box = new Rectangle(CenterX - width / 2f - 30, 140, width + 60, 52);
            Raylib.DrawRectangleRounded(box, 0.4f, 8, new Color(0, 0, 0, 210));
            FontManager.Draw(toast, box.X + 30, box.Y + 11, 28, new Color(255, 220, 120, 255));
        }

        // 曲を読み込み中(「読み込んだ数 / 曲ファイルの総数」とバー)
        private static void DrawScanning()
        {
            int total = SongList.ScanTotal;
            int done = Math.Min(SongList.ScanDone, Math.Max(total, 0));
            string text = total < 0 ? "曲ファイルを探しています…" : $"曲を読み込み中…　{done} / {total}";
            FontManager.Draw(text, 160, 420, 40, Color.White);

            float progress = total > 0 ? (float)done / total : 0f;
            Rectangle bar = new Rectangle(160, 490, 1600, 20);
            Raylib.DrawRectangleRec(bar, new Color(30, 34, 48, 255));
            Raylib.DrawRectangleRec(new Rectangle(bar.X, bar.Y, bar.Width * progress, bar.Height), CursorColor);
        }

        // 曲が1つも無いとき
        private static void DrawEmpty()
        {
            FontManager.Draw("曲がありません", 160, 360, 40, ErrorColor);
            FontManager.Draw("下のフォルダに、曲のフォルダ（.tja と .ogg が入ったもの）を入れてください。", 160, 430, 28, Color.White);
            FontManager.Draw(SongList.SongsFolder, 160, 480, 24, Gui.SubTextColor);
            FontManager.Draw("カテゴリ分けしたいときは、box.def の入ったフォルダの中に入れてください。", 160, 530, 28, Color.White);
            FontManager.Draw("入れたら、設定（F1）の「一般設定」→［曲の一覧を更新］を押してください。", 160, 580, 28, Color.White);
        }

        // 四角の真ん中に、はみ出さない大きさで文字を書く
        private static void DrawCenteredFit(string text, Rectangle area, float size, Color color)
        {
            if (text.Length == 0) return;
            Vector2 measured = FontManager.Measure(text, size);
            if (measured.X > area.Width && measured.X > 0)
            {
                size *= area.Width / measured.X;
                measured = FontManager.Measure(text, size);
            }
            FontManager.Draw(text, area.X + (area.Width - measured.X) / 2f, area.Y + (area.Height - measured.Y) / 2f, size, color);
        }
    }
}
