using TakeTaikoSimulator.Audio;

internal sealed class FakeMenuBgm : IMenuBgmTrack
{
    public double LengthMs { get; set; } = 3000;
    public int Plays, Stops, Disposes;
    public float Volume;
    public bool Loop;
    public bool IsPlaying { get; set; }
    public double LastPositionMs { get; private set; }
    public void SetLoop(bool loop) => Loop = loop;
    public void Play(double positionMs) { Plays++; IsPlaying = true; LastPositionMs = positionMs; }
    public void Stop() { Stops++; IsPlaying = false; }
    public void SetVolume(float volume) => Volume = volume;
    public void Dispose() => Disposes++;
}
