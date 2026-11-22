# TakeTaiko ランチャー

C# / .NET 10 の Windows 向けランチャーです。ゲーム本体と同じフォルダに置くと、そのフォルダを作業ディレクトリとしてゲームを起動します。ランチャーとゲームは別プロジェクトです。

## ビルドと配置

```powershell
dotnet publish TakeTaikoLauncher/TakeTaikoLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

出力先は `TakeTaikoLauncher/bin/Release/net10.0-windows/win-x64/publish/`。ランチャーの exe を、既存の `TakeTaikoSimulator.exe` の隣に置いてください。ゲーム側の配布構成については [ルート README](../README.md) を参照してください。

起動時・ウィンドウに戻ったとき・更新確認の結果を出すとき・ダウンロード直前に、実行対象の exe の Windows ファイルバージョンを読みます。配布版以下への入れ替えを案内しないため、配布版と同じか新しい exe がある場合は更新済みと判定します。exe があるのに版を取得できない場合は、更新の判定を止めます。

更新の確認と ZIP の保存はユーザー操作で開始します。「後で」で保留、「中止」でダウンロードを取り消せます。通信エラーなどが出た場合は「再試行」で更新情報を取り直せます。

## 配布担当者向け

1. 更新ごとにゲームのファイルバージョンを上げて Native AOT で発行します。例は `dotnet publish TakeTaikoSimulator/TakeTaikoSimulator.csproj -c Release -r win-x64 -p:Version=1.1.0`。配布情報の比較値は、例では `1.1.0.0` です。
2. 配布を許可したゲームファイルだけを ZIP にまとめます。ZIP の直下に `TakeTaikoSimulator.exe` を置き、個人の設定・譜面や配布権限のない素材を含めないでください。
3. RSA 鍵ペアを用意します。2048 ビット以上の秘密鍵はリポジトリや配布先の外で管理します。PowerShell 7 で次を実行すると、ZIP 内の実際の exe の版・ZIP 全体の SHA-256・サイズを含む署名済み更新情報ができます。

```powershell
./tools/launcher/New-UpdateManifest.ps1 -PackagePath ./release/game.zip -DownloadUrl 'https://updates.example.invalid/game.zip' -PrivateKeyPath 'C:/private/signing.pem' |
    Set-Content -Encoding utf8NoBOM ./release/manifest.json
```

4. ZIP と更新情報を、対象ユーザーにアクセスを限定できる HTTPS 配布先に置きます。URL はリダイレクトなしで取得できるものを指定してください。利用期限付き URL を使う場合は、更新情報の有効期限を ZIP の URL の失効より前に設定します。
5. 信頼できる経路で、次の形式の `launcher-feed.json` を exe の隣に配置します。`publicKeyPem` に `BEGIN PUBLIC KEY` 形式の公開鍵を入れます。PEM の改行は JSON の `\n` で表します。このファイルを変えると、ランチャーが信頼する鍵も変わります。

```json
{
  "manifestUrl": "https://updates.example.invalid/manifest.json",
  "publicKeyPem": "-----BEGIN PUBLIC KEY-----\n（配布担当者の公開鍵）\n-----END PUBLIC KEY-----"
}
```

非公開 GitHub の Releases は、リポジトリのアクセス権と認証が必要です。この初版には GitHub ログイン機能がないため、非公開 Releases の URL を指定するだけでは取得できません。限定配布用の HTTPS 配信先を別途用意してください。GitHub の個人アクセストークンをアプリや設定に埋め込まないでください。

## 保存と適用

署名方式は RSA-PSS / SHA-256。対象製品、版、有効期限、サイズ、ZIP の SHA-256 とダウンロード先を署名の対象にします。受信中は `%LOCALAPPDATA%\TakeTaikoLauncher\Temp` に一意の `.part` ファイルを作ります。サイズとハッシュが一致したものだけを `Downloads` に ZIP として移動します。失敗・中止・通常のウィンドウ終了でも、一時ファイルを削除します。強制終了や停電で残った `.part` は、ランチャーをすべて閉じてから削除できます。

この初版の範囲は検証済み ZIP の保存までです。ゲームへの適用、ロールバック、ランチャー自身の更新、Windows の Authenticode 署名は未実装です。ZIP の保存だけではインストール済みの版は変わりません。

手動適用するときはゲームを閉じ、現行の配布フォルダと `ドキュメント/TakeTaikoSimulator` をバックアップしてから、既存のゲームフォルダに必要な配布ファイルを入れます。旧配置やフォールバックでは exe の隣にも設定・譜面などがあるため、フォルダごとの置き換えは避けてください。ランチャーの更新処理はどちらのゲームデータ置き場にも書き込みません。
