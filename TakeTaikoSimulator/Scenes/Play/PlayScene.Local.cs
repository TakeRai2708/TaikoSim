using Raylib_cs;
using TakeTaikoSimulator.Charts;

namespace TakeTaikoSimulator.Scenes;

public partial class PlayScene
{
    private bool localPlay, localLeader;
    private int localPlayer;
    private Func<double>? localClock;
    private Func<long>? localInputClock;
    internal long LocalInputClock { get; private set; }
    private readonly Queue<int> localKeys = new();
    internal PlayResultData? LocalResult { get; private set; }
    internal string LocalError => error;
    internal double LocalTime => nowMs;

    internal void ConfigureLocal(int player, bool leader, Func<double>? clock, Func<long>? inputClock = null)
    {
        localPlay = true;
        localPlayer = player;
        localLeader = leader;
        localClock = clock;
        localInputClock = inputClock;
    }

    internal void EnqueueLocalKey(int key) { if (LocalResult == null) localKeys.Enqueue(key); }
    private int NextLocalKey() => localPlay ? (localKeys.TryDequeue(out int key) ? key : 0) : Raylib.GetKeyPressed();

    internal void RestartLocal(double start)
    {
        LocalResult = null;
        localKeys.Clear();
        Restart();
        nowMs = start;
    }

    private void FinishLocal()
    {
        LocalResult ??= new PlayResultData
        {
            Title = chart?.Title ?? "", Course = course?.Type ?? CourseType.Oni,
            Result = end.Result, Stats = stats, Good = goodCount, Ok = okCount,
            Bad = badCount, RollHits = totalRollHits, MaxCombo = maxCombo,
        };
    }

    internal void StopLocalMusic() => song?.Stop();

    internal void SaveLocalRecord()
    {
        if (LocalResult == null || autoPlay || course == null) return;
        SongClearType clear = end.Result switch
        {
            PlayResult.DondaFullCombo => SongClearType.AllPerfect,
            PlayResult.FullCombo => SongClearType.FullCombo,
            PlayResult.Clear => SongClearType.Cleared,
            _ => SongClearType.None,
        };
        LocalResult.RecordUpdate = SongHistory.SaveRecord(SongHistory.KeyFor(path), course.Type,
            clear, stats.Score, stats.ScoreRank + 1);
    }
}
