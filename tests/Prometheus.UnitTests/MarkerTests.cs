using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;

namespace Prometheus.UnitTests;

public class MarkerTests
{
    [Fact]
    public void MarkerAndRangeSurviveSerializationAndMontageDuplication()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        var marker = MarkerOperations.Add(sequence, 2.008, "Inicio");
        MarkerOperations.SetIn(sequence, 1);
        MarkerOperations.SetOut(sequence, 5);

        var loaded = ProjectStore.Deserialize(ProjectStore.Serialize(project));
        var copy = SequenceOperations.Duplicate(loaded, loaded.ActiveSequenceId);

        Assert.Equal("Inicio", copy.Markers.Single().Name);
        Assert.NotEqual(marker.Id, copy.Markers.Single().Id);
        Assert.Equal(1, copy.MarkInSeconds);
        Assert.Equal(5, copy.MarkOutSeconds);
    }

    [Fact]
    public void ReversedRangeMarkClearsTheOtherBoundary()
    {
        var sequence = ProjectStore.CreateNew().ActiveSequence!;
        MarkerOperations.SetIn(sequence, 10);
        MarkerOperations.SetOut(sequence, 4);
        Assert.Null(sequence.MarkInSeconds);
        Assert.Equal(4, sequence.MarkOutSeconds);

        MarkerOperations.SetIn(sequence, 2);
        Assert.Equal(2, sequence.MarkInSeconds);
        Assert.Equal(4, sequence.MarkOutSeconds);
    }
}
