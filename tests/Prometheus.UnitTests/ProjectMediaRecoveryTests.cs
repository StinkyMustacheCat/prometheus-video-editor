using System;
using System.IO;
using System.Linq;
using Prometheus.Core.Model;
using Prometheus.Core.Storage;
using Xunit;

namespace Prometheus.UnitTests;

public class ProjectMediaRecoveryTests
{
    [Fact]
    public void MovedProjectFindsLocalAndWebFilesInItsAssetsFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "Prometheus-relink-" + Guid.NewGuid().ToString("N"));
        var projectFile = Path.Combine(root, "Edicion.pthproj");
        var media = Path.Combine(ProjectStorage.GetDataDirectory(projectFile), "medios", "web");
        Directory.CreateDirectory(media);
        var localFile = Path.Combine(media, "imagen.png");
        var webFile = Path.Combine(media, "web-id-1234.jpg");
        File.WriteAllBytes(localFile, [1, 2, 3]);
        File.WriteAllBytes(webFile, [4, 5, 6, 7]);
        try
        {
            var project = ProjectStore.CreateNew();
            var local = new Asset { Id = "local", Name = "Imagen",
                Source = new MediaSource { Kind = "local", Uri = "C:\\antiguo\\imagen.png" },
                Representations = [new MediaRepresentation { Role = "original", SizeBytes = 3,
                    Locator = new Locator("C:\\antiguo\\imagen.png") }] };
            var web = new Asset { Id = "web-id", Name = "Web",
                Source = new MediaSource { Kind = "web", Uri = "https://example.com/image" },
                Representations = [new MediaRepresentation { Role = "final", SizeBytes = 4,
                    Locator = new Locator("C:\\antiguo\\Edicion.assets\\medios\\web\\web-id-1234.jpg") }] };
            project.Assets.AddRange([local, web]);

            var recovered = ProjectMediaRecovery.RecoverFromProjectAssets(project, projectFile);

            Assert.Equal(2, recovered.Count);
            Assert.Equal(localFile, local.EffectiveLocator!.Path);
            Assert.Equal(localFile, local.Source.Uri);
            Assert.Equal(webFile, web.EffectiveLocator!.Path);
            Assert.Equal("https://example.com/image", web.Source.Uri);
            Assert.False(ProjectMediaRecovery.NeedsRecovery(local, projectFile));
        }
        finally
        {
            File.Delete(localFile); File.Delete(webFile);
            Directory.Delete(media);
            Directory.Delete(Path.GetDirectoryName(media)!);
            Directory.Delete(ProjectStorage.GetDataDirectory(projectFile));
            Directory.Delete(root);
        }
    }

    [Fact]
    public void SameNameAndSizeInTwoFoldersIsNotChosenArbitrarily()
    {
        var asset = new Asset { Source = new MediaSource { Kind = "local", Uri = "C:\\old\\clip.mp4" },
            Representations = [new MediaRepresentation { Role = "original", SizeBytes = 10,
                Locator = new Locator("C:\\old\\clip.mp4") }] };
        var candidates = new[] { "D:\\one\\clip.mp4", "D:\\two\\clip.mp4" };
        // File size is checked before matching, so provide actual temporary files.
        var root = Path.Combine(Path.GetTempPath(), "Prometheus-ambiguous-" + Guid.NewGuid().ToString("N"));
        var one = Path.Combine(root, "one"); var two = Path.Combine(root, "two");
        Directory.CreateDirectory(one); Directory.CreateDirectory(two);
        var first = Path.Combine(one, "clip.mp4"); var second = Path.Combine(two, "clip.mp4");
        File.WriteAllBytes(first, new byte[10]); File.WriteAllBytes(second, new byte[10]);
        try { Assert.Null(ProjectMediaRecovery.FindUnique(asset, [first, second], root)); }
        finally
        {
            File.Delete(first); File.Delete(second);
            Directory.Delete(one); Directory.Delete(two); Directory.Delete(root);
        }
    }
}
