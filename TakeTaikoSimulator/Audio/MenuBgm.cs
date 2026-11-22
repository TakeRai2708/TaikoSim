using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Graphics;
using TakeTaikoSimulator.Scenes;

namespace TakeTaikoSimulator.Audio;

// 再生処理を分けて、画面をまたぐ引き継ぎを音声DLLなしでも確認できるようにする。
public interface IMenuBgmTrack : IDisposable
{
    double LengthMs { get; }
    bool IsPlaying { get; }
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
    private double sincePlayMs;
    private bool wasMuted;

    // くり返し方は3つ：
    //   ・loopPeriodMs > 0：その長さごとに、頭から鳴らし直す（前からある作り）
    //   ・introMs > 0 か loopEndMs > 0：最後（loopEndMs。0 なら曲の終わり）まで行ったら、introMs の所に戻る
    //       11/12 たけらい「BGM がループしてるけど切れてるとこがおかしい」
    //       TaikøNauts のスキンの SkinConfig.json に entryBGMIntroTimeMs（A-Style は 2500）・songSelectBGMIntroTimeMs（1000）がある。
    //       曲の頭の「イントロ」は最初の1回だけで、2周目からはイントロのあとをくり返す、という作りの曲だった
    //       （前は、エントリーは毎回イントロから、選曲は 4.06秒ごとに頭から鳴らしていた）
    //   ・どちらも 0：曲をそのままくり返す
    public void Update(string? file, int skinGeneration, int audioGeneration, bool muted, float volume,
        float dt, double loopPeriodMs = 0, double introMs = 0, double loopEndMs = 0)
    {
        bool introLoop = loopPeriodMs <= 0 && (introMs > 0 || loopEndMs > 0);
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
                track.SetLoop(loopPeriodMs <= 0 && !introLoop);
                track.SetVolume(muted ? 0 : volume);
                if (!muted) Play(0);
            }
        }
        bool resumed = !changed && wasMuted && !muted;
        if (track != null && !changed && wasMuted != muted)
        {
            if (muted) track.Stop();
            else Play(0);
            elapsedMs = 0;
        }
        wasMuted = muted;
        if (!changed && !muted && !resumed)
        {
            double step = Math.Max(0, dt) * 1000.0;
            elapsedMs += step;
            sincePlayMs += step;
        }

        if (track == null) return; // 無い素材を毎フレーム探し直さない。
        if (!muted && loopPeriodMs > 0)
        {
            double period = Math.Max(loopPeriodMs, track.LengthMs);
            if (elapsedMs >= period)
            {
                elapsedMs %= period;
                Play(0);
            }
        }
        if (!muted && introLoop)
        {
            double end = loopEndMs > 0 ? Math.Min(loopEndMs, track.LengthMs) : track.LengthMs;
            double start = Math.Clamp(introMs, 0, Math.Max(0, end - 100));
            if (end > 0 && elapsedMs >= end)
            {
                // 行きすぎた分だけ先から鳴らす（コマの長さの分、音がずれないように）
                elapsedMs = start + (elapsedMs - end) % (end - start);
                Play(elapsedMs);
            }
            else if (sincePlayMs >= 100 && !track.IsPlaying)
            {
                elapsedMs = start;
                Play(start);
            }
        }
        if (!muted && loopPeriodMs <= 0 && !introLoop && sincePlayMs >= 100 && !track.IsPlaying)
        {
            elapsedMs = 0;
            Play(0);
        }
        track.SetVolume(muted ? 0 : Math.Clamp(volume, 0, 1));
    }

    private void Play(double positionMs)
    {
        track!.Play(positionMs);
        sincePlayMs = 0;
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
        public bool IsPlaying => song.IsPlaying;
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
        // スキンの SkinConfig.json にイントロの長さが書いてあれば、そちらのくり返し方を使う
        bool entry = scene?.MenuBgmFile == EntryFile;
        double intro = entry ? SkinManager.EntryBgmIntroMs : SkinManager.SongSelectBgmIntroMs;
        double loopEnd = entry ? SkinManager.EntryBgmLoopEndMs : SkinManager.SongSelectBgmLoopEndMs;
        bool introLoop = intro > 0 || loopEnd > 0;
        player.Update(path, SkinManager.Generation, AudioManager.Generation, scene?.MenuBgmMuted ?? false,
            config.GetFinalVolume(config.BgmVolume), dt, introLoop ? 0 : scene?.MenuBgmLoopMs ?? 0, intro, loopEnd);
    }

    public static void Shutdown()
    {
        player.Update(null, SkinManager.Generation, AudioManager.Generation, true, 0, 0);
        player.Dispose();
    }
}
