using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.ValueObjects;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace ReciteHelper.Infrastructure.Services;

public sealed class GalGameCreationService : IGalGameCreationService
{
    private const string Instructions = "You are a writer who excels at creating moving and touching screenplays.";
    private const int MaxChapterAttempts = 2;

    private readonly IProjectFileService _projectFileService;
    private readonly IGalGameService _galGameService;
    private readonly IPromptProvider _promptProvider;
    private readonly IAiChatService _aiChatService;

    public GalGameCreationService(
        IProjectFileService projectFileService,
        IGalGameService galGameService,
        IPromptProvider promptProvider,
        IAiChatService aiChatService)
    {
        _projectFileService = projectFileService;
        _galGameService = galGameService;
        _promptProvider = promptProvider;
        _aiChatService = aiChatService;
    }

    public async Task CreateAsync(string projectPath, string deepSeekKey)
    {
        var project = await _projectFileService.OpenProjectAsync(projectPath)
            ?? throw new InvalidOperationException("无法读取项目文件。");

        var chapterQuestions = new Dictionary<string, StringBuilder>();
        var chapterNames = new StringBuilder();

        foreach (var chapter in project.Chapters ?? [])
        {
            if (chapter.Name is null)
                continue;

            var singleChapter = new StringBuilder();

            foreach (var question in chapter.Questions ?? [])
                singleChapter.AppendLine($"问题：{question.Text} 答案：{question.CorrectAnswer}");

            chapterNames.AppendLine(chapter.Name);
            // Duplicate chapter names would previously crash with a duplicate-key error.
            chapterQuestions.TryAdd(chapter.Name, singleChapter);
        }

        var clusterPrompt = await _promptProvider.GetPromptAsync("ReClustering.txt");
        var clusterResponse = await _aiChatService.RunAsync(deepSeekKey, $"{clusterPrompt}\n{chapterNames}", Instructions);
        var clusterJson = CleanJson(clusterResponse);
        var clusterResult = JsonSerializer.Deserialize<List<ChapterCluster>>(clusterJson)
            ?? throw new InvalidOperationException("游戏章节聚类结果无法解析。");

        chapterNames.Clear();
        clusterResult.ForEach(cluster => chapterNames.Append($"{cluster.UnifiedName}/"));

        var outlinePrompt = await _promptProvider.GetPromptAsync("GenerateOutline.txt");
        var outlineResponse = await _aiChatService.RunAsync(deepSeekKey, $"{outlinePrompt}\n{chapterNames}", Instructions);
        var chapterList = JsonSerializer.Deserialize<List<GameChapterDto>>(CleanJson(outlineResponse))
            ?? throw new InvalidOperationException("游戏章节大纲无法解析。");

        if (chapterList.Count != clusterResult.Count)
            throw new InvalidOperationException(
                $"章节大纲数量（{chapterList.Count}）与聚类结果数量（{clusterResult.Count}）不一致，请重试。");

        var galPrompt = await _promptProvider.GetPromptAsync("GenerateGal.txt");
        var combined = chapterList.Zip(clusterResult, (first, second) => (first, second));
        var storyLines = new ConcurrentBag<object>();
        var failedChapters = 0;

        await Parallel.ForEachAsync(combined, async (it, _) =>
        {
            var chapter = it.first;
            var cluster = it.second;
            var builder = new StringBuilder();

            foreach (var item in cluster.Chapters ?? [])
            {
                if (chapterQuestions.TryGetValue(item, out var questions))
                    builder.AppendLine(questions.ToString());
            }

            var currentPrompt = galPrompt;
            currentPrompt += $"{chapter.GameChapterOutline}\n" +
                             "This is the content the user needs to review (but don't explicitly label the learning points in the story; let the user feel like they are learning naturally)." +
                             $"{builder}";

            // One bad chapter must not discard the whole game: retry each chapter
            // once, skip persistent failures and only fail when nothing compiled.
            for (var attempt = 1; attempt <= MaxChapterAttempts; attempt++)
            {
                try
                {
                    var galResponse = await _aiChatService.RunAsync(deepSeekKey, currentPrompt, Instructions);
                    var galCode = CleanCodeFence(galResponse);

                    var mainStoryLine = await _galGameService.CompileStoryAsync(galCode);
                    storyLines.Add(mainStoryLine);
                    break;
                }
                catch (Exception) when (attempt < MaxChapterAttempts)
                {
                    // First failure: retry once before giving up on this chapter.
                }
                catch (Exception)
                {
                    Interlocked.Increment(ref failedChapters);
                }
            }
        });

        if (storyLines.IsEmpty)
            throw new InvalidOperationException("所有游戏章节的剧本生成均失败，请检查模型配置后重试。");

        await _galGameService.SaveStoryLinesAsync(project, storyLines);

        if (failedChapters > 0)
            throw new InvalidOperationException(
                $"游戏已生成，但 {failedChapters} 个章节的剧本生成失败，已跳过这些章节。");
    }

    private static string CleanJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var trimmed = raw.Trim();
        var fenceStart = trimmed.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart >= 0)
        {
            var jsonStart = trimmed.IndexOf('\n', fenceStart);
            if (jsonStart >= 0)
            {
                var fenceEnd = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                trimmed = fenceEnd > jsonStart
                    ? trimmed[(jsonStart + 1)..fenceEnd].Trim()
                    : trimmed[(jsonStart + 1)..].Trim();
            }
        }

        var arrayStart = trimmed.IndexOf('[');
        var arrayEnd = trimmed.LastIndexOf(']');
        return arrayStart >= 0 && arrayEnd > arrayStart
            ? trimmed[arrayStart..(arrayEnd + 1)]
            : trimmed;
    }

    /// <summary>
    /// Strips markdown code fences from generated C# without the previous naive
    /// Replace("csharp", "") that also corrupted code containing that word.
    /// </summary>
    private static string CleanCodeFence(string raw)
    {
        var trimmed = (raw ?? string.Empty).Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineBreak = trimmed.IndexOf('\n');
            if (firstLineBreak > 0)
                trimmed = trimmed[(firstLineBreak + 1)..].Trim();
        }

        if (trimmed.EndsWith("```", StringComparison.Ordinal))
            trimmed = trimmed[..^3].Trim();

        return trimmed;
    }
}
