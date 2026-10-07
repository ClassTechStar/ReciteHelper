using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Enums;
using ReciteHelper.Core.ValueObjects;
using System.Text.RegularExpressions;

namespace ReciteHelper.Core.Services;

/// <summary>
/// Post-processing for LLM-generated questions: repairs split choice options,
/// normalizes question types, drops malformed items and converts declarative
/// sentences into fill-blank questions (extracted from ProjectCreationService).
/// </summary>
public static partial class GeneratedQuestionNormalizer
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    public static void Normalize(List<Chapter> chapters)
    {
        foreach (var chapter in chapters)
        {
            if (chapter.Questions is null || chapter.Questions.Count == 0)
                continue;

            RepairSplitChoiceOptions(chapter.Questions);
            NormalizeMalformedChoiceQuestions(chapter.Questions);
            NormalizeGeneratedQuestionTypes(chapter.Questions);
            RemoveDeclarativeShortAnswerQuestions(chapter);
        }
    }

    private static void RemoveDeclarativeShortAnswerQuestions(Chapter chapter)
    {
        if (chapter.Questions is null || chapter.Questions.Count == 0)
            return;

        chapter.KnowledgePoints ??= [];
        var validQuestions = new List<Question>();

        foreach (var question in chapter.Questions)
        {
            question.Text = question.Text?.Trim();
            question.CorrectAnswer = question.CorrectAnswer?.Trim();

            if (string.IsNullOrWhiteSpace(question.Text))
                continue;

            if (question.IsSingleChoice || IsValidGeneratedQuestion(question))
            {
                validQuestions.Add(question);
                continue;
            }

            if (TryConvertDeclarativeSentenceToBlank(question))
            {
                validQuestions.Add(question);
                continue;
            }

            chapter.KnowledgePoints.Add(KnowledgePoint.Create(
                CreateKnowledgePointName(question.Text),
                question.Text));
        }

        chapter.Questions = validQuestions;
    }

    private static bool IsValidGeneratedQuestion(Question question)
    {
        if (string.IsNullOrWhiteSpace(question.CorrectAnswer) && question.GetCorrectAnswers().Count == 0)
            return false;

        return question.Type switch
        {
            QuestionType.FillBlank =>
                BlankRegex().Matches(question.Text ?? string.Empty).Count is var blankCount &&
                blankCount > 0 &&
                blankCount == question.GetCorrectAnswers().Count,
            QuestionType.TermDefinition => (question.Text ?? string.Empty).StartsWith("名词解释", StringComparison.Ordinal),
            QuestionType.Essay => IsValidShortAnswerStem(question.Text ?? string.Empty),
            _ => false
        };
    }

    private static bool IsValidShortAnswerStem(string text)
    {
        if (BlankRegex().IsMatch(text) || text.Contains('?') || text.Contains('？'))
            return true;

        return ShortAnswerPromptRegex().IsMatch(text);
    }

    private static bool TryConvertDeclarativeSentenceToBlank(Question question)
    {
        var text = question.Text?.Trim();
        var answer = question.CorrectAnswer?.Trim();
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(answer))
            return false;

        if (string.Equals(text, answer, StringComparison.OrdinalIgnoreCase))
            return false;

        var index = text.IndexOf(answer, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return false;

        var remainingLength = text.Length - answer.Length;
        if (answer.Length < 2 || remainingLength < 4)
            return false;

        question.Text = text.Remove(index, answer.Length).Insert(index, "________");
        question.Type = QuestionType.FillBlank;
        question.CorrectAnswers = [answer];
        return IsValidShortAnswerStem(question.Text);
    }

    private static void NormalizeGeneratedQuestionTypes(List<Question> questions)
    {
        questions.RemoveAll(question => question.Type == QuestionType.TrueFalse);

        foreach (var question in questions)
        {
            question.CorrectAnswer = question.CorrectAnswer?.Trim();
            question.CorrectAnswers = question.CorrectAnswers
                .Where(answer => !string.IsNullOrWhiteSpace(answer))
                .Select(answer => answer.Trim())
                .ToList();

            if (question.Type == QuestionType.FillBlank && question.CorrectAnswers.Count == 0 &&
                !string.IsNullOrWhiteSpace(question.CorrectAnswer))
            {
                question.CorrectAnswers = [question.CorrectAnswer];
            }

            if (question.Type == QuestionType.TermDefinition &&
                !string.IsNullOrWhiteSpace(question.Text) &&
                !question.Text.StartsWith("名词解释", StringComparison.Ordinal))
            {
                question.Text = $"名词解释：{question.Text.Trim().TrimEnd('。', '？', '?')}";
            }

            if (question.Type != QuestionType.SingleChoice)
            {
                question.Options = [];
                question.CorrectOptionIds = [];
            }

            if (question.Type != QuestionType.FillBlank)
                question.CorrectAnswers = [];
        }
    }

    private static void RepairSplitChoiceOptions(List<Question> questions)
    {
        for (var i = 0; i <= questions.Count - 5; i++)
        {
            var stem = questions[i];
            if (stem.Options.Count > 0 || LooksLikeOptionOnlyQuestion(stem, null, out _))
                continue;

            var parsedOptions = new List<QuestionOption>();
            var expectedIds = new[] { "A", "B", "C", "D" };
            var matched = true;

            for (var offset = 0; offset < expectedIds.Length; offset++)
            {
                var optionQuestion = questions[i + offset + 1];
                if (!LooksLikeOptionOnlyQuestion(optionQuestion, expectedIds[offset], out var optionText))
                {
                    matched = false;
                    break;
                }

                parsedOptions.Add(new QuestionOption
                {
                    Id = expectedIds[offset],
                    Text = optionText
                });
            }

            if (!matched)
                continue;

            var correctOptionIds = ResolveCorrectOptionIds(stem, parsedOptions);
            if (correctOptionIds.Count == 0)
                continue;

            stem.Type = QuestionType.SingleChoice;
            stem.Options = parsedOptions;
            stem.CorrectOptionIds = correctOptionIds;
            stem.CorrectAnswer = correctOptionIds[0];

            questions.RemoveRange(i + 1, 4);
        }
    }

    private static void NormalizeMalformedChoiceQuestions(List<Question> questions)
    {
        foreach (var question in questions.Where(question => question.Type == QuestionType.SingleChoice))
        {
            question.Options = question.Options
                .Where(option => !string.IsNullOrWhiteSpace(option.Id) && !string.IsNullOrWhiteSpace(option.Text))
                .GroupBy(option => QuestionOption.NormalizeId(option.Id))
                .Select(group => new QuestionOption
                {
                    Id = group.Key,
                    Text = group.First().Text.Trim()
                })
                .ToList();

            question.CorrectOptionIds = ResolveCorrectOptionIds(question, question.Options);

            if (question.Options.Count == 0 || question.CorrectOptionIds.Count == 0)
            {
                question.Type = QuestionType.Essay;
                question.Options = [];
                question.CorrectOptionIds = [];
            }
        }
    }

    private static List<string> ResolveCorrectOptionIds(Question question, List<QuestionOption> options)
    {
        var ids = question.GetCorrectOptionIds()
            .Where(id => options.Any(option => QuestionOption.NormalizeId(option.Id) == id))
            .ToList();

        if (ids.Count > 0)
            return ids;

        var correctAnswer = question.CorrectAnswer?.Trim();
        if (string.IsNullOrWhiteSpace(correctAnswer))
            return [];

        var matchingOption = options.FirstOrDefault(option =>
            string.Equals(option.Text.Trim(), correctAnswer, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(option.DisplayText.Trim(), correctAnswer, StringComparison.OrdinalIgnoreCase));

        return matchingOption is null ? [] : [QuestionOption.NormalizeId(matchingOption.Id)];
    }

    private static bool LooksLikeOptionOnlyQuestion(Question question, string? expectedId, out string optionText)
    {
        optionText = string.Empty;
        if (!string.IsNullOrWhiteSpace(question.CorrectAnswer) || question.Options.Count > 0)
            return false;

        var match = OptionOnlyRegex().Match(question.Text ?? string.Empty);
        if (!match.Success)
            return false;

        var optionId = QuestionOption.NormalizeId(match.Groups["id"].Value);
        if (!string.IsNullOrWhiteSpace(expectedId) && optionId != expectedId)
            return false;

        optionText = match.Groups["text"].Value.Trim();
        return !string.IsNullOrWhiteSpace(optionText);
    }

    [GeneratedRegex(@"^\s*\(?\s*(?<id>[A-Da-d])\s*\)?\s*[\.、:：\)]\s*(?<text>.+?)\s*$")]
    private static partial Regex OptionOnlyRegex();

    [GeneratedRegex(@"_{2,}|＿{2,}|-{3,}")]
    private static partial Regex BlankRegex();

    [GeneratedRegex(@"^\s*(名词解释|简述|说明|分析|比较|阐述|试述|论述|列举|举例|概括|描述|解释|指出|写出|回答|判断|计算|请|问)|(为什么|为何|如何|怎样|哪些|哪种|哪个|什么|是否|能否)")]
    private static partial Regex ShortAnswerPromptRegex();

    public static string CreateKnowledgePointName(string text)
    {
        var normalized = WhitespaceRegex().Replace(text, " ").Trim();
        return normalized.Length <= 32 ? normalized : normalized[..32];
    }
}
