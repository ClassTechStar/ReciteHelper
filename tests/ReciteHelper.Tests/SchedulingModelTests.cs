using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.Configuration;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Enums;
using ReciteHelper.Core.Scheduling;
using ReciteHelper.Core.ValueObjects;
using Xunit;

namespace ReciteHelper.Tests;

public class ModelAccessTests
{
    private static ConfigOptions With(
        string? deepSeek = null,
        string? qwen = null,
        string? openRouter = null,
        string? licenseId = null,
        string? licenseCode = null)
    {
        return new ConfigOptions
        {
            DeepSeekKey = deepSeek,
            QwenKey = qwen,
            OpenRouterKey = openRouter,
            HostedLicenseId = licenseId,
            HostedLicenseCode = licenseCode
        };
    }

    [Fact]
    public void Resolve_NullConfig_IsNone()
    {
        Assert.Equal(ModelAccessMode.None, ModelAccess.Resolve(null));
    }

    [Fact]
    public void Resolve_EmptyConfig_IsNone()
    {
        Assert.Equal(ModelAccessMode.None, ModelAccess.Resolve(With()));
    }

    [Fact]
    public void Resolve_DeepSeekAndQwenKeys_IsDirectMode()
    {
        Assert.Equal(ModelAccessMode.DeepSeekAndQwen, ModelAccess.Resolve(With("sk", "qw")));
    }

    [Fact]
    public void Resolve_DeepSeekWithoutQwen_FallsThroughToOpenRouterOrHosted()
    {
        // Direct mode requires BOTH keys; a lone DeepSeek key must not claim it.
        Assert.Equal(ModelAccessMode.None, ModelAccess.Resolve(With(deepSeek: "sk")));
        Assert.Equal(
            ModelAccessMode.OpenRouter,
            ModelAccess.Resolve(With(deepSeek: "sk", openRouter: "or")));
    }

    [Fact]
    public void Resolve_OpenRouterOnly_IsOpenRouter()
    {
        Assert.Equal(ModelAccessMode.OpenRouter, ModelAccess.Resolve(With(openRouter: "or")));
    }

    [Theory]
    [InlineData("license-id", null)]
    [InlineData(null, "license-code")]
    public void Resolve_HostedLicenseVariants_IsHosted(string? licenseId, string? licenseCode)
    {
        Assert.Equal(ModelAccessMode.Hosted, ModelAccess.Resolve(With(licenseId: licenseId, licenseCode: licenseCode)));
    }

    [Fact]
    public void HasTextGeneration_MatchesResolveNone()
    {
        Assert.False(ModelAccess.HasTextGeneration(With()));
        Assert.True(ModelAccess.HasTextGeneration(With(openRouter: "or")));
        Assert.True(ModelAccess.HasTextGeneration(With("sk", "qw")));
    }
}

public class LegacyReviewMigratorTests
{
    private static Question QuestionWithTags(params (int Quality, DateTime Time)[] reviews)
    {
        var question = new Question
        {
            Type = QuestionType.Essay,
            Text = "题干",
            CorrectAnswer = "答案"
        };
        foreach (var (quality, time) in reviews)
        {
            var tag = new ReviewTag { QValue = quality, Time = time };
            tag.SetId(question.ReviewTag.Count + 1);
            question.ReviewTag.Add(tag);
        }

        return question;
    }

    [Fact]
    public void Replay_EmptyHistory_ReturnsNull()
    {
        var question = QuestionWithTags();
        Assert.Null(LegacyReviewMigrator.Replay(question.ReviewTag, SchedulerParameters.Default));
    }

    [Fact]
    public void Replay_RebuildsRepsAndLapsesFromLegacyQualityValues()
    {
        // q >= 3 counts as recall, q < 3 as a lapse (SM-2 semantics).
        var question = QuestionWithTags(
            (5, new DateTime(2026, 1, 1, 12, 0, 0)),
            (4, new DateTime(2026, 1, 3, 12, 0, 0)),
            (1, new DateTime(2026, 1, 20, 12, 0, 0)),
            (3, new DateTime(2026, 1, 21, 12, 0, 0)));

        var record = LegacyReviewMigrator.Replay(question.ReviewTag, SchedulerParameters.Default);

        Assert.NotNull(record);
        Assert.Equal(4, record!.Reps);
        Assert.Equal(1, record.Lapses);
        Assert.Equal(MemoryRecord.CurrentAlgorithm, record.Algorithm);
        Assert.InRange(record.Stability, FsrsAlgorithm.MinStability, FsrsAlgorithm.MaxStability);
        Assert.InRange(record.Difficulty, FsrsAlgorithm.MinDifficulty, FsrsAlgorithm.MaxDifficulty);
    }

    [Fact]
    public void Replay_OutOfOrderTags_AreAppliedChronologically()
    {
        var question = QuestionWithTags(
            (4, new DateTime(2026, 1, 10, 12, 0, 0)),
            (5, new DateTime(2026, 1, 2, 12, 0, 0)));

        var record = LegacyReviewMigrator.Replay(question.ReviewTag, SchedulerParameters.Default);

        Assert.NotNull(record);
        Assert.Equal(2, record!.Reps);
        Assert.Equal(new DateTime(2026, 1, 10, 12, 0, 0), record.LastReview);
    }

    [Fact]
    public void EnsureMemory_CreatesOnce_AndNeverOverwritesExistingState()
    {
        var question = QuestionWithTags((5, new DateTime(2026, 1, 1, 12, 0, 0)));
        var parameters = SchedulerParameters.Default;

        Assert.True(LegacyReviewMigrator.EnsureMemory(question, parameters));
        Assert.NotNull(question.Memory);

        var firstState = question.Memory!;
        Assert.False(LegacyReviewMigrator.EnsureMemory(question, parameters));
        Assert.Same(firstState, question.Memory);
    }
}

public class SchedulerParametersValidationTests
{
    [Fact]
    public void Default_HasTwentyOneFiniteWeights()
    {
        Assert.Equal(21, SchedulerParameters.Count);
        for (var i = 0; i < SchedulerParameters.Count; i++)
        {
            var weight = SchedulerParameters.Default[i];
            Assert.True(double.IsFinite(weight), $"w{i} must be finite");
        }
    }

    [Fact]
    public void Constructor_RejectsWrongWeightCount()
    {
        Assert.Throws<ArgumentException>(() => new SchedulerParameters([1.0, 2.0]));
    }

    [Fact]
    public void Constructor_RejectsNonFiniteWeights()
    {
        var weights = Enumerable.Repeat(1.0, SchedulerParameters.Count).ToArray();
        weights[7] = double.NaN;
        Assert.Throws<ArgumentException>(() => new SchedulerParameters(weights));
    }
}

public class QuestionMemorySerializationTests
{
    [Fact]
    public void Project_RoundTrip_PreservesSchemaVersionAndMemoryState()
    {
        var project = new Project
        {
            ProjectName = "序列化测试",
            StoragePath = Path.Combine(Path.GetTempPath(), "rh-schema-test"),
            Chapters = [new Chapter { Name = "第一章", Number = 1, Questions = [], KnowledgePoints = [] }]
        };
        project.Chapters![0].Questions!.Add(new Question
        {
            Type = QuestionType.Essay,
            Text = "题干",
            CorrectAnswer = "答案",
            Memory = new MemoryRecord
            {
                Stability = 3.42,
                Difficulty = 5.17,
                LastReview = new DateTime(2026, 11, 6, 12, 0, 0),
                Reps = 4,
                Lapses = 1
            }
        });

        var json = System.Text.Json.JsonSerializer.Serialize(project);
        var restored = System.Text.Json.JsonSerializer.Deserialize<Project>(json);

        Assert.NotNull(restored);
        var question = restored!.Chapters![0].Questions![0];
        Assert.NotNull(question.Memory);
        Assert.Equal(3.42, question.Memory!.Stability, 9);
        Assert.Equal(5.17, question.Memory.Difficulty, 9);
        Assert.Equal(4, question.Memory.Reps);
        Assert.Equal(1, question.Memory.Lapses);
        Assert.Equal("FSRS-6", question.Memory.Algorithm);
    }
}
