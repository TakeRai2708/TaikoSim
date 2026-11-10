using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Graphics;
using TakeTaikoSimulator.Scenes;

namespace TakeTaikoSimulator.Audio;

// 再生処理を分けて、画面をまたぐ引き継ぎを音声DLLなしでも確認できるようにする。
public interface IMenuBgmTrack : IDisposable
{
    double LengthMs { get; }
    void SetLoop(bool loop);
    void Play(double positionMs);
    void Stop();
    void SetVolume(float volume);
}

public sealed class MenuBgmPlayer(Func<string, IMenuBgmTrack?> load) : IDisposable
{
    private IMenuBgmTrack? track;
    private string? currentFile;
    private int skin = -1, audio = -1;
    private double elapsedMs;
    private bool wasMuted;

    public void Update(string? file, int skinGeneration, int audioGeneration, bool muted, float volume,
        float dt, double loopPeriodMs = 0)
    {
        bool changed = file != currentFile || skin != skinGeneration || audio != audioGeneration;
        if (changed)
        {
            // 音の出力を再初期化した後のハンドルは、すでに無効なので操作しない。
            if (audio == audioGeneration) track?.Dispose();
            track = null;
            currentFile = file;
            skin = skinGeneration;
            audio = audioGeneration;
            elapsedMs = 0;
            if (file != null) track = load(file);
            if (track != null)
            {
                track.SetLoop(loopPeriodMs <= 0);
                track.SetVolume(muted ? 0 : volume);
                if (!muted) track.Play(0);
            }
        }
        bool resumed = !changed && wasMuted && !muted;
        if (track != null && !changed && wasMuted != muted)
        {
            if (muted) track.Stop();
            else track.Play(0);
            elapsedMs = 0;
        }
        wasMuted = muted;
        if (!changed && !muted && !resumed) elapsedMs += Math.Max(0, dt) * 1000.0;

        if (track == null) return; // 無い素材を毎フレーム探し直さない。
        if (!muted && loopPeriodMs > 0)
        {
            double period = Math.Max(loopPeriodMs, track.LengthMs);
            if (elapsedMs >= period)
            {
                elapsedMs %= period;
                track.Play(0);
            }
        }
        track.SetVolume(muted ? 0 : Math.Clamp(volume, 0, 1));
    }

    public void Dispose()
    {
        track?.Dispose();
        track = null;
        currentFile = null;
        skin = audio = -1;
    }
}

public static class MenuBgm
{
    public const string EntryFile = "Sound/BGM/Entry.ogg";
    public const string SongSelectFile = "Sound/BGM/SongSelect.ogg";
    // AGENTS.mdの参考動画の測定値。短い選曲ジングルの後には無音区間がある。
    public const double SongSelectLoopMs = 4060;
    private static readonly MenuBgmPlayer player = new(Load);

    private sealed class Track(Song song) : IMenuBgmTrack
    {
        public double LengthMs => song.LengthMs;
        public void SetLoop(bool loop) => song.SetLoop(loop);
        public void Play(double positionMs) => song.Play(positionMs);
        public void Stop() => song.Stop();
        public void SetVolume(float volume) => song.SetVolume(volume);
        public void Dispose() => song.Dispose();
    }

    private static IMenuBgmTrack? Load(string path)
    {
        Song? song = Song.Load(path, out _);
        return song == null ? null : new Track(song);
    }

    // Gameが画面の更新後に呼ぶ。プレビューの開始と同じフレームでBGMを消す。
    public static void Update(Scene? scene, float dt)
    {
        string? path = scene?.MenuBgmFile is string file && SkinManager.CurrentPath is string root && AudioManager.IsAvailable
            ? Path.Combine(root, file) : null;
        var config = ConfigManager.Sound;
        player.Update(path, SkinManager.Generation, AudioManager.Generation, scene?.MenuBgmMuted ?? false,
            config.GetFinalVolume(config.BgmVolume), dt, scene?.MenuBgmLoopMs ?? 0);
    }

    public static void Shutdown()
    {
        player.Update(null, SkinManager.Generation, AudioManager.Generation, true, 0, 0);
        player.Dispose();
    }
}
