using Prometheus.Core.Model;
using Prometheus.Core.Storage;
using Xunit;

namespace Prometheus.UnitTests;

public class MediaFolderTests
{
    [Fact]
    public void FoldersAndAssignmentsSurviveProjectSerialization()
    {
        var project = ProjectStore.CreateNew();
        project.MediaFolders.Add(new MediaFolder { Id = "folder1", Name = "Entrevistas" });
        project.Assets.Add(new Asset { Id = "asset1", Name = "clip.mp4", FolderId = "folder1" });

        var loaded = ProjectStore.Deserialize(ProjectStore.Serialize(project));

        Assert.Equal("Entrevistas", loaded.MediaFolders[0].Name);
        Assert.Equal("folder1", loaded.Assets[0].FolderId);
    }
}
