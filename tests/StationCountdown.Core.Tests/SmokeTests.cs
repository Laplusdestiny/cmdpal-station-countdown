namespace StationCountdown.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void Core_assembly_loads() => Assert.NotNull(typeof(StationCountdown.Core.Model.DayType));
}
