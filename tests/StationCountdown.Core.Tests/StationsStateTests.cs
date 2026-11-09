using StationCountdown.Core.Config;
using StationCountdown.Core.Model;

namespace StationCountdown.Core.Tests;

public class StationsStateTests
{
    [Fact]
    public void Initial_state_is_empty_without_errors()
    {
        var state = new StationsState();

        Assert.Same(StationsDocument.Empty, state.Current);
        Assert.Empty(state.Errors);
    }

    [Fact]
    public void Apply_ok_replaces_current_and_clears_errors()
    {
        var state = new StationsState();
        state.Apply(StationsFile.Parse("not: [valid"));
        Assert.NotEmpty(state.Errors);

        var good = StationsFile.Parse(StationsFileTests.Valid);
        state.Apply(good);

        Assert.Same(good.Value, state.Current);
        Assert.Empty(state.Errors);
    }

    [Fact]
    public void Apply_error_keeps_last_good_document()   // Review Focus 3
    {
        var state = new StationsState();
        var good = StationsFile.Parse(StationsFileTests.Valid);
        state.Apply(good);

        // エディタの保存途中に読んだ、空のファイルと途中で切れたファイル。
        foreach (var partial in new[] { "", StationsFileTests.Valid[..(StationsFileTests.Valid.Length / 2)] })
        {
            state.Apply(StationsFile.Parse(partial));

            Assert.Same(good.Value, state.Current);
            Assert.NotEmpty(state.Errors);
        }
    }

    [Fact]
    public void First_apply_error_leaves_empty_document()
    {
        var state = new StationsState();

        state.Apply(StationsFile.Parse("stations: oops"));

        Assert.Same(StationsDocument.Empty, state.Current);
        Assert.NotEmpty(state.Errors);
    }

    [Fact]
    public void Empty_document_has_defaults_and_no_stations()
    {
        Assert.Equal(new GlobalSettings(), StationsDocument.Empty.Settings);
        Assert.Empty(StationsDocument.Empty.Stations);
        Assert.Empty(StationsDocument.Empty.DayTypeOverrides);
    }
}
