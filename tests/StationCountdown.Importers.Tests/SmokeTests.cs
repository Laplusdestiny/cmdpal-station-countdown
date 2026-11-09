using System.Reflection;

namespace StationCountdown.Importers.Tests;

// Task 8 で実際のテストが入るまで、テスト 0 件（MTP の終了コード 8）で CI が落ちないようにするための最小テスト。
public class SmokeTests
{
    [Fact]
    public void Importers_assembly_loads()
        => Assert.Equal("StationCountdown.Importers", Assembly.Load("StationCountdown.Importers").GetName().Name);
}
