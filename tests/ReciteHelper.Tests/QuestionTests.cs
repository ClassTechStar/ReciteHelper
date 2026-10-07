using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Enums;
using ReciteHelper.Core.ValueObjects;
using Xunit;

namespace ReciteHelper.Tests;

public class QuestionTests
{
    [Theory]
    [InlineData("对", "正确")]
    [InlineData("√", "正确")]
    [InlineData("true", "正确")]
    [InlineData("否", "错误")]
    [InlineData("×", "错误")]
    [InlineData("X", "错误")]
    [InlineData("false", "错误")]
    public void NormalizeTrueFalseAnswer_MapsCommonVariants(string input, string expected)
    {
        Assert.Equal(expected, Question.NormalizeTrueFalseAnswer(input));
    }

    [Fact]
    public void ExtractOptionId_ReadsLeadingLetter()
    {
        Assert.Equal("A", Question.ExtractOptionId("A"));
        Assert.Equal("B", Question.ExtractOptionId("b. 某选项"));
        Assert.Equal(string.Empty, Question.ExtractOptionId("(C)"));
        Assert.Equal(string.Empty, Question.ExtractOptionId("选项内容"));
        Assert.Equal(string.Empty, Question.ExtractOptionId(null));
    }

    [Fact]
    public void IsCorrectChoiceAnswer_MatchesByOptionId()
    {
        var question = new Question
        {
            Type = QuestionType.SingleChoice,
            Text = "示例题干",
            Options =
            [
                new QuestionOption { Id = "A", Text = "选项一" },
                new QuestionOption { Id = "B", Text = "选项二" }
            ],
            CorrectOptionIds = ["B"]
        };

        Assert.True(question.IsCorrectChoiceAnswer("B"));
        Assert.True(question.IsCorrectChoiceAnswer("b"));
        Assert.False(question.IsCorrectChoiceAnswer("A"));
        Assert.False(question.IsCorrectChoiceAnswer(null));
    }

    [Fact]
    public void GetCorrectAnswers_FallsBackToCorrectAnswer()
    {
        var question = new Question { CorrectAnswer = "线粒体" };

        var answers = question.GetCorrectAnswers();

        Assert.Equal(["线粒体"], answers);
    }

    [Fact]
    public void JoinAndSplitBlankAnswers_RoundTrip()
    {
        var answers = new[] { "多基因", "加性", "显性" };

        var joined = Question.JoinBlankAnswers(answers);
        var split = Question.SplitBlankAnswers(joined);

        Assert.Equal(answers, split);
    }
}

public class SemanticsTests
{
    [Fact]
    public void Equality_IsBasedOnId_NotSummary()
    {
        // Summary-based equality used to silently drop knowledge points whose
        // summaries were identical (often both empty).
        var first = new Semantics { Id = 1, Summary = "" };
        var second = new Semantics { Id = 2, Summary = "" };

        Assert.NotEqual(first, second);
        Assert.Equal(new Semantics { Id = 7, Summary = "任意" }, new Semantics { Id = 7, Summary = "任意" });
    }
}
