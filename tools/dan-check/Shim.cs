// Charts/Chart.cs を丸ごと持ってくると Raylib が要るので、CourseType だけ写す。
// 並びは TaikoSim/Charts/Chart.cs と必ず同じにすること。
namespace TaikoSim.Charts
{
    public enum CourseType { Easy = 0, Normal = 1, Hard = 2, Oni = 3, Edit = 4, Tower = 5, Dan = 6 }
}
