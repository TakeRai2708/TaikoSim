using TakeTaikoSimulator.Charts;
int passed=0;
void Check(bool result,string name) { if(!result) throw new Exception(name); passed++; }
var song=new SongEntry { Title="ＡＢＣ",Subtitle="Artist",RelativeFolder="曲",Courses=new(){(CourseType.Easy,3),(CourseType.Oni,10),(CourseType.Edit,9)} };
var category=new SongCategory {Title="曲フォルダ",Genre="アニメ"};
bool Match(string text="",CourseType? course=null,int? stars=null,string? genre=null)=>SongSearch.MatchesFilters(song,category,SongSearch.Normalize(text),course,stars,genre);
Check(Match(),"条件なし");Check(Match("abc"),"全半角検索");Check(Match("Artist"),"アーティスト検索");Check(Match("曲"),"フォルダ検索");
Check(!Match("missing"),"文字不一致");Check(Match(course:CourseType.Oni),"難易度だけ");Check(Match(stars:9),"星だけ");Check(Match(genre:"アニメ"),"ジャンルだけ");
Check(Match(course:CourseType.Oni,stars:10),"同じ譜面で両条件一致");Check(!Match(course:CourseType.Oni,stars:3),"別譜面の星を混ぜない");
Check(!Match(course:CourseType.Normal),"存在しない難易度");Check(!Match(stars:8),"存在しない星");Check(!Match(genre:"J-POP"),"違うジャンル");
Check(Match("abc",CourseType.Edit,9,"アニメ"),"全条件");Check(!Match("abc",CourseType.Edit,10,"アニメ"),"裏を表と区別");
var loose=new SongCategory {Title="その他"};Check(SongSearch.MatchesFilters(song,loose,"",genre:"その他"),"ジャンル未設定ならカテゴリ名");
var empty=new SongEntry();Check(SongSearch.MatchesFilters(empty,loose,""),"条件なしは難易度なしも維持");Check(!SongSearch.MatchesFilters(empty,loose,"",stars:0),"星ゼロを条件なしと区別");
var zero=new SongEntry {Courses=new(){(CourseType.Easy,0)}};Check(SongSearch.MatchesFilters(zero,loose,"",stars:0),"星ゼロ指定");
Console.WriteLine($"PASS: {passed} search filter checks");
