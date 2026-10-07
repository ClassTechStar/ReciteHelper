using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.Entities;

namespace ReciteHelper.Application.Services;

public class ReviewGenerator : IReviewGenerator
{
    private readonly Random _random = new();

    public List<Question> GenerateReview(Project project, int count)
    {
        if (project == null) throw new ArgumentNullException(nameof(project));
        if (count <= 0) throw new ArgumentException("数量必须大于0", nameof(count));

        var allQuestions = GetAllQuestions(project);

        if (allQuestions.Count <= count)
            return ResetStatus(allQuestions.ToList());

        var result = allQuestions
            .OrderBy(DueOrder)
            .ThenBy(q => q.EFValue)
            .ThenBy(q => _random.Next())
            .Take(count)
            .ToList();

        return ResetStatus(result);
    }

    public List<Question> GenerateParameterizationReview(Project project, ReviewOptions options)
    {
        var allQuestions = GetAllQuestions(project);

        var weakPoints = allQuestions.Where(q => q.EFValue < 1.8).ToList();
        var learning = allQuestions.Where(q => q.EFValue >= 1.8 && q.EFValue < 2.3).ToList();
        var newQuestions = allQuestions.Where(q => q.ReviewTag.Count == 0).ToList();

        var result = new List<Question>();

        if (options.PrioritizeWeakPoints)
        {
            result.AddRange(weakPoints);
            result.AddRange(learning);
        }

        var newCount = (int)(options.TotalCount * options.NewQuestionRatio);
        result.AddRange(newQuestions.OrderBy(_ => _random.Next()).Take(newCount));

        return result
            .Distinct()
            .OrderBy(DueOrder)
            .ThenBy(q => q.EFValue)
            .Take(options.TotalCount)
            .ToList();
    }

    // SM-2 due-ness: questions without a scheduled date or already due come first;
    // items scheduled for the future follow, ordered by their next review date.
    private static int DueOrder(Question question)
    {
        if (question.NextReviewDate is null)
            return 0;
        return question.NextReviewDate.Value <= DateTime.Now ? 0 : 1;
    }


    private List<Question> GetAllQuestions(Project project)
    {
        return project.Chapters
            ?.SelectMany(c => c.Questions ?? new List<Question>())
            .ToList() ?? new List<Question>();
    }

    private static List<Question> ResetStatus(List<Question> questions)
    {
        questions.ForEach(question => question.Status = null);
        return questions;
    }
}
