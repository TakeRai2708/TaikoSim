using Raylib_cs;
using TaikoSim.Graphics;
using TaikoSim.Input;
using TaikoSim.UI;

namespace TaikoSim.Scenes
{
    // クレジット画面(ホームから開く)。ライセンスの全文は THIRD-PARTY-NOTICES.txt(exe と同じフォルダ)にある
    public sealed class CreditsScene : Scene
    {
        private const float Left = 80f, Top = 150f, LineHeight = 38f;
        private float scroll;

        // (文字, 大きさ, 色の種類 0:見出し 1:ふつう 2:うすい)
        private static readonly (string Text, float Size, int Kind)[] Lines =
        {
            ("TaikoSim", 40, 0),
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
            ("画像・効果音・BGM・キャラクター（ポンとピコ）は、すべて TaikoSim 用にプログラムで作ったものです", 24, 1),
            ("", 20, 2),
            ("ライセンスの全文は、exe と同じフォルダの THIRD-PARTY-NOTICES.txt を見てください。", 24, 2),
            ("「太鼓の達人」はバンダイナムコエンターテインメントの商標です。TaikoSim は公式のものではありません。", 24, 2),
        };

        public override void Update(float dt)
        {
            float wheel = Raylib.GetMouseWheelMove();
            if (Raylib.IsKeyDown(KeyboardKey.Down)) wheel -= dt * 12f;
            if (Raylib.IsKeyDown(KeyboardKey.Up)) wheel += dt * 12f;
            float max = Math.Max(0f, Lines.Length * LineHeight - (Screen.Height - Top - 120f));
            scroll = Math.Clamp(scroll - wheel * 40f, 0f, max);

            if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter) || InputManager.IsDonPressed())
                OnBack();
        }

        public override void OnBack() => SceneManager.ChangeScene(new HomeScene());

        public override void Draw()
        {
            Gui.BeginFrame();
            Raylib.ClearBackground(new Color(20, 22, 32, 255));
            FontManager.Draw("クレジット", Left, 60, 52, Color.White);

            Raylib.BeginScissorMode(0, (int)Top - 10, Screen.Width, Screen.Height - (int)Top - 100);
            float y = Top - scroll;
            foreach (var (text, size, kind) in Lines)
            {
                Color color = kind switch { 0 => new Color(255, 200, 80, 255), 1 => Color.White, _ => Gui.SubTextColor };
                if (text.Length > 0) FontManager.Draw(text, Left + (kind == 0 ? 0 : 24), y, size, color);
                y += kind == 0 ? LineHeight + 8 : LineHeight;
            }
            Raylib.EndScissorMode();

            FontManager.Draw("↑↓・ホイール：動かす　Esc・Enter・ドン：もどる", Left, Screen.Height - 70, 24, Color.SkyBlue);
            if (Gui.Button(new Rectangle(Screen.Width - 270, Screen.Height - 90, 190, 56), "もどる")) OnBack();
        }
    }
}