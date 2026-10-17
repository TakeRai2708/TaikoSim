using ManagedBass;

namespace TaikoSim.Audio
{
    // 曲1つ分(.tja の WAVE: のファイル)
    // 使い終わったら Dispose() で片付ける
    // ※ Raylib にも「Music」があって名前がぶつかるので「Song」にしている
    public sealed class Song : IDisposable
    {
        // BASSが曲を区別するための番号(0 なら読み込めていない)
        private int handle;

        // 曲の長さ(ミリ秒)
        public double LengthMs { get; }

        private Song(int handle)
        {
            this.handle = handle;
            LengthMs = Bass.ChannelBytes2Seconds(handle, Bass.ChannelGetLength(handle)) * 1000.0;
        }

        // 曲を読み込む。失敗したら null を返して、理由を error に入れる
        public static Song? Load(string path, out string error)
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

            // Prescan = mp3 の長さや位置を正確にする(読み込みが少し遅くなるかわりにズレにくい)
            int handle = Bass.CreateStream(path, 0, 0, BassFlags.Prescan);
            if (handle == 0)
            {
                error = $"曲を読み込めませんでした（{Bass.LastError}）";
                return null;
            }

            error = "";
            return new Song(handle);
        }

        // 今どこを鳴らしているか(ミリ秒)
        public double PositionMs
        {
            get
            {
                if (handle == 0) return 0;
                long bytes = Bass.ChannelGetPosition(handle);
                return Bass.ChannelBytes2Seconds(handle, bytes) * 1000.0;
            }
        }

        // 鳴っている途中か(最後まで鳴り終わったら false)
        public bool IsPlaying => handle != 0 && Bass.ChannelIsActive(handle) == PlaybackState.Playing;

        // 音量(0〜1)
        public void SetVolume(float volume)
        {
            if (handle == 0) return;
            Bass.ChannelSetAttribute(handle, ChannelAttribute.Volume, Math.Clamp(volume, 0f, 1f));
        }

        // startMs の位置から鳴らす
        public void Play(double startMs)
        {
            if (handle == 0) return;
            long bytes = Bass.ChannelSeconds2Bytes(handle, Math.Max(0, startMs) / 1000.0);
            Bass.ChannelSetPosition(handle, bytes);
            Bass.ChannelPlay(handle);
        }

        // 止める
        public void Stop()
        {
            if (handle == 0) return;
            Bass.ChannelStop(handle);
        }

        // 片付け(何回呼んでも大丈夫)
        public void Dispose()
        {
            if (handle == 0) return;
            Bass.StreamFree(handle);
            handle = 0;
        }
    }
}