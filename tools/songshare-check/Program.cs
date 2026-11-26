using System.Text;
using TakeTaikoSimulator.Online;

// 曲の配布(Online/SongShare.cs)の確かめ。dotnet run で動かす。通信はしない(小分けをそのまま渡す)
int passed = 0, failed = 0;
void Check(string name, bool ok)
{
    if (ok) passed++; else { failed++; Console.WriteLine($"✗ {name}"); }
}
void Throws<T>(string name, Action action) where T : Exception
{
    try { action(); Check(name, false); }
    catch (T) { Check(name, true); }
}

string work = Path.Combine(Path.GetTempPath(), "songshare-check-" + Guid.NewGuid().ToString("N"));
string host = Path.Combine(work, "host", "Songs");
string guest = Path.Combine(work, "guest", "Songs");
string temp = Path.Combine(work, "guest", "Cache");

// ----- ホストの曲を作る -----
string hostGenre = Path.Combine(host, "01 J-POP");
string hostSong = Path.Combine(hostGenre, "テスト曲");
Directory.CreateDirectory(Path.Combine(hostSong, "img"));
File.WriteAllText(Path.Combine(hostGenre, "box.def"), "#TITLE:J-POP\n#GENRE:J-POP\n");
File.WriteAllText(Path.Combine(hostSong, "test.tja"), "TITLE:テスト曲\nWAVE:test.ogg\nBGIMAGE:img/bg.png\nBGMOVIE:movie.mp4\nCOURSE:Oni\n#START\n1,\n#END\n", new UTF8Encoding(false));
var random = new Random(1);
byte[] audio = new byte[SongShare.ChunkSize * 7 + 123];   // 小分けの境目をまたぐ大きさ
random.NextBytes(audio);
File.WriteAllBytes(Path.Combine(hostSong, "test.ogg"), audio);
byte[] image = new byte[999]; random.NextBytes(image);
File.WriteAllBytes(Path.Combine(hostSong, "img", "bg.png"), image);
byte[] movie = new byte[5000]; random.NextBytes(movie);
File.WriteAllBytes(Path.Combine(hostSong, "movie.mp4"), movie);
string hostTja = Path.Combine(hostSong, "test.tja");

// ----- 1. マニフェスト -----
SongGenre? genre = SongLibrary.GenreOf(host, hostTja);
Check("ホストのジャンルを box.def から読む", genre is { Title: "J-POP", Genre: "J-POP" });
SongManifest noMovie = SongShare.Create(hostTja, genre, includeMovie: false);
SongManifest withMovie = SongShare.Create(hostTja, genre, includeMovie: true);
Check("動画なし：tja・音源・画像の3つ", noMovie.Files.Count == 3 && noMovie.Files[0].Path == "test.tja" && noMovie.Files[1].Path == "test.ogg" && noMovie.Files[2].Path == "img/bg.png");
Check("動画あり：4つ", withMovie.Files.Count == 4 && withMovie.Files[3].Path == "movie.mp4");
Check("動画の有無で指紋は同じ", noMovie.Identity == withMovie.Identity);
Check("自分の曲の指紋と同じ", SongShare.IdentityOf(hostTja) == noMovie.Identity);
Check("タイトル", noMovie.Title == "テスト曲" && noMovie.FolderName == "テスト曲");
SongManifest roundTrip = SongManifest.FromJson(withMovie.ToJson());
Check("JSON で行って帰って同じ", roundTrip.Identity == withMovie.Identity && roundTrip.Files.SequenceEqual(withMovie.Files) && roundTrip.Genre == withMovie.Genre);

// ----- 2. 小分けで送って、受け取る -----
string Transfer(SongManifest manifest, bool shuffle, Func<int, byte[], byte[]>? tamper = null)
{
    using var reader = new SongPackageReader(hostTja, manifest);
    using var receiver = new SongReceiver(SongManifest.FromJson(manifest.ToJson()), temp);
    var order = Enumerable.Range(0, manifest.ChunkCount).ToList();
    if (shuffle) order = order.OrderBy(_ => random.Next()).ToList();
    foreach (int i in order)
    {
        byte[] data = reader.ReadChunk(i);
        if (tamper != null) data = tamper(i, data);
        receiver.Accept(i, data);
    }
    return receiver.Finish(guest);
}

Directory.CreateDirectory(Path.Combine(guest, "10 J-POP"));
File.WriteAllText(Path.Combine(guest, "10 J-POP", "box.def"), "#TITLE:ジェイポップ\n#GENRE:J-POP\n");
string saved = Transfer(withMovie, shuffle: true);
Check("ジャンル(#GENRE が同じ)のフォルダに置く", saved == Path.Combine(guest, "10 J-POP", "テスト曲", "test.tja"));
Check("中身が同じ(tja)", File.ReadAllBytes(saved).SequenceEqual(File.ReadAllBytes(hostTja)));
Check("中身が同じ(音源)", File.ReadAllBytes(Path.Combine(guest, "10 J-POP", "テスト曲", "test.ogg")).SequenceEqual(audio));
Check("中身が同じ(画像・奥のフォルダ)", File.ReadAllBytes(Path.Combine(guest, "10 J-POP", "テスト曲", "img", "bg.png")).SequenceEqual(image));
Check("中身が同じ(動画)", File.ReadAllBytes(Path.Combine(guest, "10 J-POP", "テスト曲", "movie.mp4")).SequenceEqual(movie));
Check("受け取り中の一時フォルダは消える", !Directory.Exists(Path.Combine(temp, withMovie.Identity)));
Check("もらった曲を自分の曲として見つけられる", SongLibrary.FindLocal(guest, withMovie) == saved);

string second = Transfer(noMovie, shuffle: false);
Check("同じ名前があれば「(2)」", second == Path.Combine(guest, "10 J-POP", "テスト曲 (2)", "test.tja"));

// ----- 3. ジャンルの決め方 -----
Check("#GENRE が無ければ #TITLE で探す", SongLibrary.ResolveGenreFolder(guest, new SongGenre("ジェイポップ", "")) == Path.Combine(guest, "10 J-POP"));
Check("どちらも無ければ未分類", SongLibrary.ResolveGenreFolder(guest, new SongGenre("アニメ", "アニメ")) == Path.Combine(guest, "未分類"));
Check("ジャンルなし(null)は未分類", SongLibrary.ResolveGenreFolder(guest, null) == Path.Combine(guest, "未分類"));
Directory.CreateDirectory(Path.Combine(guest, "まとめ", "20 アニメ"));
File.WriteAllText(Path.Combine(guest, "まとめ", "20 アニメ", "box.def"), "#TITLE:アニメ\n#GENRE:アニメ\n");
Check("box.def の無いフォルダの奥の box.def も見つける", SongLibrary.ResolveGenreFolder(guest, new SongGenre("アニメ", "アニメ")) == Path.Combine(guest, "まとめ", "20 アニメ"));
Directory.CreateDirectory(Path.Combine(guest, "10 J-POP", "中"));
File.WriteAllText(Path.Combine(guest, "10 J-POP", "中", "box.def"), "#TITLE:中のジャンル\n#GENRE:中\n");
Check("box.def の中の box.def は使わない", SongLibrary.ResolveGenreFolder(guest, new SongGenre("中のジャンル", "中")) == Path.Combine(guest, "未分類"));
string looseTja = Transfer(SongShare.Create(hostTja, null, false), shuffle: false);
Check("ジャンルなしの曲は Songs/未分類 に置く", looseTja == Path.Combine(guest, "未分類", "テスト曲", "test.tja"));

// ----- 4. 壊れたデータ・危ないデータ -----
Throws<InvalidDataException>("壊れた小分けは見つかる", () => Transfer(noMovie, false, (i, d) => { if (i == 3) d[10] ^= 0xFF; return d; }));
Check("壊れたときは何も置かない", !Directory.Exists(Path.Combine(guest, "10 J-POP", "テスト曲 (3)")));
using (var r = new SongReceiver(noMovie, temp))
{
    Check("番号がはみ出したら捨てる", !r.Accept(-1, new byte[SongShare.ChunkSize]) && !r.Accept(noMovie.ChunkCount, new byte[SongShare.ChunkSize]));
    Check("大きさが違ったら捨てる", !r.Accept(0, new byte[10]));
    Check("最初は全部「まだ」", r.Missing(10000).Count == noMovie.ChunkCount && r.Progress == 0);
    Throws<InvalidOperationException>("そろう前に Finish はできない", () => r.Finish(guest));
}

SongManifest With(Func<List<SongFile>, List<SongFile>> change)
{
    var files = change(noMovie.Files.ToList());
    return new SongManifest { Identity = SongShare.MakeIdentity(files[0].Sha256, files[1].Sha256), Title = "x", FolderName = "x", Files = files };
}
string fake = new string('a', 64);
foreach (string bad in new[] { "../evil.tja", "a/../../evil.ogg", "/etc/passwd.ogg", "C:/x.ogg", "a\\b.ogg", "a//b.ogg", "con?.ogg", "dot./x.ogg" })
    Throws<InvalidDataException>($"危ない場所 {bad} は受け取らない", () => SongShare.Validate(With(f => { f.Add(new SongFile(bad, 1, fake)); return f; })));
Throws<InvalidDataException>("exe は受け取らない", () => SongShare.Validate(With(f => { f.Add(new SongFile("run.exe", 1, fake)); return f; })));
Throws<InvalidDataException>("同じファイル2回はだめ", () => SongShare.Validate(With(f => { f.Add(f[1]); return f; })));
Throws<InvalidDataException>("大きすぎる曲はだめ", () => SongShare.Validate(With(f => { f.Add(new SongFile("big.mp4", SongShare.MaxTotalBytes, fake)); return f; })));
Throws<InvalidDataException>("1つ目が tja でないとだめ", () => SongShare.Validate(With(f => { (f[0], f[2]) = (f[2], f[0]); return f; })));
Throws<InvalidDataException>("指紋が中身と合わないとだめ", () => SongShare.Validate(new SongManifest { Identity = fake, Files = noMovie.Files }));
Throws<InvalidDataException>("形式の番号が違うとだめ", () => SongShare.Validate(new SongManifest { Format = 99, Identity = noMovie.Identity, Files = noMovie.Files }));
Throws<InvalidDataException>("JSON が壊れていたらだめ", () => SongManifest.FromJson("{"u8));

// ----- 5. ホスト側で配れない曲 -----
string outside = Path.Combine(host, "外", "x");
Directory.CreateDirectory(outside);
File.WriteAllText(Path.Combine(outside, "x.tja"), "TITLE:外\nWAVE:../../01 J-POP/テスト曲/test.ogg\n");
Throws<InvalidDataException>("音源が .tja のフォルダの外なら配れない", () => SongShare.Create(Path.Combine(outside, "x.tja"), null, false));
File.WriteAllText(Path.Combine(outside, "y.tja"), "TITLE:音源なし\n");
Throws<InvalidDataException>("WAVE が無ければ配れない", () => SongShare.Create(Path.Combine(outside, "y.tja"), null, false));

// ----- 6. 名前 -----
Check("フォルダ名の危ない文字は _", SongLibrary.SafeFolderName("a/b:c*?") == "a_b_c__");
Check("空の名前は song", SongLibrary.SafeFolderName("  ") == "song" && SongLibrary.SafeFolderName("..") == "song");

Directory.Delete(work, recursive: true);
Console.WriteLine($"{passed} 個OK / {failed} 個だめ");
return failed == 0 ? 0 : 1;
