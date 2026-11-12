using System.Numerics;
using Raylib_cs;
using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Graphics;

namespace TakeTaikoSimulator.Input
{
    // キー入力の判定係(設定されたキーを見て判定する)
    public static class InputManager
    {
        // 叩く場所が押された瞬間か(登録されたキーのどれか1つでもOK)
        public static bool IsDrumPressed(DrumPart part) => IsDrumPressed(part, TakeTaikoSimulator.Scenes.LocalSession.MenuPlayer);
        //   player は「側」（0 = ひだり、1 = みぎ）。どのキーの組を叩くかは LocalSession.KeySets が決める
        public static bool IsDrumPressed(DrumPart part, int player) => IsKeySetPressed(part, TakeTaikoSimulator.Scenes.LocalSession.KeySets[player]);

        // キーの組（0 = 設定の1Pのキー、1 = 2Pのキー）を直接見る。まだ側が決まっていない人（エントリー・途中参加）用
        public static bool IsKeySetPressed(DrumPart part, int keySet)
        {
            part = (DrumPart)((int)part % 4 + keySet * 4);
            if (!ConfigManager.Key.DrumKeys.TryGetValue(part, out List<KeyboardKey>? keys)) return false;

            foreach (KeyboardKey key in keys)
            {
                if (Raylib.IsKeyPressed(key)) return true;
            }
            return false;
        }

        public static bool IsKeySetDonPressed(int keySet) => IsKeySetPressed(DrumPart.LeftDon, keySet) || IsKeySetPressed(DrumPart.RightDon, keySet);

        // ドン(左面か右面)が押された瞬間か
        public static bool IsDonPressed()
        {
            return IsDrumPressed(DrumPart.LeftDon) || IsDrumPressed(DrumPart.RightDon);
        }

        public static bool IsDonPressed(int player) => IsDrumPressed(DrumPart.LeftDon, player) || IsDrumPressed(DrumPart.RightDon, player);

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
