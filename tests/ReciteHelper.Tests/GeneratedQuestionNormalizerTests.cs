using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Enums;
using ReciteHelper.Core.ValueObjects;
using ReciteHelper.Core.Services;
using Xunit;

namespace ReciteHelper.Tests;

public class GeneratedQuestionNormalizerTests
{
    [Fact]
    public void Normalize_RejoinsChoiceQuestionSplitIntoStemAndOptions()
    {
        // The LLM sometimes splits "stem + A/B/C/D" into five separate questions.
        var chapter = new Chapter
        {
            Name = "章节",
            Questions =
            [
                new Question { Type = QuestionType.Essay, Text = "光合作用的场所是？", CorrectAnswer = "A" },
                new Question { Type = QuestionType.Essay, Text = "A. 叶绿体" },
                new Question { Type = QuestionType.Essay, Text = "B. 线粒体" },
                new Question { Type = QuestionType.Essay, Text = "C. 核糖体" },
                new Question { Type = QuestionType.Essay, Text = "D. 细胞核" }
            ]
        };

        GeneratedQuestionNormalizer.Normalize([chapter]);

        var question = Assert.Single(chapter.Questions);
        Assert.Equal(QuestionType.SingleChoice, question.Type);
        Assert.Equal(4, question.Options.Count);
        Assert.Equal("A", question.GetCorrectOptionIds().Single());
    }

    [Fact]
    public void Normalize_RemovesTrueFalseQuestions()
    {
        var chapter = new Chapter
        {
            Name = "章节",
            Questions =
            [
                new Question { Type = QuestionType.TrueFalse, Text = "判断题", CorrectAnswer = "正确" },
                new Question { Type = QuestionType.TermDefinition, Text = "名词解释：光合作用", CorrectAnswer = "定义" }
            ]
        };

        GeneratedQuestionNormalizer.Normalize([chapter]);

        var question = Assert.Single(chapter.Questions);
        Assert.Equal(QuestionType.TermDefinition, question.Type);
    }

    [Fact]
    public void CreateKnowledgePointName_TruncatesLongNames()
    {
        var longText = new string('知', 60);

        var name = GeneratedQuestionNormalizer.CreateKnowledgePointName(longText);

        Assert.Equal(32, name.Length);
    }
}
