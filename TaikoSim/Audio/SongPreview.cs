using TaikoSim.Charts;
using TaikoSim.Configs;

namespace TaikoSim.Audio
{
    // 選曲画面のプレビュー(選んでいる曲を DEMOSTART の位置から流す)
    //   ・選んでから少し待ってから読み込む(速くカーソルを動かしているときに、1曲ずつ読まないように)
    //   ・読み込みは裏でやる(大きい曲でも画面が止まらないように)
    //   ・だんだん大きくして流し始める。曲の最後まで行ったら頭から繰り返す
    public sealed class SongPreview : IDisposable
    {
        private const float WaitSeconds = 0.35f;
        private const float FadeInSeconds = 0.6f;

        private string? wanted;        // 流したい曲(.tja)。null なら止める
        private string? attempted;     // 最後に読み込みを始めた曲(読めなかったときに、何度も読み直さないため)
        private float wait;

        private Task<(Song? Song, double StartMs)>? loading;
        private string? loadingTja;
        private int loadingAudio;

        private Song? song;
        private int songAudio;
        private float fade;

        // 流したい曲を伝える(毎フレーム呼んでよい)。同じ曲なら何もしない
        public void Request(string? tja)
        {
            if (string.Equals(tja, wanted, StringComparison.OrdinalIgnoreCase)) return;
            wanted = tja;
            attempted = null;
            wait = WaitSeconds;
            StopCurrent();
        }

        public void Update(float dt)
        {
            // 音を準備し直したあとは、前の曲はもう使えない(片付けもしない)
            if (song != null && songAudio != AudioManager.Generation) song = null;

            if (song != null)
            {
                fade = Math.Min(1f, fade + dt / FadeInSeconds);
                song.SetVolume(Volume * fade);
            }

            // 裏で読み込み中
            if (loading != null)
            {
                if (!loading.IsCompleted) return;
                Song? loaded = null;
                double startMs = 0;
                if (loading.IsCompletedSuccessfully) (loaded, startMs) = loading.Result;
                loading = null;
                bool stillWanted = string.Equals(loadingTja, wanted, StringComparison.OrdinalIgnoreCase)
                    && loadingAudio == AudioManager.Generation;
                if (loaded == null) return;
                if (!stillWanted)
                {
                    if (loadingAudio == AudioManager.Generation) loaded.Dispose();
                    return;
                }
                song = loaded;
                songAudio = loadingAudio;
                fade = 0f;
                song.SetVolume(0f);
                song.SetLoop(true);
                song.Play(startMs);
                return;
            }

            if (wanted == null || string.Equals(wanted, attempted, StringComparison.OrdinalIgnoreCase)) return;
            wait -= dt;
            if (wait > 0f) return;

            string tja = wanted;
            attempted = tja;
            loadingTja = tja;
            loadingAudio = AudioManager.Generation;
            loading = Task.Run(() =>
            {
                var info = TjaParser.ReadPreview(tja);
                if (info == null) return ((Song?)null, 0.0);
                Song? loaded = Song.Load(info.Value.WavePath, out _);
                return (loaded, info.Value.DemoStartSec * 1000.0);
            });
        }

        private static float Volume => (float)ConfigManager.Sound.GetFinalVolume(ConfigManager.Sound.MusicVolume);

        private void StopCurrent()
        {
            if (song != null && songAudio == AudioManager.Generation)
            {
                song.Stop();
                song.Dispose();
            }
            song = null;
        }

        // 片付け(画面から出るとき)。裏で読み込み中の曲は、読み終わったら捨てる
        public void Dispose()
        {
            wanted = null;
            StopCurrent();
            if (loading != null)
            {
                int audio = loadingAudio;
                loading.ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully && audio == AudioManager.Generation) t.Result.Song?.Dispose();
                });
                loading = null;
            }
        }
    }
}
