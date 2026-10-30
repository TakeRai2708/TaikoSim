# dan-check — 段位ファイルのパーサを実データで試す道具

`TaikoSim/Charts/Dan.cs`・`DanJson.cs`・`ChartText.cs` を、実際の段位フォルダに対して動かして、
読めた中身をそのまま表示します。ゲーム本体には入りません。

```
dotnet run --project tools/dan-check -- "<dan.def の入っているフォルダ>"
```

例：

```
dotnet run --project tools/dan-check -- "C:\Users\<名前>\Documents\TaikoNauts-latest\Songs\10 段位道場"
```

出てくるもの：カテゴリ名・練習チート禁止の有無・段位ごとの課題曲（ファイルが実在するかも出す）・
ゲージ条件・合格条件（全体か曲ごとか、以上か未満かも出す）・画像の有無・解禁条件・曲間演出。

`Shim.cs` には `CourseType` だけ写してあります（`Charts/Chart.cs` を丸ごと持ってくると
Raylib が必要になるため）。**`Chart.cs` 側で並びを変えたら、こっちも直してください。**
