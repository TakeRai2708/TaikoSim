using System.Numerics;
using Raylib_cs;
using TaikoSim.Audio;
using TaikoSim.Configs;
using TaikoSim.Graphics;
using TaikoSim.Input;
using TaikoSim.UI;

namespace TaikoSim.Scenes
{
    // モード選択(起動したら最初に出る画面。前のホーム画面のかわり)
    //   演奏ゲーム → 選曲画面
    //   段位道場・特訓モード → まだ無い(準備中)
    //   設定 → 設定パネル、クレジット → クレジット画面
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

        // 選んでいる項目(ほかの画面から戻ってきても同じ場所)
        private static int cursor = 0;
        private float shown; // 表示上の位置(cursor にだんだん近づく)

        // 配置
        private const float CenterX = 960, CenterY = 560;
        private const float BarWidth = 760, BarHeight = 78, Gap = 14;
        private const float SelectedWidth = 900, SelectedHeight = 210;

        // 終了の確認・お知らせ
        private const float ExitConfirmSeconds = 3f;
        private float exitTimer = 0f;
        private string toast = "";
        private float toastTimer = 0f;

        public override void Enter()
        {
            shown = cursor;
        }

        // Esc：1回目は確認を出す、確認中(3秒以内)にもう1回押したら終了
        public override void OnBack()
        {
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
            shown += (cursor - shown) * Math.Min(1f, dt * 14f);
            if (Math.Abs(cursor - shown) < 0.001f) shown = cursor;

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
            int next = Math.Clamp(cursor + direction, 0, Items.Length - 1);
            if (next == cursor) return;
            cursor = next;
            exitTimer = 0f;
            MenuSounds.Ka();
        }

        private void Decide()
        {
            Item item = Items[cursor];
            if (!item.Ready)
            {
                MenuSounds.Ka();
                ShowToast($"{item.Title}は準備中です");
                return;
            }

            MenuSounds.Don();
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
            Color baseColor = Items[cursor].Color;
            DrawBackground(baseColor);

            FontManager.Draw("モードをえらぶ", 80, 60, 56, Color.White);

            // 項目(選んでいるものを真ん中に大きく)
            for (int i = 0; i < Items.Length; i++)
            {
                float r = i - shown;
                float grow = Math.Clamp(1f - Math.Abs(r), 0f, 1f); // 真ん中に近いほど 1
                float width = BarWidth + (SelectedWidth - BarWidth) * grow;
                float height = BarHeight + (SelectedHeight - BarHeight) * grow;

                // 真ん中から離れた分だけずらす(大きい箱の分も空ける)
                float offset = r * (BarHeight + Gap) + Math.Sign(r) * Math.Min(Math.Abs(r), 1f) * (SelectedHeight - BarHeight) / 2f;
                float y = CenterY + offset - height / 2f;
                if (y > 1080 || y + height < 120) continue;

                Rectangle area = new Rectangle(CenterX - width / 2f, y, width, height);
                DrawItem(Items[i], area, grow, i == cursor);

                // クリック：ほかの項目なら選ぶ、選んでいる項目なら決定
                if (Gui.Clicked(area))
                {
                    if (i == cursor) Decide();
                    else Move(i - cursor);
                }
            }

            FontManager.Draw("カッ・↑↓：えらぶ　ドン・Enter：決定　Esc：終了（2回）", 80, 1010, 26, Gui.SubTextColor);
            FontManager.Draw("テスト：.tja ファイルをウィンドウにドラッグ＆ドロップすると譜面が流れます", 80, 975, 22, new Color(220, 190, 110, 255));

            if (toastTimer > 0f) DrawBanner(toast, new Color(255, 200, 60, 255));
            else if (exitTimer > 0f) DrawBanner("3秒以内にもう一度 Esc を押すと終了します", new Color(255, 200, 60, 255));
        }

        private static void DrawBackground(Color tint)
        {
            Color top = Raylib.ColorBrightness(tint, -0.72f);
            Color bottom = Raylib.ColorBrightness(tint, -0.86f);
            Raylib.DrawRectangleGradientV(0, 0, Screen.Width, Screen.Height, top, bottom);
        }

        private static void DrawItem(Item item, Rectangle area, float grow, bool selected)
        {
            Color fill = item.Ready ? item.Color : Raylib.ColorBrightness(item.Color, -0.45f);
            // 選んでいるときは、少し大きい黄色の箱を後ろに描いて枠にする
            if (selected)
                Raylib.DrawRectangleRounded(new Rectangle(area.X - 6, area.Y - 6, area.Width + 12, area.Height + 12), 0.25f, 12,
                    new Color(255, 220, 80, 255));
            Raylib.DrawRectangleRounded(area, 0.25f, 12, Raylib.ColorBrightness(fill, -0.55f));
            Rectangle inner = new Rectangle(area.X + 5, area.Y + 5, area.Width - 10, area.Height - 10);
            Raylib.DrawRectangleRounded(inner, 0.25f, 12, fill);

            Color text = item.Ready ? Color.White : new Color(200, 200, 210, 255);
            float titleSize = 40 + 18 * grow;
            Vector2 titleWidth = FontManager.Measure(item.Title, titleSize);
            float titleY = grow > 0.5f ? area.Y + area.Height * 0.22f : area.Y + (area.Height - titleSize) / 2f - 4;
            FontManager.Draw(item.Title, area.X + (area.Width - titleWidth.X) / 2f, titleY, titleSize, text);

            // 選んでいるときだけ説明
            if (grow > 0.5f)
            {
                byte alpha = (byte)(255 * Math.Clamp((grow - 0.5f) * 2f, 0f, 1f));
                Vector2 descWidth = FontManager.Measure(item.Description, 28);
                FontManager.Draw(item.Description, area.X + (area.Width - descWidth.X) / 2f, area.Y + area.Height * 0.62f, 28,
                    new Color((byte)235, (byte)235, (byte)240, alpha));
            }
        }

        private static void DrawBanner(string text, Color color)
        {
            float width = FontManager.Measure(text, 26).X;
            Raylib.DrawRectangle(0, 130, (int)width + 40, 46, new Color(0, 0, 0, 210));
            FontManager.Draw(text, 20, 140, 26, color);
        }
    }
}
