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
    //   段位道場・特訓モード → まだ無い(準備中)
    //   設定 → 設定パネル、クレジット → クレジット画面
    // 見た目と動きは選曲画面と同じ(UI/BarList。TaikøNauts の選曲画面を測った値)
    //   ・板は斜めに並ぶ。上下はぐるっとつながっている
    //   ・カーソルは0.15秒で1つ動く。止まってから0.43秒で選んでいる板が開く
    //   ・決定すると、黄色い枠が点滅してから次の画面へ
    // 操作：カッ・↑↓・ホイールで選ぶ、ドン・Enter・クリックで決定、Esc を2回で終了
    public class ModeSelectScene : Scene
    {
        private sealed record Item(string Title, string Description, Color Color, bool Ready);

        private static readonly Item[] Items =
        {
            new("演奏ゲーム", "すきな曲・むずかしさをえらんであそぶ", new Color(225, 70, 80, 255), true),
            new("段位道場", "準備中", new Color(70, 90, 170, 255), false),
            new("特訓モード", "準備中", new Color(60, 150, 110, 255), false),
            new("設定", "音・キー・画面・スキンなどの設定", new Color(90, 100, 125, 255), true),
            new("クレジット", "使っているソフト・フォント・ライセンス", new Color(60, 130, 150, 255), true),
        };

        // 選んでいる項目(ほかの画面から戻ってきても同じ場所)。ぐるっと回れるので、項目の番号は Items.Length で割った余り
        private static int cursor = 0;
        private readonly BarList list = new(MenuSkin.ModeFolder);
        private Item? deciding;                // 決定して、枠が点滅している項目

        // 終了の確認・お知らせ
        private const float ExitConfirmSeconds = 3f;
        private float exitTimer = 0f;
        private string toast = "";
        private float toastTimer = 0f;

        private static Item ItemAt(int pos) => Items[((pos % Items.Length) + Items.Length) % Items.Length];
        private static Item Current => ItemAt(cursor);

        public override void Enter()
        {
            cursor = ((cursor % Items.Length) + Items.Length) % Items.Length;
            list.Reset(cursor);
            deciding = null;
            MenuSkin.Preload(MenuSkin.ModeFolder, MenuSkin.BarImages);
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
            cursor += Math.Sign(direction);
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
                case "演奏ゲーム": SceneManager.ChangeScene(new SongSelectScene()); break;
                case "設定": Game.OpenSettings(); break;
                case "クレジット": SceneManager.ChangeScene(new CreditsScene()); break;
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
            Color baseColor = Current.Color;
            Raylib.DrawRectangleGradientV(0, 0, Screen.Width, Screen.Height,
                Raylib.ColorBrightness(baseColor, -0.55f), Raylib.ColorBrightness(baseColor, -0.8f));

            DrawList();

            Text("モードをえらぶ", 40, 26, 44, Color.White);

            DrawHint("カッ・↑↓：えらぶ　ドン・Enter：決定　Esc：終了（2回）", 1030);
            DrawHint("テスト：.tja ファイルをウィンドウにドラッグ＆ドロップすると譜面が流れます", 984, new Color(240, 205, 120, 255), 20);

            if (toastTimer > 0f) DrawBanner(toast, new Color(255, 200, 60, 255));
            else if (exitTimer > 0f) DrawBanner("3秒以内にもう一度 Esc を押すと終了します", new Color(255, 200, 60, 255));
        }

        // 項目(斜めに並べる。上下はぐるっとつながっている)
        private void DrawList()
        {
            int baseIndex = (int)MathF.Round(list.Shown);
            var (before, after) = BarList.VisibleRange(Items.Length);
            bool clickable = deciding == null;

            // 選んでいる板は、ほかの板の上に描くので最後
            Rectangle? selectedArea = null;
            for (int k = -before; k <= after; k++)
            {
                int pos = baseIndex + k;
                Item item = ItemAt(pos);
                Rectangle area = BarList.BarArea(pos - list.Shown);
                if (area.Y > Screen.Height || area.Y + area.Height < 0) continue;

                bool selected = pos == cursor;
                if (selected) selectedArea = list.OpenArea(area);
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
                list.DrawFrame(sel, 1f);
                DrawItem(Current, sel, list.Expand);
            }
        }

        // 1本の板。open は開き具合(0→1)
        private void DrawItem(Item item, Rectangle area, float open)
        {
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
            Centered(item.Description, new Rectangle(pill.X + 20, pill.Y, pill.Width - 40, pill.Height), 32, Fade(ink, open));
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
