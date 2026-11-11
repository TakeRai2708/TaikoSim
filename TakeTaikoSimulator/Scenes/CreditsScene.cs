using Raylib_cs;
using TakeTaikoSimulator.Graphics;
using TakeTaikoSimulator.Input;
using TakeTaikoSimulator.UI;

namespace TakeTaikoSimulator.Scenes
{
    // クレジット画面(モード選択から開く)。ライセンスの全文は THIRD-PARTY-NOTICES.txt(exe と同じフォルダ)にある
    public sealed class CreditsScene : Scene
    {
        private const float Left = 80f, Top = 150f, LineHeight = 38f;
        private const float HeadingExtra = 8f;     // 見出しの行は少し広く空ける
        private const float ViewBottomMargin = 110f; // 下の「もどる」ボタンと説明の分
        private float scroll;

        // 見える範囲の高さ(上の Top から、下の説明の上まで)
        private static float ViewHeight => Screen.Height - Top - ViewBottomMargin;

        // 全部の行の高さ(見出しの分も入れる)
        private static float ContentHeight()
        {
            float h = 0f;
            foreach (var (_, _, kind) in Lines) h += kind == 0 ? LineHeight + HeadingExtra : LineHeight;
            return h;
        }

        // 一番下までスクロールしたときの量(最後の行の下に少し余白を残す)
        private static float MaxScroll => Math.Max(0f, ContentHeight() + 20f - ViewHeight);

        // (文字, 大きさ, 色の種類 0:見出し 1:ふつう 2:うすい)
        private static readonly (string Text, float Size, int Kind)[] Lines =
        {
            ("TakeTaikoSimulator", 40, 0),
            ("作った人：たけらい", 28, 1),
            ("太鼓の達人ふうの音楽ゲームを、自分で遊ぶために作ったものです。非公開・非売品です。", 24, 2),
            ("", 20, 2),
            ("参考にしたソフト（動き・決まり・計算のしかた）", 32, 0),
            ("OpenTaiko — Copyright (c) 2021 0auBSQ — MIT ライセンス", 26, 1),
            ("TJAPlayer3 — Copyright (c) 2017 J.MIR — MIT ライセンス", 26, 1),
            ("TaikøNauts（見た目・時間を動画で測って合わせた）", 26, 1),
            ("", 20, 2),
            ("使っているライブラリ", 32, 0),
            ("raylib — zlib/libpng ライセンス", 26, 1),
            ("Raylib-cs（raylib を C# から使う）— zlib/libpng ライセンス", 26, 1),
            ("BASS・BASSmix・BASSWASAPI・BASSASIO — un4seen developments（お金をもうけない使い方なら無料）", 26, 1),
            ("ManagedBass（BASS を C# から使う）— MIT ライセンス", 26, 1),
            ("", 20, 2),
            ("フォント", 32, 0),
            ("Noto Sans JP — SIL Open Font License 1.1", 26, 1),
            ("TaikoSim-Simple スキンの文字の画像は Noto Sans CJK（SIL Open Font License 1.1）で描きました", 24, 2),
            ("", 20, 2),
            ("TaikoSim-Simple スキン", 32, 0),
            ("画像・効果音・BGMは、すべて TakeTaikoSimulator 用にプログラムで作ったものです", 24, 1),
            ("", 20, 2),
            ("ライセンスの全文は、exe と同じフォルダの THIRD-PARTY-NOTICES.txt を見てください。", 24, 2),
            ("「太鼓の達人」はバンダイナムコエンターテインメントの商標です。TakeTaikoSimulator は公式のものではありません。", 24, 2),
        };

        public override void Update(float dt)
        {
            float wheel = Raylib.GetMouseWheelMove();
            if (Raylib.IsKeyDown(KeyboardKey.Down)) wheel -= dt * 12f;
            if (Raylib.IsKeyDown(KeyboardKey.Up)) wheel += dt * 12f;
            if (Raylib.IsKeyPressed(KeyboardKey.PageDown)) wheel -= ViewHeight / 40f;
            if (Raylib.IsKeyPressed(KeyboardKey.PageUp)) wheel += ViewHeight / 40f;
            if (Raylib.IsKeyPressed(KeyboardKey.End)) wheel -= 9999f;
            if (Raylib.IsKeyPressed(KeyboardKey.Home)) wheel += 9999f;
            scroll = Math.Clamp(scroll - wheel * 40f, 0f, MaxScroll);

            if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter) || InputManager.IsDonPressed())
                OnBack();
        }

        public override void OnBack() => SceneManager.Simple(new ModeSelectScene());

        public override void Draw()
        {
            Gui.BeginFrame();
            Raylib.ClearBackground(new Color(20, 22, 32, 255));
            FontManager.Draw("クレジット", Left, 60, 52, Color.White);

            Screen.BeginScissorMode(0, (int)Top - 10, Screen.Width, (int)ViewHeight + 10);
            float y = Top - scroll;
            foreach (var (text, size, kind) in Lines)
            {
                Color color = kind switch { 0 => new Color(255, 200, 80, 255), 1 => Color.White, _ => Gui.SubTextColor };
                if (text.Length > 0) FontManager.Draw(text, Left + (kind == 0 ? 0 : 24), y, size, color);
                y += kind == 0 ? LineHeight + HeadingExtra : LineHeight;
            }
            Screen.EndScissorMode();

            // 右はしのスクロールバー(全部見えているときは出さない)
            float max = MaxScroll;
            if (max > 0f)
            {
                float trackX = Screen.Width - 40f, trackY = Top, trackH = ViewHeight;
                float barH = Math.Max(40f, trackH * ViewHeight / (ViewHeight + max));
                float barY = trackY + (trackH - barH) * (scroll / max);
                Raylib.DrawRectangleRounded(new Rectangle(trackX, trackY, 8, trackH), 1f, 4, new Color(255, 255, 255, 30));
                Raylib.DrawRectangleRounded(new Rectangle(trackX, barY, 8, barH), 1f, 4, new Color(255, 255, 255, 140));
            }

            FontManager.Draw("↑↓・ホイール・PageUp/Down：動かす　Esc・Enter・ドン：もどる", Left, Screen.Height - 70, 24, Color.SkyBlue);
            if (Gui.Button(new Rectangle(Screen.Width - 270, Screen.Height - 90, 190, 56), "もどる")) OnBack();
        }
    }
}

