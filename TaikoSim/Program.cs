using Raylib_cs;

namespace TaikoSim
{
    internal class Program
    {
        static void Main(string[] args)
        {
            // ウィンドウを作る(横1280, 縦720, タイトル)
            Raylib.InitWindow(1280, 720, "TaikoSim");

            // 1秒間に60回画面を更新する
            Raylib.SetTargetFPS(60);

            // ウィンドウが閉じられるまでずっと繰り返す
            while (!Raylib.WindowShouldClose())
            {
                Raylib.BeginDrawing();               // 描画開始

                Raylib.ClearBackground(Color.Black); // 画面を黒で塗りつぶす
                Raylib.DrawText("TaikoSim Start!", 40, 40, 40, Color.White); // 文字を表示(x, y, 大きさ, 色)
                Raylib.DrawFPS(40, 100);             // FPS(1秒の更新回数)を表示

                Raylib.EndDrawing();                 // 描画終了
            }

            // 後片付け
            Raylib.CloseWindow();
        }
    }
}