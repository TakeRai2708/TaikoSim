using TakeTaikoSimulator.Charts;
using TakeTaikoSimulator.Configs;

namespace TakeTaikoSimulator.Audio
{
    // 選曲画面のプレビュー(選んでいる曲を DEMOSTART の位置から流す)
    //   ・カーソルが止まったら、裏で先に読み込んでおく(大きい曲でも画面が止まらないように)
    //   ・流し始めるのは「流して」と言われたとき(TaikøNauts と同じく、曲の板が開いたとき)
    //   ・最初から普通の大きさで流す(TaikøNauts と同じくフェードなし)。曲の最後まで行ったら頭から繰り返す
    public sealed class SongPreview : IDisposable
    {
        // カーソルが止まってから読み込みを始めるまで(速くカーソルを動かしているときに、1曲ずつ読まないように)
        private const float LoadWaitSeconds = 0.1f;

        private string? wanted;        // 流したい曲(.tja)。null なら止める
        private bool playWanted;       // 今流してよいか
        private string? attempted;     // 最後に読み込みを始めた曲(読めなかったときに、何度も読み直さないため)
        private float wait;

        private Task<(Song? Song, double StartMs)>? loading;
        private string? loadingTja;
        private int loadingAudio;

        private Song? song;            // 読み込んだ曲(wanted の曲)
        private int songAudio;
        private int observedAudio = AudioManager.Generation;
        private double songStartMs;
        private bool playing;

        // プレビューの再生状態と読み込み待ち。失敗した曲ではBGMに戻せる。
        public bool IsPlaying => playing && song != null && songAudio == AudioManager.Generation;
        public bool HasPlaybackRequest => playWanted && (song != null || loading != null
            || !string.Equals(wanted, attempted, StringComparison.OrdinalIgnoreCase));

        // 流したい曲と、今流してよいかを伝える(毎フレーム呼んでよい)
        //   tja が同じなら読み込み直さない。play が false なら、読み込みだけして待つ
        public void Request(string? tja, bool play)
        {
            if (!string.Equals(tja, wanted, StringComparison.OrdinalIgnoreCase))
            {
                wanted = tja;
                attempted = null;
                wait = LoadWaitSeconds;
                StopCurrent();
            }
            playWanted = play && tja != null;
        }

        // 設定パネルで音量が変わったとき、流れている曲にすぐ新しい音量をかける
        public void ApplyVolume()
        {
            if (song != null && playing && songAudio == AudioManager.Generation) song.SetVolume(Volume);
        }

        public void Update(float dt)
        {
            // 音を準備し直したあとは、前の曲はもう使えない(片付けもしない)
            if (observedAudio != AudioManager.Generation)
            {
                observedAudio = AudioManager.Generation;
                song = null;
                playing = false;
                attempted = null; // 同じ曲のままでも、新しい出力へ読み直す。
                wait = 0;
            }

            // 読み込んである曲を、流す・止める
            if (song != null)
            {
                if (playWanted && !playing)
                {
                    song.SetVolume(Volume);
                    song.SetLoop(true);
                    song.Play(songStartMs);
                    playing = true;
                }
                else if (!playWanted && playing)
                {
                    song.Stop();
                    playing = false;
                }
                else if (playing) song.SetVolume(Volume);
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
                songStartMs = startMs;
                playing = false;
                return; // 次のフレームで、流してよければ流す
            }

            if (wanted == null || song != null || string.Equals(wanted, attempted, StringComparison.OrdinalIgnoreCase)) return;
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
                if (playing) song.Stop();
                song.Dispose();
            }
            song = null;
            playing = false;
        }

        // 片付け(画面から出るとき)。裏で読み込み中の曲は、読み終わったら捨てる
        public void Dispose()
        {
            wanted = null;
            playWanted = false;
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

