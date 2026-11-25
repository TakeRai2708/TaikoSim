using Raylib_cs;
using TakeTaikoSimulator.Audio;
using TakeTaikoSimulator.Charts;
using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Graphics;
using TakeTaikoSimulator.Input;
using TakeTaikoSimulator.UI;
using static TakeTaikoSimulator.UI.MenuDraw;

namespace TakeTaikoSimulator.Scenes;

// 通常演奏を2つ動かす。音源は1つだけにして、両方の譜面を同じ時計で進める。
public sealed class LocalPlayScene : Scene
{
    public override Platform.DiscordPresence.Info? Presence => Platform.DiscordPresence.ShowSongTitle
        ? new(song.Title, "2人で演奏中")
        : new("2人で演奏中");

    private readonly SongEntry song;
    private readonly PlayScene[] players;

    // 曲を鳴らしているのは 1P の側だけなので、両方に伝えても音が重なることはない
    public override void ApplyVolume()
    {
        foreach (PlayScene player in players) player.ApplyVolume();
    }
    private bool finished;
    private double startTime;
    private string error = "";
    public override bool CanOpenSettings => false;

    public LocalPlayScene(SongEntry song, CourseType first, CourseType second)
    {
        this.song = song;
        players = new[] { new PlayScene(song.TjaPath, first, true), new PlayScene(song.TjaPath, second, true) };
        players[0].ConfigureLocal(0, true, null);
        players[1].ConfigureLocal(1, false, () => players[0].LocalTime, () => players[0].LocalInputClock);
    }

    public override void Enter()
    {
        for (int p = 0; p < 2; p++)
        {
            int side = p;
            LocalSession.WithOptions(side, players[side].Enter);
            if (players[p].LocalError.Length > 0) error = players[p].LocalError;
        }
        // 異なる難易度の先頭小節が負の時刻でも、同じ開始位置に揃える。
        startTime = Math.Min(players[0].LocalTime, players[1].LocalTime);
        Restart();
    }

    private void Restart()
    {
        for (int p = 0; p < 2; p++)
        {
            int side = p;
            LocalSession.WithOptions(side, () => players[side].RestartLocal(startTime));
        }
    }

    public override void Exit()
    {
        for (int p = 0; p < 2; p++) LocalSession.WithOptions(p, players[p].Exit);
    }

    public override void Update(float dt)
    {
        if (finished)
        {
            if (InputManager.IsDonPressed(0) || InputManager.IsDonPressed(1) || Raylib.IsKeyPressed(KeyboardKey.Enter)) OnBack();
            return;
        }
        if (error.Length > 0) return;
        // Raylibの入力キューは1回だけ読み、各演奏側で自分のキーだけ判定する。
        int key;
        while ((key = Raylib.GetKeyPressed()) != 0)
            foreach (var player in players) player.EnqueueLocalKey(key);
        if (InputManager.IsSystemPressed(SystemAction.QuickRestart))
        {
            Restart();
            return;
        }
        for (int p = 0; p < 2; p++) LocalSession.WithOptions(p, () => players[p].Update(dt));
        if (players.Any(p => p.LocalResult == null)) return;
        finished = true;
        players[0].StopLocalMusic();
        // プロフィールの切り替えは記録時だけ。失敗時も操作側のデータに戻す。
        try
        {
            for (int p = 0; p < 2; p++)
            {
                try
                {
                    SongHistory.SelectProfile(LocalSession.Profiles[p]);
                    SongHistory.AddPlayed(song.Key);
                    LocalSession.WithOptions(p, players[p].SaveLocalRecord);
                    if (players[p].LocalResult?.RecordUpdate is SongRecordUpdate update && !update.Saved)
                        error += $" {p + 1}Pの記録を保存できませんでした。";
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                { error += $" {p + 1}Pの記録を保存できませんでした：" + ex.Message; }
            }
        }
        finally
        {
            try { SongHistory.SelectProfile(LocalSession.Profiles[LocalSession.Controller]); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            { error += " データの再読み込みに失敗しました：" + ex.Message; }
        }
    }

    public override void OnBack() => SceneManager.Simple(new SongSelectScene(afterPlay: true));

    public override void Draw()
    {
        Raylib.ClearBackground(new Color(10, 17, 28, 255));
        Outlined(finished ? "演奏結果" : "2P 演奏テスト", new(200, 40, 1520, 90), 50, Color.White, Color.Black);
        for (int p = 0; p < 2; p++)
        {
            Outlined($"{p + 1}P　{SongHistory.ProfileName(LocalSession.Profiles[p])}", new(p * 960 + 40, 170, 880, 70), 38,
                p == 0 ? Color.Red : Color.SkyBlue, Color.Black);
            if (finished && players[p].LocalResult is PlayResultData result)
            {
                NamePlate.Draw(LocalSession.Profiles[p], p, p * 960 + 40, 825, 1.1f);
                string[] lines = { Chart.GetCourseName(result.Course), result.Result switch { PlayResult.DondaFullCombo => "ドンダフルコンボ", PlayResult.FullCombo => "フルコンボ", PlayResult.Clear => "クリア", _ => "クリア失敗" }, $"{result.Stats.Score:N0} 点",
                    $"良 {result.Good}　可 {result.Ok}　不可 {result.Bad}", $"連打 {result.RollHits}　最大コンボ {result.MaxCombo}" };
                for (int i = 0; i < lines.Length; i++) Outlined(lines[i], new(p * 960 + 40, 320 + i * 100, 880, 75), 38, Color.White, Color.Black);
            }
            else
            {
                // キャンバスを入れ子にせず、既存の演奏を縦横同じ倍率で並べる。
                Screen.BeginScissorMode(p * 960, 270, 960, 540);
                Rlgl.PushMatrix();
                Rlgl.Translatef(p * 960, 270, 0);
                Rlgl.Scalef(.5f, .5f, 1);
                LocalSession.WithOptions(p, players[p].Draw);
                Rlgl.PopMatrix();
                Screen.EndScissorMode();
            }
        }
        Outlined(error.Length > 0 ? error : finished ? "ドン・Enter・Esc：選曲へ" : "Esc：演奏をやめる　F5：2人とも最初から", new(40, 940, 1840, 90), 30, Color.White, Color.Black);
    }
}

