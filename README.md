# TakeTaiko

Windows / .NET 10、Raylib-cs、ManagedBass を使う個人用の太鼓シミュレーターです。
旧製品名は TaikoSim。第三者の曲・スキンを含め、このリポジトリは非公開のまま扱ってください。

## ソースを探す

画面ごとのソースは `TakeTaikoSimulator/Scenes/` の下にまとめています。
演奏は `Play/`、選曲・難易度選択は `SongSelect/`、エントリーは `Entry/` です。

- [画面別のファイル案内](SOURCE-MAP.md)：各フォルダの役割と、直したい内容から探す表。
- [座標調整ガイド](COORDINATE-GUIDE.md)：日本語コメント付きの調整値と具体例。

## ビルド

Visual Studio または .NET 10 SDK で `TakeTaikoSimulator.slnx` を開きます。

```powershell
dotnet build TakeTaikoSimulator.slnx -r win-x64
dotnet publish TakeTaikoSimulator/TakeTaikoSimulator.csproj -r win-x64 -c Release
```

実行ファイル名は **`TakeTaiko.exe`**。Native AOT の設定は維持しています。
`dotnet build` は Native AOT 版の生成ではありません。AOT の publish は Windows と
Visual Studio の「C++ によるデスクトップ開発」が必要です。

互換性のため、ビルド出力先は従来の `TaikoSim/bin/` のままです。
例：`TaikoSim/bin/Debug/net10.0-windows/win-x64/TakeTaiko.exe`。
publish は `TaikoSim/bin/Release/net10.0-windows/win-x64/publish/` に出力します。
改名前と同じ構成・RID・出力先でビルドすれば、既存データを同じ場所から読みます。
構成やRIDを変えたときは別の出力先になるので、使用中のデータを引き継いでください。

Linux のソース確認は [tools/build-check/README.md](tools/build-check/README.md) を参照。

## 既存データの引き継ぎ

最新版を取得する Windows 用インストーラーとゲーム起動時の更新確認については、[インストーラーと配布手順](tools/installer/README.md) を参照してください。

ゲームは引き続き **exe の隣**を基準に読み書きします。更新時は同じ配布フォルダを使い、
既存の `Config/`、`Songs/`、`Skins/`、`Cache/`、`HitSounds/`、`Screenshots/`、`Logs/`
を残してください。空の設定で上書きしたり、古い配布フォルダごと消したりしないでください。

- 設定ファイル名とJSON形式は変更していません。
- データ1は `Config/SongHistory.json`、データ2・3は
  `Config/PlayerData/2|3/SongHistory.json` のままです。記録を移す処理はありません。
- 曲・スキンの外部フォルダ指定、キャッシュ形式・保存先も維持しています。
- 既存スキン名 `TaikoSim-Simple`、`TAIKOSIM_*` 環境変数、ログ・スクリーンショットの
  `TaikoSim_` 接頭辞は意図的に残しています。
- ソースディレクトリと名前空間は `TakeTaikoSimulator` に統一しています。
  ゲームロジック、外部DLL名、依存ライブラリ、ライセンス条文は改名で変更していません。

## プレイヤー名・ネームプレート・着せ替え

初めて使うデータ枠では名前を入力します。既存のスコアはそのままです。
モード選択の「着せ替え」で、参加者ごとにどんちゃん・ぷちキャラ・ネームプレートを選び、
適用して保存できます。段位は手動合格時に改善し、画面からは直接編集しません。
名前・称号・段位などのJSON編集方法、素材の置き方、操作・確認手順は
[ネームプレートのガイド](tools/nameplate-check/README.md)を参照してください。

今回の追加チェックは79項目（エントリーの選択・決定と暗転中の回帰チェックを含む）、既存の記録等82項目・特訓50項目も成功しています。
Windows実機の入力・見た目・音声とNative AOT publishは未確認です。
ぷちキャラは実素材が未提供のため、基本読み込みの実装までで、動きと配置は追加確認が必要です。

## リポジトリと統合内容

現在のGitHub URLは [TakeRai2708/TaikoSim](https://github.com/TakeRai2708/TaikoSim) です。
希望するリポジトリ名は `TakeRai2708/TakeTaikoSimulator`。
今回の作業環境ではGitHub連携にリポジトリ改名機能がなく、ブラウザーも未ログインのため、
GitHub側の改名だけは未実施です。管理者が Settings → General → Repository name で変更できます。
リポジトリの公開設定は変更しないでください。

改名に先立ち、ユーザーの追加指示で未マージだった以下の既存作業を統合しました。
master と各ブランチのコミット履歴を保持しています。

- `codex/local-two-player-test`：2Pテスト、記録表示、選曲BGM、戻る音、オプションのクリック修正。
- `codex/training-review-scroll`：一時停止の小節移動、長押し、早可・遅可、背景継続。
- `codex/known-issues`：問題点共有メモ。古い調査内容は当時の対象コミットを明記。

2P画面は既存のテスト配置のままです。新しいUIやオーディオエンジンは追加していません。

## 今回の検証

- 統合後・改名前後のソースビルド：0エラー（既存253警告）。
- 実際のWindows向けcsproj、`win-x64`、既定のAOT設定でのbuild：0エラー、既存CS0162警告1件。
  Windows用 `TakeTaikoSimulator.exe` の生成を確認しました。
- 記録・2P・BGM 82項目、特訓50項目が改名前後で成功。
- [改名互換性チェック](tools/rename-check/README.md) 22項目が成功。
- 段位条件の既存 `--requirements-test` は変更前と変更後で同じ失敗があります。
  詳細は [KNOWN-ISSUES.txt](KNOWN-ISSUES.txt)。今回そのゲーム処理は変更していません。

**Windows実機での起動・描画・入力・音声と、Native AOT publish は未確認です。**
Linuxでのbuild成功やWindows形式のexe生成は、これらの動作確認を意味しません。
配布物にはexeだけでなく、従来どおり必要なDLL・フォント・スキン等も含めてください。

## 画面別フォルダ整理の検証（2026-11-12）

32ファイルを画面別フォルダへ移動しました。今回のC#本文の変更は、検索の星数欄の
「すべての★」を「すべて」にした2箇所だけです。全120個のC#ファイルについて、
この文言変更を除き、整理前とバイト単位で一致することを確認しています。
13個のプロジェクト・ソリューションファイルも不変です。

既存の再帰コンパイル対象と案内のリンク・パスを静的確認しました。
現在の作業環境には.NET SDKがなく、今回の整理後のビルド・Windows実機・Native AOT publishは未確認です。
上のビルド成功記録は、過去の改名・統合作業の結果です。
