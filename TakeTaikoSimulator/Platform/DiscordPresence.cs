using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Scenes;

namespace TakeTaikoSimulator.Platform
{
    // Discord の「〇〇をプレイ中」に、今なにをしているかを出す（リッチプレゼンス）
    //
    //   11/11 たけらい「Discord でプレイ中のゲームでリッチプレゼンスみたいなのつけたい」→ A 案（自分で書く）
    //   ・ライブラリは使わない。Discord とは PC の中の名前付きパイプ（\\.\pipe\discord-ipc-0〜9）に JSON を送るだけでつながる。
    //     JSON は Utf8JsonWriter で手で書くので、Native AOT（逆コンパイル対策）を壊さない
    //   ・やりとりは裏のスレッドでやる。Discord が起動していなくても、ゲームは止まらない（15秒ごとにつなぎ直す）
    //   ・各画面は Scene.Presence で「今出したいこと」を返すだけ。変わったときだけ送る（Discord の回数制限があるので、間は2秒以上あける）
    //   ・設定：「Discord に表示する」（ConfigManager.Game.DiscordPresence）、「曲名を出さない」（DiscordHideSongTitle）
    //
    //   送るもの（Discord の決まり）：
    //     1回分 = [種類 4バイト][長さ 4バイト][JSON]（どちらも little endian）
    //     種類 0 = あいさつ（{"v":1,"client_id":"…"}）、1 = ふつうの命令（SET_ACTIVITY）、2 = 切断
    public static class DiscordPresence
    {
        /// <summary>
        /// Discord Developer Portal で作ったアプリの Application ID（数字の列）。
        /// 空のあいだは何もしない。ここに貼ってビルドし直すと動く。
        /// </summary>
        public const string ApplicationId = "1570361696688799856";

        // Discord の「Rich Presence → Art Assets」に上げた画像の名前
        private const string LogoAsset = "logo";
        private const string LogoText = "TakeTaikoSimulator";

        /// <summary>出す内容。Details が1行目、State が2行目。EndUnixSeconds があれば「残り ○:○○」が出る</summary>
        public readonly record struct Info(string Details, string? State = null, long? EndUnixSeconds = null);

        public enum Status { Off, NoId, Waiting, Connected }

        /// <summary>今の状態（設定画面に出す）</summary>
        public static Status CurrentStatus =>
            !ConfigManager.Game.DiscordPresence ? Status.Off
            : ApplicationId.Length == 0 ? Status.NoId
            : connected ? Status.Connected : Status.Waiting;

        // ===== メインのスレッド側 =====
        private static Info? lastWanted;
        private static bool lastEnabled;
        private static bool hasLast;

        // ===== 2つのスレッドで分け合うもの（gate でまもる） =====
        private static readonly object gate = new();
        private static Info? wanted;
        private static bool wantedEnabled;
        private static int wantedVersion;

        // ===== 裏のスレッド =====
        private static Thread? thread;
        private static readonly AutoResetEvent wake = new(false);
        private static volatile bool stopping;
        private static volatile bool connected;
        private static NamedPipeClientStream? pipe;

        private const int ReconnectMs = 15000;     // Discord が見つからないとき、つなぎ直すまで
        private const int MinSendIntervalMs = 2000; // 送る間を、これ以上あける（Discord の回数制限）
        private const long EndToleranceSeconds = 3; // 残り時間が、これ以上ずれたら送り直す（一時停止など）

        /// <summary>毎フレーム（Game から）。今の画面の Presence を見て、変わっていたら裏のスレッドに渡す</summary>
        public static void Update(Scene? scene)
        {
            bool enabled = ConfigManager.Game.DiscordPresence && ApplicationId.Length > 0;
            Info? info = enabled ? scene?.Presence ?? new Info("メニュー") : null;
            if (hasLast && enabled == lastEnabled && Same(info, lastWanted)) return;

            hasLast = true;
            lastEnabled = enabled;
            lastWanted = info;
            lock (gate)
            {
                wanted = info;
                wantedEnabled = enabled;
                wantedVersion++;
            }
            if (enabled && thread == null)
            {
                thread = new Thread(Run) { IsBackground = true, Name = "DiscordPresence" };
                thread.Start();
            }
            wake.Set();
        }

        /// <summary>ゲームを閉じるとき。Discord の表示を消して、切る</summary>
        public static void Shutdown()
        {
            stopping = true;
            wake.Set();
            thread?.Join(1000);
            Disconnect(clear: false);
        }

        /// <summary>曲名を出してよいか（設定の「曲名を出さない」）</summary>
        public static bool ShowSongTitle => !ConfigManager.Game.DiscordHideSongTitle;

        /// <summary>今から seconds 秒後の時刻（Discord の「残り」用）</summary>
        public static long EndAfter(double seconds) => DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (long)Math.Ceiling(Math.Max(0, seconds));

        private static bool Same(Info? a, Info? b)
        {
            if (a == null || b == null) return a == null && b == null;
            Info x = a.Value, y = b.Value;
            if (x.Details != y.Details || x.State != y.State) return false;
            if (x.EndUnixSeconds == null || y.EndUnixSeconds == null) return x.EndUnixSeconds == y.EndUnixSeconds;
            return Math.Abs(x.EndUnixSeconds.Value - y.EndUnixSeconds.Value) <= EndToleranceSeconds;
        }

        // ===== ここから裏のスレッド =====

        private static void Run()
        {
            int sentVersion = -1;
            long lastSend = 0;
            while (!stopping)
            {
                Info? info;
                bool enabled;
                int version;
                lock (gate)
                {
                    info = wanted;
                    enabled = wantedEnabled;
                    version = wantedVersion;
                }

                if (!enabled)
                {
                    Disconnect(clear: true);
                    sentVersion = -1;
                    wake.WaitOne(1000);
                    continue;
                }

                if (pipe == null)
                {
                    if (!TryConnect())
                    {
                        wake.WaitOne(ReconnectMs);
                        continue;
                    }
                    sentVersion = -1;
                }

                if (version != sentVersion)
                {
                    double since = Stopwatch.GetElapsedTime(lastSend).TotalMilliseconds;
                    if (lastSend != 0 && since < MinSendIntervalMs)
                    {
                        wake.WaitOne((int)(MinSendIntervalMs - since) + 1);
                        continue;
                    }
                    try
                    {
                        Send(1, ActivityJson(info));
                        ReadFrame();
                        sentVersion = version;
                        lastSend = Stopwatch.GetTimestamp();
                    }
                    catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is InvalidOperationException)
                    {
                        Console.WriteLine($"[Discord] 切れました：{ex.Message}");
                        Disconnect(clear: false);
                    }
                    continue;
                }

                wake.WaitOne(1000);
            }
            Disconnect(clear: true);
        }

        // Discord につなぐ（discord-ipc-0〜9 を順に試す）
        private static bool TryConnect()
        {
            for (int i = 0; i < 10 && !stopping; i++)
            {
                NamedPipeClientStream client = new(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.None);
                try
                {
                    client.Connect(200);
                    pipe = client;
                    Send(0, HandshakeJson());
                    int op = ReadFrame();
                    if (op != 1) throw new IOException($"あいさつが通りませんでした（{op}）。Application ID を確かめてください");
                    connected = true;
                    Console.WriteLine($"[Discord] つながりました（discord-ipc-{i}）");
                    return true;
                }
                catch (Exception ex) when (ex is TimeoutException || ex is IOException || ex is UnauthorizedAccessException
                                           || ex is ObjectDisposedException || ex is InvalidOperationException)
                {
                    if (pipe != null) Console.WriteLine($"[Discord] {ex.Message}");
                    pipe = null;
                    client.Dispose();
                }
            }
            return false;
        }

        private static void Disconnect(bool clear)
        {
            NamedPipeClientStream? p = pipe;
            if (p == null) { connected = false; return; }
            if (clear && connected)
            {
                try
                {
                    Send(1, ActivityJson(null));   // 表示を消す
                    ReadFrame();
                }
                catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is InvalidOperationException)
                {
                }
            }
            pipe = null;
            connected = false;
            p.Dispose();
        }

        private static void Send(int op, byte[] json)
        {
            NamedPipeClientStream p = pipe ?? throw new IOException("つながっていません");
            byte[] frame = new byte[8 + json.Length];
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), op);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4, 4), json.Length);
            json.CopyTo(frame, 8);
            p.Write(frame, 0, frame.Length);
            p.Flush();
        }

        // 返事を1つ読む（中身は使わないので読み捨てる）。種類を返す
        private static int ReadFrame()
        {
            NamedPipeClientStream p = pipe ?? throw new IOException("つながっていません");
            byte[] header = new byte[8];
            ReadExactly(p, header);
            int op = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
            int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4));
            if (length < 0 || length > 1 << 20) throw new IOException("返事の長さがおかしい");
            ReadExactly(p, new byte[length]);
            if (op == 2) throw new IOException("Discord に切られました");
            return op;
        }

        private static void ReadExactly(Stream stream, byte[] buffer)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int n = stream.Read(buffer, read, buffer.Length - read);
                if (n <= 0) throw new IOException("Discord との接続が閉じました");
                read += n;
            }
        }

        private static byte[] HandshakeJson()
        {
            using MemoryStream ms = new();
            using (Utf8JsonWriter w = new(ms))
            {
                w.WriteStartObject();
                w.WriteNumber("v", 1);
                w.WriteString("client_id", ApplicationId);
                w.WriteEndObject();
            }
            return ms.ToArray();
        }

        private static byte[] ActivityJson(Info? info)
        {
            using MemoryStream ms = new();
            using (Utf8JsonWriter w = new(ms))
            {
                w.WriteStartObject();
                w.WriteString("cmd", "SET_ACTIVITY");
                w.WriteString("nonce", Guid.NewGuid().ToString());
                w.WriteStartObject("args");
                w.WriteNumber("pid", Environment.ProcessId);
                if (info == null)
                {
                    w.WriteNull("activity");
                }
                else
                {
                    Info a = info.Value;
                    w.WriteStartObject("activity");
                    w.WriteString("details", Fit(a.Details));
                    if (!string.IsNullOrWhiteSpace(a.State)) w.WriteString("state", Fit(a.State));
                    if (a.EndUnixSeconds is long end)
                    {
                        w.WriteStartObject("timestamps");
                        w.WriteNumber("end", end);
                        w.WriteEndObject();
                    }
                    w.WriteStartObject("assets");
                    w.WriteString("large_image", LogoAsset);
                    w.WriteString("large_text", LogoText);
                    w.WriteEndObject();
                    w.WriteEndObject();
                }
                w.WriteEndObject();
                w.WriteEndObject();
            }
            return ms.ToArray();
        }

        // Discord の決まり：2〜128文字。1文字の曲名（「冬」など）は全角スペースを足す。長いものは切って「…」
        private static string Fit(string text)
        {
            text = text.Trim();
            if (text.Length < 2) text += "　";
            if (text.Length > 128)
            {
                int cut = 127;
                if (char.IsHighSurrogate(text[cut - 1])) cut--;
                text = text[..cut] + "…";
            }
            return text;
        }
    }
}
