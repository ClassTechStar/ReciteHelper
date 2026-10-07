using ReciteHelper.Core.Entities;
using ReciteHelper.Core.ValueObjects;
using Xunit;

namespace ReciteHelper.Tests;

public class FileVectorStoreTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"ReciteHelperVectorStoreTests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, true);
        }
        catch (IOException)
        {
        }
    }

    private FileVectorStore CreateStore(out string path)
    {
        Directory.CreateDirectory(_tempDirectory);
        path = Path.Combine(_tempDirectory, "knowledge-base.json");
        return new FileVectorStore(path);
    }

    [Fact]
    public void Search_ExcludesEntriesWithMismatchedDimensions()
    {
        // A store built with one embedding model must not rank entries from another
        // model (different vector dimension) — they would produce garbage scores.
        var store = CreateStore(out _);
        store.Add(new VectorEntry
        {
            Id = 0,
            Text = "三维条目",
            Vector = [1f, 0f, 0f],
            SourceFile = "test",
            Semantics = new Semantics { Id = 0 }
        });
        store.Add(new VectorEntry
        {
            Id = 1,
            Text = "二维条目",
            Vector = [0.9f, 0.1f],
            SourceFile = "test",
            Semantics = new Semantics { Id = 1 }
        });

        var results = store.Search([1f, 0f, 0.01f], topK: 5);

        var entry = Assert.Single(results);
        Assert.Equal("三维条目", entry.Entry.Text);
    }

    [Fact]
    public void Search_RanksByCosineSimilarity()
    {
        var store = CreateStore(out _);
        store.Add(new VectorEntry { Id = 0, Text = "近", Vector = [1f, 0.1f],
            SourceFile = "test", Semantics = new Semantics { Id = 0 } });
        store.Add(new VectorEntry { Id = 1, Text = "远", Vector = [-1f, 0.1f],
            SourceFile = "test", Semantics = new Semantics { Id = 1 } });

        var results = store.Search([1f, 0f], topK: 2);

        Assert.Equal("近", results[0].Entry.Text);
        Assert.True(results[0].Score > results[1].Score);
    }

    [Fact]
    public void Entries_SurviveSaveAndReload()
    {
        var store = CreateStore(out var path);
        store.Add(new VectorEntry
        {
            Id = 3,
            Text = "持久化条目",
            Vector = [0.5f, 0.5f],
            SourceFile = "test",
            Semantics = new Semantics { Id = 3, Summary = "摘要" }
        });

        var reloaded = new FileVectorStore(path);

        var entry = Assert.Single(reloaded.Entries);
        Assert.Equal(3, entry.Id);
        Assert.Equal("持久化条目", entry.Text);
        Assert.Equal("摘要", entry.Semantics.Summary);
    }
}
