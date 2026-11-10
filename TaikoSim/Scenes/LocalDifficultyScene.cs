using Raylib_cs;
using TaikoSim.Audio;
using TaikoSim.Charts;
using TaikoSim.Configs;
using TaikoSim.Input;
using TaikoSim.Graphics;
using TaikoSim.UI;
using static TaikoSim.UI.MenuDraw;

namespace TaikoSim.Scenes;

// 同じ曲で難易度とオプションを別々に決める、2P用のテスト画面。
public sealed class LocalDifficultyScene(SongEntry song) : Scene
{
    private readonly CourseType[] courses = song.Courses.Select(c => c.Type)
        .Where(c => c <= CourseType.Edit).Distinct().OrderBy(c => c).ToArray();
    private readonly int[] cursor = new int[2];
    private readonly bool[] ready = new bool[2];
    private readonly MenuChara chara = new();
    private readonly PlayOptionsPanel options = new();
    private int optionPlayer = -1;
    private int focus = LocalSession.Controller;
    private float age;
    private bool leaving;

    public override void Enter()
    {
        chara.Load();
        if (CharaBaker.Config is CharaConfig cc) chara.Loop("songselect_loop", cc.SongSelectLoopTime);
        int oni = Array.IndexOf(courses, CourseType.Oni);
        for (int p = 0; p < 2; p++) cursor[p] = Math.Max(0, oni >= 0 ? oni : courses.Length - 1);
    }

    public override void Exit()
    {
        chara.Dispose();
        if (optionPlayer >= 0) LocalSession.WithOptions(optionPlayer, options.CloseNow);
    }

    public override void Update(float dt)
    {
        age += dt;
        chara.Update(dt);
        if (leaving || Game.IsSettingsOpen) return;
        if (optionPlayer >= 0)
        {
            LocalSession.WithOptions(optionPlayer, () => options.Update(dt));
            if (InputManager.IsSystemPressed(SystemAction.QuickOption))
                LocalSession.WithOptions(optionPlayer, options.Close);
            if (!options.IsOpen) optionPlayer = -1;
            return;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Tab)) focus = 1 - focus;
        if (InputManager.IsSystemPressed(SystemAction.QuickOption)) { OpenOptions(focus); return; }
        // 曲決定に使ったドンを、そのまま難易度の決定にも使わない。
        if (age < .25f) return;
        for (int p = 0; p < 2; p++)
        {
            bool left = InputManager.IsDrumPressed(DrumPart.LeftKa, p) || p == focus && Raylib.IsKeyPressed(KeyboardKey.Left);
            bool right = InputManager.IsDrumPressed(DrumPart.RightKa, p) || p == focus && Raylib.IsKeyPressed(KeyboardKey.Right);
            if (left || right)
            {
                ready[p] = false;
                cursor[p] = Math.Clamp(cursor[p] + (right ? 1 : -1), 0, courses.Length);
                MenuSounds.Ka();
            }
            if (InputManager.IsDonPressed(p) || p == focus && Raylib.IsKeyPressed(KeyboardKey.Enter))
            {
                if (cursor[p] == courses.Length) { OpenOptions(p); return; }
                ready[p] = true;
                MenuSounds.Don();
            }
        }
        if (ready.All(r => r))
        {
            leaving = true;
            SceneManager.ToPlayWithLoading(new LocalPlayScene(song, courses[cursor[0]], courses[cursor[1]]), song.Title, song.Subtitle);
        }
    }

    public override void UpdateBlocked(float dt) => chara.Update(dt);

    private void OpenOptions(int player)
    {
        ready[player] = false;
        optionPlayer = focus = player;
        LocalSession.WithOptions(player, options.Open);
    }

    public override void OnBack()
    {
        if (leaving) return;
        if (optionPlayer >= 0) { LocalSession.WithOptions(optionPlayer, options.Close); return; }
        if (ready.Any(r => r)) { Array.Clear(ready); return; }
        SceneManager.Simple(new SongSelectScene());
    }

    public override void Draw()
    {
        Gui.BeginFrame();
        Raylib.ClearBackground(new Color(12, 20, 33, 255));
        if (CharaBaker.Config is CharaConfig cc) chara.DrawPlayers(cc.SongSelectX, cc.SongSelectY, cc.SongSelectScale, LocalSession.Players);
        Outlined(song.Title, new(80, 50, 1760, 110), 56, Color.White, Color.Black);
        for (int p = 0; p < 2; p++)
        {
            float x = 400 + p * 590;
            Color accent = p == 0 ? Color.Red : Color.SkyBlue;
            Outlined($"{p + 1}P　{SongHistory.ProfileName(LocalSession.Profiles[p])}", new(x, 180, 530, 70), 38, accent, Color.Black);
            for (int i = 0; i <= courses.Length; i++)
            {
                Rectangle row = new(x, 280 + i * 85, 530, 70);
                bool selected = cursor[p] == i;
                Raylib.DrawRectangleRec(row, selected ? accent : new Color(30, 43, 62, 255));
                string label = i == courses.Length ? "演奏オプション" : Chart.GetCourseName(courses[i]) + $"　★{song.Courses.First(c => c.Type == courses[i]).Level}";
                Rectangle text = i == courses.Length ? row : new(row.X, row.Y, row.Width - 195, row.Height);
                Outlined(label, text, 30, Color.White, Color.Black);
                if (i < courses.Length) SongRecordPanel.DrawCard(song.Key, courses[i], new(row.X + row.Width - 196, row.Y, 196, 354), 1, LocalSession.Profiles[p]);
                if (!leaving && !Game.IsSettingsOpen && optionPlayer < 0 && Gui.Clicked(row))
                {
                    focus = p;
                    ready[p] = false;
                    if (i == courses.Length) OpenOptions(p);
                    else if (selected) ready[p] = true;
                    else cursor[p] = i;
                }
            }
            SongRecordPanel.DrawSummary(ConfigManager.Game.RecordGroup, 1, profile: LocalSession.Profiles[p], side: p);
            if (cursor[p] < courses.Length) SongRecordPanel.DrawBest(song, courses[cursor[p]], 1, LocalSession.Profiles[p], p);
            var cfg = LocalSession.Options[p];
            Outlined($"速さ ×{cfg.HiSpeed:0.0}　オート {(cfg.AutoPlay ? "ON" : "OFF")}", new(x, 820, 530, 50), 28, Color.White, Color.Black);
            Outlined(ready[p] ? "準備完了" : "難易度を選んでドン", new(x, 885, 530, 60), 38, ready[p] ? Color.Lime : Color.White, Color.Black);
        }
        Outlined($"カッ：選択　ドン：決定　Esc：解除・戻る　Tab：キーボード操作側（{focus + 1}P）　F2：オプション", new(40, 1000, 1840, 50), 27, Color.White, Color.Black);
        if (optionPlayer >= 0)
        {
            Raylib.DrawRectangle(0, 0, 1920, 1080, new Color(0, 0, 0, 130));
            Outlined($"{optionPlayer + 1}P の演奏オプション", new(500, 150, 920, 80), 44, Color.White, Color.Black);
            LocalSession.WithOptions(optionPlayer, () => options.Draw(!Game.IsSettingsOpen));
        }
    }
}
