using System.Numerics;
using Raylib_cs;

namespace TakeTaikoSimulator.UI
{
    // 設定パネルの左に並ぶアイコン(図形で描く仮のもの)
    // (x, y) がアイコンの中心
    public static class Icons
    {
        // 歯車(一般設定)
        public static void Gear(float x, float y, Color color, Color background)
        {
            for (int i = 0; i < 6; i++)
            {
                Raylib.DrawRectanglePro(new Rectangle(x, y, 9, 46), new Vector2(4.5f, 23f), i * 30f, color);
            }
            Raylib.DrawCircleV(new Vector2(x, y), 17, color);
            Raylib.DrawCircleV(new Vector2(x, y), 7, background); // 真ん中の穴
        }

        // モニター(ウィンドウ設定)
        public static void Monitor(float x, float y, Color color)
        {
            Raylib.DrawRectangleLinesEx(new Rectangle(x - 24, y - 19, 48, 32), 4, color);
            Raylib.DrawRectangleRec(new Rectangle(x - 3, y + 13, 6, 8), color);
            Raylib.DrawRectangleRec(new Rectangle(x - 13, y + 20, 26, 4), color);
        }

        // スピーカー(サウンド設定)
        public static void Speaker(float x, float y, Color color)
        {
            Raylib.DrawRectangleRec(new Rectangle(x - 22, y - 8, 10, 16), color);
            Raylib.DrawPoly(new Vector2(x - 9, y), 3, 14, 180f, color);
            Raylib.DrawRing(new Vector2(x + 2, y), 9, 12, -40, 40, 16, color);
            Raylib.DrawRing(new Vector2(x + 2, y), 17, 20, -40, 40, 16, color);
        }

        // 太鼓(プレイ設定)
        public static void Drum(float x, float y, Color color)
        {
            Raylib.DrawRing(new Vector2(x, y), 17, 22, 0, 360, 36, color);
            Raylib.DrawCircleV(new Vector2(x, y), 11, color);
        }

        // キーボード(キー設定)
        public static void Keyboard(float x, float y, Color color)
        {
            Raylib.DrawRectangleLinesEx(new Rectangle(x - 25, y - 15, 50, 30), 3, color);
            for (int row = 0; row < 2; row++)
            {
                for (int col = 0; col < 5; col++)
                {
                    Raylib.DrawRectangleRec(new Rectangle(x - 19 + col * 8, y - 9 + row * 7, 5, 4), color);
                }
            }
            Raylib.DrawRectangleRec(new Rectangle(x - 12, y + 6, 24, 4), color); // スペースキー
        }

        // 筆(スキン設定)
        public static void Brush(float x, float y, Color color)
        {
            Raylib.DrawRectanglePro(new Rectangle(x + 3, y - 3, 7, 38), new Vector2(3.5f, 19f), 45f, color);
            Raylib.DrawCircleV(new Vector2(x - 11, y + 11), 8, color);
        }

        // 電源(終了)
        public static void Power(float x, float y, Color color)
        {
            Raylib.DrawRing(new Vector2(x, y), 15, 20, -50, 230, 32, color);
            Raylib.DrawRectangleRec(new Rectangle(x - 3, y - 24, 6, 22), color);
        }
    }
}