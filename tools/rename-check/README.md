# 改名前後の互換性チェック

ゲームに組み込まない検証用プログラムです。改名前と改名後の
`tools/build-check/Check.csproj` の出力を比較します。
2つのチェックアウトには同じ統合済みソースを使い、改名だけを差分にしてください。

```sh
dotnet run --project tools/rename-check/RenameCheck.csproj -- \
  /path/to/before/tools/build-check/.bin/net10.0/Check.dll \
  /path/to/after/tools/build-check/.bin/net10.0/TakeTaikoSimulator.dll
```

22項目を確認します。

- 4設定・曲記録・曲キャッシュ・キャラキャッシュの7種類を、実際のAOT用
  `AppJsonContext` で読み書きし、保存内容を比較。比較用JSONの値が読み捨てられていないことも検証。
- 設定ファイル名4種類、設定・スクリーンショット保存先2種類、曲・キャッシュ保存先3種類。
- プレイヤーデータ1〜3の記録パス。
- アセンブリ名、旧名前空間の除去、型一覧が名前変更だけで対応すること。

リフレクションはこの検証ツールだけで使用します。本体のAOT設定やJSON処理は変更しません。
ゲーム、Raylib、BASSのネイティブ関数を起動しないため、Windowsでの動作確認の代わりにはなりません。
