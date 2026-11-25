using Prometheus.Client.Services;

namespace Prometheus.UnitTests;

public class RecentProjectStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "recent-projects-" + Guid.NewGuid().ToString("N"));
    private string StorePath => Path.Combine(_directory, "recent.json");
    private string ProjectPath(int id) => Path.Combine(_directory, id + ".pthproj");

    [Fact]
    public void RecentProjectsPersistDeduplicateAndMoveMostRecentFirst()
    {
        var store = new RecentProjectStore(StorePath);
        for (var i = 0; i < 12; i++) store.Record(ProjectPath(i));
        store.Record(ProjectPath(5));
        var reloaded = new RecentProjectStore(StorePath).List();
        Assert.Equal(10, reloaded.Count);
        Assert.Equal(ProjectPath(5), reloaded[0]);
        Assert.Equal(ProjectPath(11), reloaded[1]);
        Assert.Single(reloaded.Where(p => p == ProjectPath(5)));
        Assert.DoesNotContain(ProjectPath(0), reloaded);
    }

    [Fact]
    public void MissingProjectRemainsListedWithoutCreatingAnyProjectFile()
    {
        var store = new RecentProjectStore(StorePath);
        store.Record(ProjectPath(1));
        Assert.Equal(ProjectPath(1), Assert.Single(store.List()));
        Assert.False(File.Exists(ProjectPath(1)));
        store.Clear();
        Assert.Empty(new RecentProjectStore(StorePath).List());
    }

    [Fact]
    public void DamagedPreferencesRecoverWithoutLosingNewEntry()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(StorePath, "broken json");
        var store = new RecentProjectStore(StorePath);
        Assert.Empty(store.List());
        store.Record(ProjectPath(2));
        Assert.Equal(ProjectPath(2), Assert.Single(store.List()));
        Assert.Throws<ArgumentException>(() => store.Record(Path.Combine(_directory, "not-a-project.txt")));
        Assert.Equal(ProjectPath(2), Assert.Single(store.List()));
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
