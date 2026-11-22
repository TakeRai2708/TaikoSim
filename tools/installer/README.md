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

`latest.json` を配布し、次の形式で `update-feed.json` を作ります。RSA 2048 ビット以上の公開鍵を使ってください。PEM の改行は JSON の `\n` で表します。`TakeTaikoSetup.exe` とこの設定ファイルを、信頼できる経路で配布してください。秘密鍵は配布物やリポジトリに含めません。

```json
{
  "manifestUrl": "https://updates.example.invalid/latest.json",
  "publicKeyPem": "-----BEGIN PUBLIC KEY-----\n（実際の公開鍵）\n-----END PUBLIC KEY-----"
}
```

通常の配布元にはリダイレクトなしの HTTPS 直リンクを使います。本番用の配布先、鍵、Authenticode 署名はこのリポジトリには含まれません。

## 非公開 GitHub リリースで試す

1. 配布 ZIP をリリースに添付し、GitHub API が返す添付ファイルの `url`（`https://api.github.com/repos/…/releases/assets/…`）を署名スクリプトの `-DownloadUrl` に指定します。
2. 生成した署名済み `latest.json` を同じリリースに添付します。テストにはプレリリースを使えます。
3. `update-feed.json` に `publicKeyPem` と `"githubRepository": "TakeRai2708/TaikoSim"` を設定します。プレリリースを読むときは `githubTag` にそのタグ名も指定します。省略時は GitHub の最新正式リリースが対象です。
4. そのリポジトリの Contents を読み取れる各自の認証用トークンを、起動するプロセスの `TAKETAIKO_GITHUB_TOKEN` 環境変数で渡します。トークンは配布物や JSON に含めません。この方式では、友達にもリポジトリの読み取り権限と自分の認証情報が必要です。

## ゲーム起動時

起動時に最新版と実際の exe のファイルバージョンを比較します。「いいえ」や確認時の通信エラーでは、そのままゲームを起動します。「はい」を押すとゲームの隣の `temp/TakeTaikoUpdate-…` に受信し、署名、サイズ、SHA-256、ZIP 内の exe の版を確認して展開します。再起動の案内を約3秒表示した後、ゲームの終了を待って補助プロセスが差し替えます。新しいゲームの起動時に今回の `temp` の作業ファイルを削除します。

自動更新対象は exe、既知の DLL、フォント、ライセンス表記です。設定・譜面・スキン・効果音などのユーザーデータを守るため、同梱スキンと音色は初回インストールのみ展開します。対応するファイルを差し替えられなかった場合は、この実行中に差し替えたものを元に戻します。停電や強制終了後の自動復旧には対応せず、差し替えが中断した形跡があれば起動を止めて `temp` を保持します。
