using Prometheus.Client.Services;

namespace Prometheus.UnitTests;

public class DynamicPreviewBudgetTests
{
    [Fact]
    public void SustainedLoadLowersOneLevelAtATime()
    {
        var budget = new DynamicPreviewBudget();
        Feed(budget, 0, 420, 35);
        Assert.Equal(0, budget.OutputLongEdge(1920));

        Feed(budget, 440, 780, 35);
        Assert.Equal(1280, budget.OutputLongEdge(1920));

        Feed(budget, 800, 1260, 35);
        Assert.Equal(1, budget.Level);
        Feed(budget, 1280, 1540, 35);
        Assert.Equal(960, budget.OutputLongEdge(1920));
    }

    [Fact]
    public void RecoveryRequiresSustainedHeadroomAtEachLevel()
    {
        var budget = new DynamicPreviewBudget();
        Feed(budget, 0, 1400, 35);
        Assert.Equal(2, budget.Level);

        Feed(budget, 1420, 2800, 8);
        Assert.Equal(2, budget.Level);
        Feed(budget, 2820, 4500, 8);
        Assert.Equal(1, budget.Level);
        Feed(budget, 4520, 7800, 8);
        Assert.Equal(0, budget.Level);
    }

    [Fact]
    public void PausingResetsTheLevelAndASeekDiscardsOldPressure()
    {
        var budget = new DynamicPreviewBudget();
        Feed(budget, 0, 420, 35);
        budget.ResetEvidence();
        Feed(budget, 440, 1000, 35);
        Assert.Equal(0, budget.Level);

        Feed(budget, 1020, 1500, 35);
        Assert.Equal(1, budget.Level);
        budget.ResetEvidence();
        Assert.Equal(1, budget.Level);
        budget.Reset();
        Assert.Equal(0, budget.OutputLongEdge(1920));
    }

    [Fact]
    public void SlowSpikesAndSustainableWorkAtLowerFpsPreserveFullResolution()
    {
        var budget = new DynamicPreviewBudget();
        Feed(budget, 0, 3000, 24, fps: 30);
        budget.Observe(TimeSpan.FromMilliseconds(3020), TimeSpan.FromMilliseconds(240), TimeSpan.FromMilliseconds(12), 30);
        Feed(budget, 3040, 5000, 24, fps: 30);
        Assert.Equal(0, budget.Level);
    }

    [Fact]
    public void SlowSourceFramesWithCheapCompositionsPreserveDetail()
    {
        var budget = new DynamicPreviewBudget();
        Feed(budget, 0, 3000, 35, compositionMs: .8);
        Assert.Equal(0, budget.Level);
    }

    private static void Feed(DynamicPreviewBudget budget, int fromMs, int untilMs, int frameMs,
        double fps = 60, double? compositionMs = null)
    {
        var composition = TimeSpan.FromMilliseconds(compositionMs ?? frameMs * .25);
        for (var now = fromMs; now <= untilMs; now += 20)
            budget.Observe(TimeSpan.FromMilliseconds(now), TimeSpan.FromMilliseconds(frameMs), composition, fps);
    }
}
