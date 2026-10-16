using Xunit;

namespace Temhalo.Presentation.Tests;

public sealed class BitrateHistoryTests
{
    [Fact]
    public void CommandRefreshesDoNotDistortOneSecondRates()
    {
        var history = new BitrateHistory();
        history.Sample(0, 0, 0, 1, true);
        history.Sample(.1, 100_000, 50_000, 1, true);
        history.Sample(.5, 500_000, 250_000, 1, true);
        Assert.Empty(history.Points);
        history.Sample(1, 1_000_000, 500_000, 1, true);
        Assert.Equal(8, history.InputMbps);
        Assert.Equal(4, history.OutputMbps);
        Assert.Single(history.Points);
    }

    [Fact]
    public void ReconnectAndCounterResetCreateNoNegativeOrArtificialPeak()
    {
        var history = new BitrateHistory();
        history.Sample(0, 0, 0, 1, true);
        history.Sample(1, 1_000_000, 1_000_000, 1, true);
        history.Sample(2, 2_000_000, 0, 2, true);
        Assert.Equal(0, history.OutputMbps);
        Assert.Single(history.Points);
        history.Sample(3, 3_000_000, 1_000_000, 2, true);
        Assert.Equal(8, history.OutputMbps);
        history.Sample(4, 0, 0, 2, true);
        Assert.Equal(0, history.InputMbps);
        Assert.All(history.Points, point => Assert.InRange(point.InputMbps, 0, 8));
    }

    [Fact]
    public void HistoryRetainsOnlyLastMinuteAndNewSessionClearsIt()
    {
        var history = new BitrateHistory();
        for (int second = 0; second < 300; second++)
            history.Sample(second, second * 1_000_000L, second * 1_000_000L, 1, true);
        Assert.InRange(history.Points.Count, 1, 81);
        Assert.All(history.Points, point => Assert.InRange(point.Seconds, 239, 299));
        history.Sample(300, 0, 0, 1, false);
        history.Sample(0, 0, 0, 1, true);
        Assert.Empty(history.Points);
        Assert.Equal(0, history.InputMbps);
    }
}
