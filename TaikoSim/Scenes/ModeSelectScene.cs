using Raylib_cs;
using TaikoSim.Audio;
using TaikoSim.Configs;
using TaikoSim.Graphics;
using TaikoSim.Input;
using TaikoSim.UI;
using static TaikoSim.UI.MenuDraw;

namespace TaikoSim.Scenes
{
    // モード選択(起動したら最初に出る画面。前のホーム画面のかわり)
    //   演奏ゲーム → 選曲画面
    //   段位道場 → 段位選択画面／特訓モード → まだ無い(準備中)
    //   設定 → 設定パネル、クレジット → クレジット画面
    // 見た目と動きは選曲画面と同じ(UI/BarList。TaikøNauts の選曲画面を測った値)
    //   ・板は斜めに並ぶ。先頭と末尾で止まる
    //   ・カーソルは0.15秒で1つ動く。止まってから0.43秒で選んでいる板が開く
    //   ・決定すると、黄色い枠が点滅してから次の画面へ
    // 操作：カッ・↑↓・ホイールで選ぶ、ドン・Enter・クリックで決定、Esc を2回で終了
    public class ModeSelectScene : Scene
    {
        private sealed record Item(string Title, string Description, Color Color, bool Ready);

        private static readonly Item[] Items =
        {
            new("演奏ゲーム", "すきな曲・むずかしさをえらんであそぶ", new Color(225, 70, 80, 255), true),
            // 説明は TaikøNauts と同じ3行（10/31 の動画 2026-10-31_01-44-20.mp4 の16秒あたり）
            new("段位道場", "3曲連続して演奏しながら、\nお題の合格を目指そう!\n合格すると腕前が認定されるよ!", new Color(70, 90, 170, 255), true),
            new("特訓モード", "準備中", new Color(60, 150, 110, 255), false),
            new("設定", "音・キー・画面・スキンなどの設定", new Color(90, 100, 125, 255), true),
            new("クレジット", "使っているソフト・フォント・ライセンス", new Color(60, 130, 150, 255), true),
        };

        // 選んでいる項目(ほかの画面から戻ってきても同じ場所)。先頭・末尾を越えない
        private static int cursor = 0;
        private readonly BarList list = new(MenuSkin.ModeFolder);
        private Item? deciding;                // 決定して、枠が点滅している項目
        private const string EntryFolder = "Image/01.Entry/";
        private const string EntryBarFolder = EntryFolder + "Bar/";
        private const string EntryBackground = EntryFolder + "Background.png";
        private float backgroundTime;

        // 画面の下の「フリープレイ」の文字。30px だと小さすぎたので 48px にした
        //   A-Style の CreditBar.png(「1人プレイ」の黄色いバー)はここでは使わない。
        //   あれは一番最初のエントリー画面(コインを入れる画面)のものなので、エントリー画面を作るときに使う
        private static readonly Rectangle FreePlayArea = new(730f, 1012f, 460f, 56f);
        private const float FreePlaySize = 48f;

        // 終了の確認・お知らせ
        private const float ExitConfirmSeconds = 3f;
        private float exitTimer = 0f;
        private string toast = "";
        private float toastTimer = 0f;

        private static Item Current => Items[cursor];

        public override void Enter()
        {
            cursor = Math.Clamp(cursor, 0, Items.Length - 1);
            list.Reset(cursor);
            deciding = null;
            backgroundTime = 0f;
            MenuSkin.Preload(MenuSkin.ModeFolder, MenuSkin.BarImages.Concat(new[] { "Background.png", "Background/0.png" }));
            MenuSkin.Preload(EntryFolder, new[] { "Background.png", "Header.png", "Background_Fireworks.png" });
            MenuSkin.Preload(EntryBarFolder, new[] { "Bar/0.png", "Bar/1.png", "Title/0.png", "Title/1.png",
                "Overlay.png", "Selected.png", "Pop/0.png", "1POnly.png" });
        }

        // Esc：1回目は確認を出す、確認中(3秒以内)にもう1回押したら終了。点滅中なら決定をやめる
        public override void OnBack()
        {
            if (deciding != null)
            {
                deciding = null;
                list.EndDecide();
                return;
            }
            if (exitTimer > 0f)
            {
                Game.ShouldExit = true;
                return;
            }
            exitTimer = ExitConfirmSeconds;
        }

        public override void Update(float dt)
        {
            backgroundTime = (backgroundTime + dt) % 3.2f;
            if (exitTimer > 0f) exitTimer -= dt;
            if (toastTimer > 0f) toastTimer -= dt;
            list.Update(dt, cursor);

            // 決定したあとの点滅中は、何もできない。点滅が終わったら次の画面へ
            if (deciding != null)
            {
                if (list.DecideTime < BarList.BlinkEnd) return;
                Item item = deciding;
                deciding = null;
                list.EndDecide();
                Open(item);
                return;
            }

            // ← カッ(左縁)・↑ で上、カッ(右縁)・↓ で下
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
            int next = Math.Clamp(cursor + Math.Sign(direction), 0, Items.Length - 1);
            if (next == cursor) return;
            cursor = next;
            list.Collapse();
            exitTimer = 0f;
            MenuSounds.Ka();
        }

        private void Decide()
        {
            Item item = Current;
            if (!item.Ready)
            {
                MenuSounds.Ka();
                ShowToast($"{item.Title}は準備中です");
                return;
            }
            MenuSounds.Don();
            exitTimer = 0f;
            deciding = item;
            list.StartDecide();
        }

        // 点滅が終わったら、その項目の画面を開く
        private static void Open(Item item)
        {
            switch (item.Title)
            {
                case "演奏ゲーム": SceneManager.ToSongSelectFromMode(new SongSelectScene()); break;
                case "段位道場": SceneManager.ToSongSelectFromMode(new DanSelectScene()); break;
                case "設定": Game.OpenSettings(); break;
                case "クレジット": SceneManager.Simple(new CreditsScene()); break;
            }
        }

        private void ShowToast(string text)
        {
            toast = text;
            toastTimer = 2f;
        }

        // .tja がドロップされたら、テスト用に譜面を流す(前のホーム画面と同じ)
        public override void OnFilesDropped(string[] paths)
        {
            string? tja = paths.FirstOrDefault(PlayScene.IsTja);
            if (tja != null) SceneManager.ChangeScene(new PlayScene(tja));
        }

        private static bool KeyPressed(KeyboardKey key) => Raylib.IsKeyPressed(key) || Raylib.IsKeyPressedRepeat(key);

        // ===== 描画 =====

        public override void Draw()
        {
            Gui.BeginFrame();
            bool entryBackground = MenuSkin.Whole(EntryBackground, new Rectangle(0, 0, Screen.Width, Screen.Height), Color.White);
            if (entryBackground) DrawEntryFireworks();
            else if (!DrawSkinBackground())
            {
                Color baseColor = Current.Color;
                Raylib.DrawRectangleGradientV(0, 0, Screen.Width, Screen.Height,
                    Raylib.ColorBrightness(baseColor, -0.55f), Raylib.ColorBrightness(baseColor, -0.8f));
            }

            DrawList();

            if (entryBackground)
            {
                // 上のプレイ情報は板より手前に出す（板が上へ流れても隠さない）
                MenuSkin.Whole(EntryFolder + "Header.png", new Rectangle(0, 0, Screen.Width, Screen.Height), Color.White);
                Outlined("フリープレイ", FreePlayArea, FreePlaySize, Color.White, new Color(12, 12, 16, 255));
            }
            else
            {
                Text("モードをえらぶ", 40, 26, 44, Color.White);
                DrawHint("カッ・↑↓：えらぶ　ドン・Enter：決定　Esc：終了（2回）", 1030);
                DrawHint("テスト：.tja ファイルをウィンドウにドラッグ＆ドロップすると譜面が流れます", 984, new Color(240, 205, 120, 255), 20);
            }

            if (toastTimer > 0f) DrawBanner(toast, new Color(255, 200, 60, 255));
            else if (exitTimer > 0f) DrawBanner("3秒以内にもう一度 Esc を押すと終了します", new Color(255, 200, 60, 255));
        }

        // 01.Entry が無いスキンはモード専用画像を試し、無ければシステムのグラデーションに戻す
        private static bool DrawSkinBackground()
        {
            Rectangle screen = new(0, 0, Screen.Width, Screen.Height);
            return MenuSkin.Whole(MenuSkin.ModeFolder + "Background.png", screen, Color.White)
                || MenuSkin.Whole(MenuSkin.ModeFolder + "Background/0.png", screen, Color.White);
        }

        private void DrawEntryFireworks()
        {
            // 背景と別になっている花火(300×300 の2コマ)をゆっくり浮かべる
            DrawFirework(380, 75, 0, 0.25f);
            DrawFirework(1360, 85, 1, 1.45f);
        }

        private void DrawFirework(float x, float y, int cell, float offset)
        {
            if (SkinManager.GetTexture(EntryFolder + "Background_Fireworks.png") is not Texture2D texture) return;
            float age = (backgroundTime + offset) % 3.2f;
            if (age >= 1.4f) return;
            float size = 300f * (0.7f + 0.3f * age / 1.4f);
            float alpha = MathF.Sin(age / 1.4f * MathF.PI) * 0.6f;
            Raylib.DrawTexturePro(texture, new Rectangle(cell * 300, 0, 300, 300),
                new Rectangle(x - size / 2, y - size / 2, size, size), System.Numerics.Vector2.Zero, 0f, Fade(Color.White, alpha));
        }

        // 項目(斜めに並べる。先頭と末尾の外には項目を描かない)
        private void DrawList()
        {
            bool clickable = deciding == null;
            bool entryLayout = EntryBarType(Items[0]) >= 0;
            int selectedPos = cursor;

            // 選んでいる板は、ほかの板の上に描くので最後
            Rectangle? selectedArea = null;
            for (int pos = 0; pos < Items.Length; pos++)
            {
                Item item = Items[pos];
                float distance = pos - list.Shown;
                Rectangle area = BarList.BarArea(distance);
                // モード選択の動画は選曲より上下の板が50px離れている
                if (entryLayout) area.Y += Math.Clamp(distance, -1f, 1f) * 50f;
                if (area.Y > Screen.Height || area.Y + area.Height < 0) continue;

                bool selected = pos == selectedPos;
                if (selected)
                {
                    Rectangle openedArea = list.OpenArea(area);
                    if (entryLayout)
                    {
                        float spread = 23f * EaseOut(list.Expand);
                        openedArea.Y -= spread;
                        openedArea.Height += spread * 2;
                    }
                    selectedArea = openedArea;
                }
                else DrawItem(item, area, 0f);

                // クリック：ほかの項目なら選ぶ、選んでいる項目なら決定
                if (clickable && Gui.Clicked(selected && selectedArea is Rectangle open ? open : area))
                {
                    if (selected) Decide();
                    else
                    {
                        cursor = pos;
                        list.Collapse();
                        exitTimer = 0f;
                        MenuSounds.Ka();
                    }
                }
            }

            if (selectedArea is Rectangle sel)
            {
                Item selectedItem = Items[selectedPos];
                int entryType = EntryBarType(selectedItem);
                list.DrawFrame(sel, 1f, entryType >= 0 ? EntryBarFolder + "Selected.png" : null);
                DrawItem(selectedItem, sel, list.Expand);
                if (entryType == 0 && list.FrameShown)
                {
                    float alpha = Math.Clamp(list.FrameSeconds / 0.13f, 0f, 1f);
                    MenuSkin.Whole(EntryBarFolder + $"Pop/{entryType}.png", new Rectangle(288, 271, 1344, 486), MenuSkin.White(alpha));
                }
            }
        }

        // A-Style の 2・3 は「プレイヤー設定」「AIバトル演奏」で、TaikoSim の項目とは違う
        private static int EntryBarType(Item item)
        {
            int type = item.Title switch { "演奏ゲーム" => 0, "段位道場" => 1, _ => -1 };
            return type >= 0 && MenuSkin.Has(EntryBarFolder + $"Bar/{type}.png", EntryBarFolder + $"Title/{type}.png")
                ? type : -1;
        }

        // 1本の板。open は開き具合(0→1)
        private void DrawItem(Item item, Rectangle area, float open)
        {
            int entryType = EntryBarType(item);
            if (entryType >= 0)
            {
                DrawEntryBar(item, entryType, area, open);
                return;
            }

            Color fill = item.Ready ? item.Color : Raylib.ColorBrightness(item.Color, -0.4f);
            Rectangle inner = list.DrawBody(area, fill, 1f, plain: false);
            Color ink = Raylib.ColorBrightness(fill, -0.65f);
            Color text = item.Ready ? Color.White : new Color(215, 215, 225, 255);

            if (open <= 0f)
            {
                Outlined(item.Title, inner, 50, text, ink);
                return;
            }

            // 開いた板：上に名前、下に説明
            Outlined(item.Title, new Rectangle(inner.X + 20, inner.Y + 30, inner.Width - 40, 90), 72, text, ink);
            Rectangle pill = new Rectangle(inner.X + 60, inner.Y + inner.Height - 110, inner.Width - 120, 70);
            Raylib.DrawRectangleRounded(pill, 1f, 12, Fade(new Color(255, 255, 255, 210), open));
            // スキンの画像が無いときの細い帯は1行しか入らないので、行をつなげて書く（はみ出すぶんは小さくなる）
            Centered(item.Description.Replace("\n", ""), new Rectangle(pill.X + 20, pill.Y, pill.Width - 40, pill.Height), 32, Fade(ink, open));
        }

        // 980×160 の板とタイトル。開いたときは土台だけ伸ばし、タイトルは原寸で上に置く
        private static void DrawEntryBar(Item item, int type, Rectangle area, float open)
        {
            Rectangle imageArea = new(area.X - 24, area.Y - 18, area.Width + 48, area.Height + 36);
            MenuSkin.NPatch(EntryBarFolder + $"Bar/{type}.png", imageArea, 40, Color.White);

            if (open <= 0f)
                MenuSkin.Whole(EntryBarFolder + "Overlay.png", imageArea, Color.White);
            else
            {
                // 閉じた板の白い帯を、開いた板の説明欄に伸ばす
                Rectangle plate = new(area.X + 70, area.Y + 135, area.Width - 140, area.Height - 157);
                if (plate.Height > 0)
                    MenuSkin.NPatchPart(EntryBarFolder + "Overlay.png", new Rectangle(93, 38, 794, 84), plate, 10, Color.White);

                if (plate.Height > 90)
                {
                    Color ink = new(20, 20, 24, 255);
                    if (type == 0)
                    {
                        Outlined("すきな曲や、むずかしさを", new Rectangle(plate.X + 20, plate.Y + 42, plate.Width - 40, 48), 36, Color.White, ink);
                        Outlined("えらんであそべるよ！", new Rectangle(plate.X + 20, plate.Y + 100, plate.Width - 40, 48), 36, Color.White, ink);
                    }
                    else
                    {
                        // 段位道場は3行（本家：行の間 55px くらい・白い字に黒い縁）
                        string[] lines = item.Description.Split('\n');
                        float lineHeight = Math.Min(55f, (plate.Height - 20f) / Math.Max(1, lines.Length));
                        float top = plate.Y + (plate.Height - lineHeight * lines.Length) / 2f;
                        for (int i = 0; i < lines.Length; i++)
                            Outlined(lines[i], new Rectangle(plate.X + 20, top + i * lineHeight, plate.Width - 40, lineHeight), 34, Color.White, ink);
                    }
                }
            }

            MenuSkin.Whole(EntryBarFolder + $"Title/{type}.png",
                new Rectangle(imageArea.X, imageArea.Y, imageArea.Width, 160), Color.White);
            if (type == 1)
                MenuSkin.Whole(EntryBarFolder + "1POnly.png",
                    new Rectangle(area.X - 80, area.Y - 35, 400, 70), Color.White);
        }

        // 下の操作説明(うしろに薄い帯)
        private static void DrawHint(string hint, float y, Color? color = null, float size = 24)
        {
            var measured = Measure(hint, size);
            Raylib.DrawRectangleRounded(new Rectangle(28, y - 7, measured.X + 32, measured.Y + 14), 0.5f, 8, new Color(0, 0, 0, 150));
            Text(hint, 44, y, size, color ?? new Color(220, 224, 236, 255));
        }

        private static void DrawBanner(string text, Color color)
        {
            float width = Measure(text, 26).X;
            Raylib.DrawRectangle(0, 130, (int)width + 40, 46, new Color(0, 0, 0, 210));
            Text(text, 20, 140, 26, color);
        }
    }
}
