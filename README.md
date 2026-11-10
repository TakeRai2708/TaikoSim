# TakeTaikoSimulator

Windows / .NET 10、Raylib-cs、ManagedBass を使う個人用の太鼓シミュレーターです。
旧製品名は TaikoSim。第三者の曲・スキンを含め、このリポジトリは非公開のまま扱ってください。

## ビルド

Visual Studio または .NET 10 SDK で `TakeTaikoSimulator.slnx` を開きます。

```powershell
dotnet build TakeTaikoSimulator.slnx -r win-x64
dotnet publish TakeTaikoSimulator/TakeTaikoSimulator.csproj -r win-x64 -c Release
```

実行ファイル名は **`TakeTaikoSimulator.exe`**。Native AOT の設定は維持しています。
`dotnet build` は Native AOT 版の生成ではありません。AOT の publish は Windows と
Visual Studio の「C++ によるデスクトップ開発」が必要です。

互換性のため、ビルド出力先は従来の `TaikoSim/bin/` のままです。
例：`TaikoSim/bin/Debug/net10.0-windows/win-x64/TakeTaikoSimulator.exe`。
publish は `TaikoSim/bin/Release/net10.0-windows/win-x64/publish/` に出力します。
改名前と同じ構成・RID・出力先でビルドすれば、既存データを同じ場所から読みます。
構成やRIDを変えたときは別の出力先になるので、使用中のデータを引き継いでください。

Linux のソース確認は [tools/build-check/README.md](tools/build-check/README.md) を参照。

## 既存データの引き継ぎ

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
