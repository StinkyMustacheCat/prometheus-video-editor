using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Prometheus.Client.Services;
using Prometheus.Core.Storage;
using Prometheus.Core.Undo;
using Xunit;

namespace Prometheus.UnitTests;

public sealed class ProjectHistoryTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("prometheus-history-");

    [Fact]
    public void LockedHistoryKeepsItsRowsWhenAnotherProjectIsSaved()
    {
        var oldProject = ProjectPath("Original");
        var otherProject = ProjectPath("Otro");
        var history = new ProjectHistory();
        history.Add(oldProject, 1, "Primera edición", null);
        history.Flush();

        using (File.Open(Csv(oldProject), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            history.Add(oldProject, 2, "Segunda edición", null);
            history.Flush();
            Assert.True(history.HasPending);
            history.DiscardUnsaved();
            history.Add(otherProject, 1, "Edición de otro proyecto", null);
            history.FlushProject(otherProject);
            Assert.Equal(2, File.ReadAllLines(Csv(otherProject)).Length);
        }

        history.Flush();
        Assert.False(history.HasPending);
        var rows = File.ReadAllLines(Csv(oldProject));
        Assert.Equal(3, rows.Length);
        Assert.Contains("Segunda edición", rows[2]);
        Assert.DoesNotContain("otro proyecto", string.Join("\n", rows));
    }

    [Fact]
    public void FirstSaveAndSaveAsKeepEarlierRowsAndCsvEscaping()
    {
        var original = ProjectPath("Original");
        var copy = ProjectPath("Copia");
        var history = new ProjectHistory();
        history.Add(null, 0, "Crear proyecto", null);
        history.AdoptUnsaved(original);
        history.Add(original, 1, "Guardar proyecto", null);
        history.CopyTo(original, copy);
        history.Add(copy, 2, "Editar texto", "Título, \"entre comillas\"\nsegunda línea");
        history.Flush();

        var oldRows = File.ReadAllLines(Csv(original));
        var newRows = File.ReadAllLines(Csv(copy));
        Assert.Equal(oldRows, newRows.Take(oldRows.Length).ToArray());
        Assert.Equal("Fecha,Revision,Origen,Accion,Detalle", newRows[0]);
        Assert.EndsWith("\"Título, \"\"entre comillas\"\" segunda línea\"", newRows[3]);
        Assert.Equal(4, newRows.Length);
    }

    [Fact]
    public void UndoAndRedoNameTheConfirmedOperationAndIgnoreUnchangedEdits()
    {
        var manager = new UndoRedoManager();
        var entries = new List<(string, string?)>();
        manager.ActionRecorded += (action, details) => entries.Add((action, details));

        manager.Commit(_ => { }, "Gestos sin cambio");
        manager.Commit(project => project.Name = "Nuevo montaje", "Renombrar", "Nuevo montaje");
        Assert.True(manager.Undo());
        Assert.True(manager.Redo());

        Assert.Equal(new (string, string?)[]
        {
            ("Renombrar", "Nuevo montaje"),
            ("Deshacer", "Renombrar · Nuevo montaje"),
            ("Rehacer", "Renombrar · Nuevo montaje")
        }, entries);
    }

    private string ProjectPath(string name)
    {
        var path = Path.Combine(_folder.FullName, name + ".pthproj");
        Directory.CreateDirectory(ProjectStorage.GetDataDirectory(path));
        return path;
    }

    private static string Csv(string projectPath) =>
        Path.Combine(ProjectStorage.GetDataDirectory(projectPath), "historial.csv");

    public void Dispose()
    {
        foreach (var directory in _folder.EnumerateDirectories())
        {
            foreach (var file in directory.EnumerateFiles()) file.Delete();
            directory.Delete();
        }
        _folder.Delete();
    }
}
