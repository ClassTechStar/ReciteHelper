using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Enums;
using Xunit;

namespace ReciteHelper.Tests;

public class SuperMemoSchedulingTests
{
    [Fact]
    public void ApplyReviewOutcome_CorrectAnswer_AdvancesInterval()
    {
        var question = new Question { Type = QuestionType.Essay, Text = "题干", CorrectAnswer = "答案" };

        question.ApplyReviewOutcome(2.5, 4);
        Assert.Equal(1, question.IntervalDays);
        Assert.Equal(1, question.Repetitions);
        Assert.NotNull(question.NextReviewDate);

        question.ApplyReviewOutcome(2.5, 4);
        Assert.Equal(6, question.IntervalDays);
        Assert.Equal(2, question.Repetitions);

        question.ApplyReviewOutcome(2.5, 4);
        Assert.Equal(15, question.IntervalDays);
        Assert.Equal(3, question.Repetitions);
    }

    [Fact]
    public void ApplyReviewOutcome_FailedAnswer_ResetsSequence()
    {
        var question = new Question
        {
            Type = QuestionType.Essay,
            Repetitions = 3,
            IntervalDays = 20
        };

        question.ApplyReviewOutcome(1.8, 2);

        Assert.Equal(0, question.Repetitions);
        Assert.Equal(1, question.IntervalDays);
        Assert.InRange(question.EFValue, 1.3, 5.0);
    }

    [Fact]
    public void ApplyReviewOutcome_ClampsEFToValidRange()
    {
        var question = new Question { EFValue = 4.9 };

        question.ApplyReviewOutcome(9.9, 5);

        Assert.Equal(5.0, question.EFValue);
    }
}
