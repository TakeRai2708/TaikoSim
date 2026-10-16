using System.Numerics;
using Raylib_cs;
using TaikoSim.Configs;
using TaikoSim.Graphics;

namespace TaikoSim.Input
{
    // キー入力の判定係(設定されたキーを見て判定する)
    public static class InputManager
    {
        // 叩く場所が押された瞬間か(登録されたキーのどれか1つでもOK)
        public static bool IsDrumPressed(DrumPart part)
        {
            if (!ConfigManager.Key.DrumKeys.TryGetValue(part, out List<KeyboardKey>? keys)) return false;

            foreach (KeyboardKey key in keys)
            {
                if (Raylib.IsKeyPressed(key)) return true;
            }
            return false;
        }

        // ドン(左面か右面)が押された瞬間か
        public static bool IsDonPressed()
        {
            return IsDrumPressed(DrumPart.LeftDon) || IsDrumPressed(DrumPart.RightDon);
        }

        // カッ(左縁か右縁)が押された瞬間か
        public static bool IsKaPressed()
        {
            return IsDrumPressed(DrumPart.LeftKa) || IsDrumPressed(DrumPart.RightKa);
        }

        // システムキーが押された瞬間か(未割り当てなら常に false)
        public static bool IsSystemPressed(SystemAction action)
        {
            if (!ConfigManager.Key.SystemKeys.TryGetValue(action, out KeyboardKey key)) return false;
            if (key == KeyboardKey.Null) return false;
            return Raylib.IsKeyPressed(key);
        }

        // 戻るキー(Esc)が押された瞬間か
        public static bool IsBackPressed()
        {
            return Raylib.IsKeyPressed(KeyConfig.BackKey);
        }

        // マウスの位置(1920×1080の座標)
        public static Vector2 MousePosition => Screen.GetMousePosition();
    }
}