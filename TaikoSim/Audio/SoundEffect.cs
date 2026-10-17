using ManagedBass;

namespace TaikoSim.Audio
{
    // 効果音1つ分(ドン・カッなど、短くて何回も重ねて鳴らす音)
    // 曲(Song)と違って、全部メモリに読み込んでおいて、鳴らすたびに新しく再生する
    public sealed class SoundEffect : IDisposable
    {
        // 同時に重ねて鳴らせる数(連打で速く叩いても音が途切れないように)
        // 8 だと、同じ音(ドンだけ・カッだけ)を速く続けて叩いたときに足りなくなって、音が鳴らなかった
        private const int MaxOverlap = 32;

        // 全部鳴っているときは、一番前から鳴っている音を止めて、新しい音を鳴らす
        // (BASS の BASS_SAMPLE_OVER_POS = 0x20000。これが無いと、空きが無いときに新しい音が鳴らない)
        private const BassFlags OverrideLongestPlaying = (BassFlags)0x20000;

        // 鳴らせなかったことをログに書いたか(何回も書かないように)
        private bool playFailLogged = false;

        // BASSが効果音を区別するための番号(0 なら読み込めていない)
        private int sample;

        private SoundEffect(int sample)
        {
            this.sample = sample;
        }

        // 効果音を読み込む。失敗したら null を返して、理由を error に入れる
        public static SoundEffect? Load(string path, out string error)
        {
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

            int sample = Bass.SampleLoad(path, 0, 0, MaxOverlap, OverrideLongestPlaying);
            if (sample == 0)
            {
                error = $"効果音を読み込めませんでした（{Bass.LastError}）";
                return null;
            }

            error = "";
            return new SoundEffect(sample);
        }

        // 鳴らす(音量 0〜1)。前の音が鳴っていても、重ねて鳴らす
        public void Play(float volume)
        {
            if (sample == 0) return;

            int channel = Bass.SampleGetChannel(sample, false);
            if (channel == 0)
            {
                // 普通はもう起きないはず。起きたら原因を調べられるように1回だけ書いておく
                if (!playFailLogged) Console.WriteLine($"[音] 効果音を鳴らせませんでした（{Bass.LastError}）");
                playFailLogged = true;
                return;
            }

            Bass.ChannelSetAttribute(channel, ChannelAttribute.Volume, Math.Clamp(volume, 0f, 1f));
            Bass.ChannelPlay(channel);
        }

        // 片付け(何回呼んでも大丈夫)
        public void Dispose()
        {
            if (sample == 0) return;
            Bass.SampleFree(sample);
            sample = 0;
        }
    }
}