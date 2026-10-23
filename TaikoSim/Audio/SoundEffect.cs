using ManagedBass;
using ManagedBass.Mix;

namespace TaikoSim.Audio
{
    // 効果音1つ分(ドン・カッなど、短くて何回も重ねて鳴らす音)
    // 曲(Song)と違って、全部メモリに読み込んでおいて、鳴らすたびに新しく再生する
    //
    // 鳴らし方は2通り(AudioManager.Mixer で決まる)
    //   ・BASS / DirectSound … BASS の「サンプル」で鳴らす(今まで通り)
    //   ・WASAPI / ASIO      … サンプルが使えないので、同じ音を MaxOverlap 個ミキサーに入れておいて、順番に使う
    public sealed class SoundEffect : IDisposable
    {
        // 同時に重ねて鳴らせる数(連打で速く叩いても音が途切れないように)
        // 8 だと、同じ音(ドンだけ・カッだけ)を速く続けて叩いたときに足りなくなって、音が鳴らなかった
        private const int MaxOverlap = 32;

        // ----- BASS / DirectSound のとき -----
        // BASSが効果音を区別するための番号(0 なら使っていない)
        private int sample;

        // 鳴らせなかったことをログに書いたか(何回も書かないように)
        private bool playFailLogged = false;

        // ----- WASAPI / ASIO のとき -----
        // ミキサーに入れた同じ音たち(一時停止の状態で待っている)
        private int[] voices = Array.Empty<int>();

        // 次に使う番号(順番に使う＝全部鳴っていたら、一番前から鳴っている音を止めて鳴らすのと同じ)
        private int nextVoice = 0;

        private SoundEffect(int sample)
        {
            this.sample = sample;
        }

        private SoundEffect(int[] voices)
        {
            this.voices = voices;
        }

        // 効果音を読み込む。失敗したら null を返して、理由を error に入れる
        //   maxOverlap … 同じ音を同時に重ねて鳴らせる数(ボイスや演奏後の音のように、重ならない長い音は 1〜2 でよい)
        public static SoundEffect? Load(string path, out string error, int maxOverlap = MaxOverlap)
        {
            maxOverlap = Math.Clamp(maxOverlap, 1, MaxOverlap);
            if (!AudioManager.IsAvailable)
            {
                error = $"音が使えません：{AudioManager.Status}";
                return null;
            }
            if (!File.Exists(path))
            {
                error = $"効果音が見つかりません：{Path.GetFileName(path)}";
                return null;
            }

            int mixer = AudioManager.Mixer;

            // ----- BASS / DirectSound：今まで通りサンプルで -----
            if (mixer == 0)
            {
                // 全部鳴っているときは、一番前から鳴っている音を止めて、新しい音を鳴らす
                int sample = Bass.SampleLoad(path, 0, 0, maxOverlap, BassFlags.SampleOverrideLongestPlaying);
                if (sample == 0)
                {
                    error = $"効果音を読み込めませんでした（{Bass.LastError}）";
                    return null;
                }
                error = "";
                return new SoundEffect(sample);
            }

            // ----- WASAPI / ASIO：同じ音を maxOverlap 個、ミキサーに入れておく -----
            byte[] data;
            try
            {
                data = File.ReadAllBytes(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                error = $"効果音を読み込めませんでした（{ex.Message}）";
                return null;
            }

            int[] voices = new int[maxOverlap];
            for (int i = 0; i < maxOverlap; i++)
            {
                // メモリの音から作る(ManagedBass がメモリを固定しておいてくれる)
                int stream = Bass.CreateStream(data, 0, data.Length, BassFlags.Decode | BassFlags.Float);
                if (stream == 0 || !BassMix.MixerAddChannel(mixer, stream, BassFlags.MixerChanPause | BassFlags.MixerChanNoRampin))
                {
                    error = $"効果音を読み込めませんでした（{Bass.LastError}）";
                    if (stream != 0) Bass.StreamFree(stream);
                    FreeVoices(voices);
                    return null;
                }
                voices[i] = stream;
            }

            error = "";
            return new SoundEffect(voices);
        }

        // 鳴らす(音量 0〜1)。前の音が鳴っていても、重ねて鳴らす
        public void Play(float volume)
        {
            float clamped = Math.Clamp(volume, 0f, 1f);

            // ----- BASS / DirectSound -----
            if (sample != 0)
            {
                int channel = Bass.SampleGetChannel(sample, false);
                if (channel == 0)
                {
                    // 普通はもう起きないはず。起きたら原因を調べられるように1回だけ書いておく
                    if (!playFailLogged) Console.WriteLine($"[音] 効果音を鳴らせませんでした（{Bass.LastError}）");
                    playFailLogged = true;
                    return;
                }

                Bass.ChannelSetAttribute(channel, ChannelAttribute.Volume, clamped);
                Bass.ChannelPlay(channel);
                return;
            }

            // ----- WASAPI / ASIO：次の音を頭に戻して、一時停止を外す -----
            if (voices.Length == 0) return;

            int voice = voices[nextVoice];
            nextVoice = (nextVoice + 1) % voices.Length;

            BassMix.ChannelAddFlag(voice, BassFlags.MixerChanPause);  // 鳴っている途中なら、いったん止める
            BassMix.ChannelSetPosition(voice, 0);                      // 頭に戻す
            Bass.ChannelSetAttribute(voice, ChannelAttribute.Volume, clamped);
            BassMix.ChannelRemoveFlag(voice, BassFlags.MixerChanPause); // 鳴らす
        }

        // 鳴っている音を全部止める(F5 で最初からやり直すときなど)
        public void Stop()
        {
            if (sample != 0)
            {
                Bass.SampleStop(sample);
                return;
            }
            foreach (int voice in voices)
            {
                if (voice != 0) BassMix.ChannelAddFlag(voice, BassFlags.MixerChanPause);
            }
        }

        // 片付け(何回呼んでも大丈夫)
        public void Dispose()
        {
            if (sample != 0)
            {
                Bass.SampleFree(sample);
                sample = 0;
            }
            FreeVoices(voices);
            voices = Array.Empty<int>();
        }

        // ミキサーに入れた音を全部片付ける
        private static void FreeVoices(int[] voices)
        {
            foreach (int voice in voices)
            {
                if (voice == 0) continue;
                BassMix.MixerRemoveChannel(voice);
                Bass.StreamFree(voice);
            }
        }
    }
}