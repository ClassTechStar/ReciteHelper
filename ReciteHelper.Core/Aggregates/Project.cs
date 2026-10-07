using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Scheduling;
using ReciteHelper.Core.ValueObjects;
using ReciteHelper.SharedKernel;
using System.Text.Json.Serialization;

namespace ReciteHelper.Core.Aggregates;

public class Project : AggregateRoot
{
    // Bump when the on-disk .rhproj layout changes in a backward-incompatible way
    // and add a migration branch in ProjectFileService.MigrateSchema.
    public const int CurrentSchemaVersion = 1;

    [JsonConstructor]
    public Project() { }

    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("name")]
    public string? ProjectName { get; set; }

    [JsonPropertyName("path")]
    public string? StoragePath { get; set; }

    [JsonPropertyName("bankfile")]
    public string? QuestionBankPath { get; set; }

    [JsonPropertyName("chapter")]
    public List<Chapter>? Chapters { get; set; }

    [JsonPropertyName("knowledge_base")]
    public string? KnowledgeBasePath { get; set; }

    [JsonPropertyName("knowledge_base_error")]
    public string? KnowledgeBaseBuildError { get; set; }

    [JsonIgnore]
    public FileVectorStore? KnowledgeBase { get; private set; }

    [JsonPropertyName("last_accessed")]
    public DateTime LastAccessed { get; private set; }

    /// <summary>
    /// Personalised FSRS-6 parameters fitted from this project's review history;
    /// null means the population defaults are used.
    /// </summary>
    [JsonPropertyName("scheduler_parameters")]
    public SchedulerParameters? SchedulerParameters { get; set; }

    /// <summary>Number of reviews the current <see cref="SchedulerParameters"/> were fitted on.</summary>
    [JsonPropertyName("scheduler_fit_reviews")]
    public int SchedulerFitReviews { get; set; }

    public List<Question> ExportQuestions()
    {
        List<Question> questions = [];

        foreach (var chapter in Chapters!)
            questions.AddRange(chapter.Questions!);
        return questions;
    }

    public void UpdateLastAccessed()
    {
        LastAccessed = DateTime.Now;
    }

    public void AttachKnowledgeBase(string relativePath, FileVectorStore store)
    {
        KnowledgeBasePath = relativePath;
        KnowledgeBase = store;
        KnowledgeBaseBuildError = null;
    }

    public void LoadKnowledgeBase(FileVectorStore store)
    {
        KnowledgeBase = store;
    }

    public void MarkKnowledgeBaseBuildFailed(string error)
    {
        KnowledgeBaseBuildError = error;
    }
}
