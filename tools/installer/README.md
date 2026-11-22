# TakeTaiko Web インストーラーと起動時更新

`TakeTaikoSetup.exe` は起動時に署名付きの最新リリース情報を取得し、ユーザーが「はい」を選んでから初回配布 ZIP をダウンロードします。既定のインストール先は `%LOCALAPPDATA%\Programs\TakeTaiko` です。「参照…」で選んだ場所には `TakeTaiko` フォルダーを作ります。同名のフォルダーがある場合は `TakeTaiko (2)` などの空いている名前を表示します。インストール後はスタートメニューまたはインストール先の `TakeTaiko.exe` から直接起動します。既存のユーザーデータの保存先 `ドキュメント/TakeTaikoSimulator` は共用します。

## 配布担当者の手順

Windows の Visual Studio Installer で「C++ によるデスクトップ開発」を用意します。リポジトリのルートから発行します。更新のたびに `Version` を上げてください。

```powershell
dotnet publish ./TakeTaikoSimulator/TakeTaikoSimulator.csproj -c Release -r win-x64 -p:Version=1.0.1 -o ./work/publish-1.0.1
./tools/installer/New-ReleasePackage.ps1 -PublishDirectory ./work/publish-1.0.1 -OutputDirectory ./work/release-1.0.1
```

ZIP を限定 HTTPS 配布先に置き、同じ ZIP から署名付きの最新リリース情報を作ります。PowerShell 7 を使います。ZIP 内の実際のファイルバージョンが署名に含まれます。作業用の最新リリース情報は、以前のインストーラーからも取得できる固定 URL で提供してください。

```powershell
./tools/installer/New-UpdateManifest.ps1 -PackagePath ./work/release-1.0.1/TakeTaiko-1.0.1.0-win-x64.zip -DownloadUrl 'https://updates.example.invalid/game-1.0.1.zip' -PrivateKeyPath 'C:/private/signing.pem' -Notes '更新内容をここに記載' |
    Set-Content -Encoding utf8NoBOM ./work/release-1.0.1/latest.json
```

`latest.json` を配布し、次の形式で `update-feed.json` を作ります。RSA 2048 ビット以上の公開鍵を使ってください。PEM の改行は JSON の `\n` で表します。秘密鍵は配布物やリポジトリに含めません。

```json
{
  "manifestUrl": "https://updates.example.invalid/latest.json",
  "publicKeyPem": "-----BEGIN PUBLIC KEY-----\n（実際の公開鍵）\n-----END PUBLIC KEY-----"
}
```

通常の配布元にはリダイレクトなしの HTTPS 直リンクを使います。本番用の配布先、鍵、Authenticode 署名はこのリポジトリには含まれません。

### 友達にインストーラー exe だけを渡す

友達が GitHub にログインせず取得できる HTTPS 配布先を用意します。固定の `latest.json` URL と公開鍵を入れた設定を、最初の発行時に組み込みます。`update-feed.json` は作業者が用意するファイルです。

```powershell
$feedConfig = (Resolve-Path ./work/update-feed.json).Path
dotnet publish ./TakeTaikoSimulator/TakeTaikoSimulator.csproj -c Release -r win-x64 -p:Version=1.0.1 "-p:TakeTaikoFeedConfig=$feedConfig" -o ./work/publish-friends
./tools/installer/New-ReleasePackage.ps1 -PublishDirectory ./work/publish-friends -OutputDirectory ./work/release-friends
```

出力された配布 ZIP と署名済み `latest.json` を配置すれば、友達に渡すのは `TakeTaikoSetup.exe` だけです。インストーラーがゲームのフォルダーにも設定を書き込み、その後のゲーム起動時更新に引き継ぎます。設定を組み込んだ exe では組み込みの公開鍵と配布先が優先されます。

## 非公開 GitHub リリースで試す

1. 配布 ZIP をリリースに添付し、GitHub API が返す添付ファイルの `url`（`https://api.github.com/repos/…/releases/assets/…`）を署名スクリプトの `-DownloadUrl` に指定します。
2. 生成した署名済み `latest.json` を同じリリースに添付します。テストにはプレリリースを使えます。
3. `update-feed.json` に `publicKeyPem` と `"githubRepository": "TakeRai2708/TaikoSim"` を設定します。プレリリースを読むときは `githubTag` にそのタグ名も指定します。省略時は GitHub の最新正式リリースが対象です。
4. そのリポジトリの Contents を読み取れる各自の認証用トークンを、起動するプロセスの `TAKETAIKO_GITHUB_TOKEN` 環境変数で渡します。トークンは配布物や JSON に含めません。この方式では、友達にもリポジトリの読み取り権限と自分の認証情報が必要です。

## 招待された人だけに配布する

`InviteRelay` は非公開 GitHub Releases を読み、招待を持つインストーラーとゲームへだけ配布します。招待は初回インストール時にゲームへ引き継がれます。プレイヤーの GitHub アカウントは不要です。インストーラーや招待設定をコピーすると同じ招待を使えるため、本人に直接渡してください。招待の失効後は新規取得と更新が止まり、手元にあるゲームは起動できます。

1. 招待設定を組み込まずにゲームを発行し、ZIP と署名済み `latest.json` を同じ非公開リリースに添付します。署名するダウンロード URL には ZIP の GitHub API 添付 URL を使います。全員が使うゲーム ZIP に個別の招待を含めないでください。
2. .NET 10 が動く HTTPS 配布先に下のコマンドで発行した `InviteRelay` を配置します。HTTPS はサーバーまたは手前の HTTPS プロキシで終端し、HTTP のバックエンドをインターネットに公開しないでください。認証ヘッダーをログに残さず、招待ファイルは公開ディレクトリの外で管理します。

```powershell
dotnet publish ./tools/installer/InviteRelay/InviteRelay.csproj -c Release -o ./work/invite-relay
```

サーバーの環境変数に `TAKETAIKO_RELAY_REPOSITORY`（例 `TakeRai2708/TaikoSim`）、`TAKETAIKO_RELAY_INVITES`（許可する招待ハッシュを記録したファイルのパス）、`TAKETAIKO_RELAY_GITHUB_TOKEN`（そのリポジトリの Contents の読み取り権限だけを持つトークン）を設定し、`dotnet InviteRelay.dll` を起動します。任意の `TAKETAIKO_RELAY_TAG` でテスト用リリースを固定できます。省略時は最新の正式リリースが対象です。

3. 受取人ごとに招待を作り、出力した JSON を `TakeTaikoFeedConfig` に指定して発行します。その発行物の `TakeTaiko.exe` を `TakeTaikoSetup.exe` に改名し、本人に渡します。作成時に表示される識別子をサーバーの許可リストから消すと、その人の招待だけを失効できます。

```powershell
./tools/installer/New-Invite.ps1 -RelayUrl 'https://updates.example.invalid/' -PublicKeyPath ./work/public.pem -ConfigPath ./work/invite-friend.json -AllowlistPath ./work/allowed-invites.txt
$inviteConfig = (Resolve-Path ./work/invite-friend.json).Path
dotnet publish ./TakeTaikoSimulator/TakeTaikoSimulator.csproj -c Release -r win-x64 "-p:TakeTaikoFeedConfig=$inviteConfig" -o ./work/setup-friend
Copy-Item ./work/setup-friend/TakeTaiko.exe ./work/setup-friend/TakeTaikoSetup.exe
```

## ゲーム起動時

起動時に最新版と実際の exe のファイルバージョンを比較します。「いいえ」や確認時の通信エラーでは、そのままゲームを起動します。「はい」を押すとゲームの隣の `temp/TakeTaikoUpdate-…` に受信し、署名、サイズ、SHA-256、ZIP 内の exe の版を確認して展開します。再起動の案内を約3秒表示した後、ゲームの終了を待って補助プロセスが差し替えます。新しいゲームの起動時に今回の `temp` の作業ファイルを削除します。

自動更新対象は exe、既知の DLL、フォント、ライセンス表記です。設定・譜面・スキン・効果音などのユーザーデータを守るため、同梱スキンと音色は初回インストールのみ展開します。対応するファイルを差し替えられなかった場合は、この実行中に差し替えたものを元に戻します。停電や強制終了後の自動復旧には対応せず、差し替えが中断した形跡があれば起動を止めて `temp` を保持します。
