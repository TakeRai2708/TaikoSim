using TaikoSim.Audio;

internal sealed class FakeMenuBgm : IMenuBgmTrack
{
    public double LengthMs { get; set; } = 3000;
    public int Plays, Stops, Disposes;
    public float Volume;
    public bool Loop;
    public void SetLoop(bool loop) => Loop = loop;
    public void Play(double positionMs) => Plays++;
    public void Stop() => Stops++;
    public void SetVolume(float volume) => Volume = volume;
    public void Dispose() => Disposes++;
}
