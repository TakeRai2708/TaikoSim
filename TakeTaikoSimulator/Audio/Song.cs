using ManagedBass;
using ManagedBass.Mix;
using TakeTaikoSimulator.Diagnostics;

namespace TakeTaikoSimulator.Audio
{
    // 曲1つ分(.tja の WAVE: のファイル)
    // 使い終わったら Dispose() で片付ける
    // ※ Raylib にも「Music」があって名前がぶつかるので「Song」にしている
    //
    // 鳴らし方は2通り(AudioManager.Mixer で決まる)
    //   ・BASS / DirectSound … BASS に直接鳴らしてもらう(今まで通り)
    //   ・WASAPI / ASIO      … ミキサーに入れておいて、一時停止を外して鳴らす(OpenTaiko と同じ)
    public sealed class Song : IDisposable
    {
        // BASSが曲を区別するための番号(0 なら読み込めていない)
        private int handle;
        private readonly double originalFrequency;
        private double playbackRate = 1.0;

        // ミキサーに入れて鳴らしているか(読み込んだときに決まる)
        private readonly bool inMixer;

        // ミキサーのとき：鳴らしている途中か(止めたら false)
        private bool mixerPlaying = false;

        // 曲の長さ(ミリ秒)
        public double LengthMs { get; }

        private Song(int handle, bool inMixer)
        {
            this.handle = handle;
            this.inMixer = inMixer;
            Bass.ChannelGetAttribute(handle, ChannelAttribute.Frequency, out float frequency);
            originalFrequency = frequency;
            LengthMs = Bass.ChannelBytes2Seconds(handle, Bass.ChannelGetLength(handle)) * 1000.0;
        }

        // 曲を読み込む。失敗したら null を返して、理由を error に入れる
        //   デバッグ記録が ON なら、かかった時間を events.csv に書く
        public static Song? Load(string path, out string error)
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            Song? song = LoadCore(path, out error);
            double ms = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            DebugLog.Event("曲の読み込み", song != null
                ? $"{Path.GetFileName(path)}（{song.LengthMs / 1000.0:F1}秒）"
                : $"{Path.GetFileName(path)} を読めなかった：{error}", ms);
            return song;
        }

        private static Song? LoadCore(string path, out string error)
        {
            if (!AudioManager.IsAvailable)
            {
                error = $"音が使えません：{AudioManager.Status}";
                return null;
            }
            if (!File.Exists(path))
            {
                error = $"曲ファイルが見つかりません：{Path.GetFileName(path)}";
                return null;
            }

            int mixer = AudioManager.Mixer;

            // ----- BASS / DirectSound：今まで通り -----
            if (mixer == 0)
            {
                // Prescan = mp3 の長さや位置を正確にする(読み込みが少し遅くなるかわりにズレにくい)
                int direct = Bass.CreateStream(path, 0, 0, BassFlags.Prescan);
                if (direct == 0)
                {
                    error = $"曲を読み込めませんでした（{Bass.LastError}）";
                    return null;
                }
                error = "";
                return new Song(direct, inMixer: false);
            }

            // ----- WASAPI / ASIO：ミキサーに入れる -----
            // Decode = 自分では鳴らさない(ミキサーが読みに来る)。Float = ミキサーと同じ形
            int stream = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan);
            if (stream == 0)
            {
                error = $"曲を読み込めませんでした（{Bass.LastError}）";
                return null;
            }

            // MixerChanPause = 一時停止の状態で入れておく(Play で外す)
            // MixerChanNoRampin = 鳴り始めをフェードしない(頭がちゃんと聞こえるように)
            if (!BassMix.MixerAddChannel(mixer, stream, BassFlags.MixerChanPause | BassFlags.MixerChanNoRampin))
            {
                error = $"曲をミキサーに入れられませんでした（{Bass.LastError}）";
                Bass.StreamFree(stream);
                return null;
            }

            error = "";
            return new Song(stream, inMixer: true);
        }

        // 今どこが聞こえているか(ミリ秒)
        // ミキサーのときは「ミキサーから出したところ − まだ鳴っていない分(出力の遅延)」
        public double PositionMs
        {
            get
            {
                if (handle == 0) return 0;
                if (!inMixer)
                {
                    // 通常の再生チャンネルは、BASSがバッファを考慮した再生位置を返す。
                    // デコード位置ではないので、機器の遅れやバッファをもう一度引かない。
                    return Bass.ChannelBytes2Seconds(handle, Bass.ChannelGetPosition(handle)) * 1000.0;
                }

                double mixedMs = Bass.ChannelBytes2Seconds(handle, BassMix.ChannelGetPosition(handle)) * 1000.0;
                return mixerPlaying ? mixedMs - AudioManager.OutputLatencyMs * playbackRate : mixedMs;
            }
        }

        // 鳴っている途中か(最後まで鳴り終わったら false)
        public bool IsPlaying
        {
            get
            {
                if (handle == 0) return false;
                if (!inMixer) return Bass.ChannelIsActive(handle) == PlaybackState.Playing;

                // ミキサーの中の曲は「最後まで行ったか」を位置で見る
                double mixedMs = Bass.ChannelBytes2Seconds(handle, BassMix.ChannelGetPosition(handle)) * 1000.0;
                return mixerPlaying && mixedMs < LengthMs;
            }
        }

        // 特訓用。再生周波数を変えるので、速度と一緒に音程も下がる。
        public bool SetPlaybackRate(double rate)
        {
            rate = Math.Clamp(rate, 0.5, 1.0);
            if (handle == 0 || originalFrequency <= 0) return false;
            if (!Bass.ChannelSetAttribute(handle, ChannelAttribute.Frequency, originalFrequency * rate)) return false;
            playbackRate = rate;
            return true;
        }

        // 音量(0〜1)。ミキサーの中の曲も、この設定で混ぜてくれる
        public void SetVolume(float volume)
        {
            if (handle == 0) return;
            Bass.ChannelSetAttribute(handle, ChannelAttribute.Volume, Math.Clamp(volume, 0f, 1f));
        }

        // 最後まで行ったら頭に戻って、ずっと鳴らすか(リザルト画面などの BGM 用)
        public void SetLoop(bool loop)
        {
            if (handle == 0) return;
            Bass.ChannelFlags(handle, loop ? BassFlags.Loop : BassFlags.Default, BassFlags.Loop);
        }

        // startMs の位置から鳴らす
        public void Play(double startMs)
        {
            if (handle == 0) return;
            long bytes = Bass.ChannelSeconds2Bytes(handle, Math.Max(0, startMs) / 1000.0);

            if (!inMixer)
            {
                Bass.ChannelSetPosition(handle, bytes);
                Bass.ChannelPlay(handle);
                return;
            }

            // ミキサーの中の曲は BassMix の方で位置を変える(ミキサーにたまっている分も捨ててくれる)
            BassMix.ChannelSetPosition(handle, bytes);
            BassMix.ChannelRemoveFlag(handle, BassFlags.MixerChanPause);
            mixerPlaying = true;
        }

        // 止める
        public void Stop()
        {
            if (handle == 0) return;

            if (!inMixer)
            {
                Bass.ChannelStop(handle);
                return;
            }

            BassMix.ChannelAddFlag(handle, BassFlags.MixerChanPause);
            mixerPlaying = false;
        }

        // 片付け(何回呼んでも大丈夫)
        public void Dispose()
        {
            if (handle == 0) return;
            if (inMixer) BassMix.MixerRemoveChannel(handle);
            Bass.StreamFree(handle);
            handle = 0;
            mixerPlaying = false;
        }
    }
}
