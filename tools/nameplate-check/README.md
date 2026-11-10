# ネームプレート・着せ替えの確認

## 自動チェック

`tools/build-check/Check.csproj` をビルドしてから実行します。

```sh
dotnet run --project tools/nameplate-check/NamePlateCheck.csproj
dotnet run --project tools/record-check/RecordCheck.csproj
dotnet run --project tools/training-check/TrainingCheck.csproj
```

名前・キャラ設定のJSON、3データの分離、適用前のコピー、未知の設定の維持、
スコアファイルの維持、異常JSONと保存失敗、段位の昇格・格下げ防止などを確認します。
テストはテスト実行ファイルの隣のConfigだけを使い、元の内容があれば最後に戻します。
Raylib/BASSや実際の画面は起動しません。

追加のエントリー回帰チェックでは、実際のデータ確定処理を呼び出し、1Pのみ・2Pのみ・
2人の両参加順で、最後の確定後も暗転中のネームプレートが有効な参加者を参照することを確認します。
最後の参加者を確定した直後のIndexOutOfRangeExceptionを修正前に再現し、修正後に検証しています。

## 保存先と設定

新しい設定だけを `Config/PlayerData/1|2|3/` に保存します。
データ1のスコアは引き続き `Config/SongHistory.json`、データ2・3のスコアも従来どおりです。
TaikøNautsのNamePlateConfig.jsonの内容は、そのまま各データ枠へコピーできます。
この実装はTaikøNauts本体のPlayerDataフォルダやバイナリ記録を自動で移行しません。

`NamePlateConfig.json`：

```json
{
  "name": "プレイヤー1",
  "title": "",
  "rank": "",
  "isRankGold": false,
  "namePlateId": "00",
  "rankType": 0
}
```

- 初めてそのデータ枠を選ぶと名前入力。既存のスコアは消しません。
- 名前は24文字まで（結合文字・絵文字を途中で切らない）。称号は空なら文字を非表示。
- 名前・称号・段位などを外部編集したときは、エントリーのデータ選択へ戻るか再起動して読み直します。
- 段位3項目の画面編集はありません。`isRankGold`は金合格、`rankType`は銀0・金1・虹2。
- 手動で段位に合格すると更新。通常合格は銀、フルコンボは金、全良は虹。
  上位段位を優先し、同段位では金合格・枠の色を改善。下位段位では格下げしません。
  オート・途中終了・不合格と、上下関係のない外伝では更新しません。
- 新しい段位へ上がったときは、その段位での結果にします。前の段位の金合格・虹を流用しません。
- 着せ替えで変更するのはプレートIDとキャラ選択。名前・称号・段位を保持します。

`PlayerConfig.json`は `donchanId`・`donchanType`、`isUsePuchiChara`・`puchiCharaId`・
`puchiCharaType` を保存します。IDはフォルダ名を優先し、旧インデックス形式にも対応。
既存ファイルの未知の項目は読み書き時に残します。

## スキン素材

- `Image/99.Common/NamePlate/NamePlate_Base.png`：338×560の共通シート。
- `NamePlate_Rank_Gold.png`、`Plates/<ID>/Base.png`または`Anime.aup2`、`PlateConfig.json`。
  adjustX/Yを読み、既存のAup2プレイヤーでアニメを表示します。
- どんちゃんは `Image/99.Common/Chara/<ID>/Config.json` と既存アニメフォルダ。
  3データで選んだキャラを必要な分だけ焼き、同じキャラのキャッシュは共有します。
- ぷちキャラは `Image/99.Common/PuchiChara/<ID>/` または
  `Image/99.Common/Cosmetic/PuchiChara/<ID>/` の `Chara.png` と `Config.json`。
  横並びのcharaImageCount、charaSizeWidth/Height、画面別の位置・倍率に対応。
  現時点では基本の4コマ/秒のループ。元動画のぷちキャラの動きと一致したとは扱いません。
- 素材がなければ一覧は空、ネームプレートはシステム表示。画像を必須にはしません。

参照した私有素材は、以前送付された09.zip中のNamePlateの279ファイル、
どんちゃん2種類の設定、TaikoNauts.zip中の演奏動画です。これらはコミットしません。
参照した09.zipにはぷちキャラ素材がありません。Windowsのtaikonauts-latestはこの環境から未確認です。

## Windowsで確認すること（未実施）

1. スコアが残っているデータで初めて名前入力し、名前保存後も記録が残ること。
   日本語変換のEnterでは進まず、次のEnterで確定。Escの入力取消が背面の決定にならないこと。
2. 3データで別名を保存。1P/2Pを逆のデータに割り当て、各画面で正しい名前になること。
3. JSONの6項目を編集し、再エントリーで反映。壊れたJSONは上書きせずエラーにすること。
4. モード選択の着せ替えへ。←→で種類、↑↓/カッ/ホイールで選択、Tabで参加側切替。
   行クリックはプレビュー、適用またはドン/Enterで保存。長い一覧にはスクロールバーがあります。
5. 未保存で戻ると保存確認。×/Escで確認だけを閉じる、破棄で戻る、保存して戻るが動くこと。
   保存先が書き込み不可のときは画面に留まり、解除して再度適用できること。
6. 1P/2Pで別のどんちゃんを選び、モード・選曲・2P演奏に別々のキャラが出ること。
   初回のキャッシュ生成中も画面が止まらず、スキン変更で古い画像が残らないこと。
7. プレート01等のaup2アニメとadjustYが負のプレート。素材なしスキンでも名前が読めること。
   1P/2Pの文字は反転せず、配置だけ反対側になること。
8. 手動で段位合格・金合格・FC・全良、同段位改善、下位段位、外伝、オート・失敗・途中終了を確認。
9. ぷちキャラ素材を配置して選択・無効化。画面別の座標を確認。
   ぷちキャラは実素材が未確認なので、動き・座標は追加検証が必要です。
10. WindowsでNative AOT publish。日本語入力・描画・実際の音声とあわせて実機確認してください。
