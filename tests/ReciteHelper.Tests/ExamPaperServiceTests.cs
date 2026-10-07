using ReciteHelper.Application.Services;
using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Enums;
using ReciteHelper.Core.ValueObjects;
using Xunit;

namespace ReciteHelper.Tests;

public class ExamPaperServiceTests
{
    private static Question CreateQuestion(QuestionType type, string text, string answer = "答案")
    {
        var question = new Question { Type = type, Text = text, CorrectAnswer = answer };
        if (type == QuestionType.SingleChoice)
        {
            question.Options =
            [
                new QuestionOption { Id = "A", Text = "甲" },
                new QuestionOption { Id = "B", Text = "乙" }
            ];
            question.CorrectOptionIds = ["A"];
        }

        return question;
    }

    private static Project CreateProject(params (QuestionType Type, int Count)[] groups)
    {
        var project = new Project
        {
            ProjectName = "测试项目",
            StoragePath = Path.Combine(Path.GetTempPath(), "ExamPaperServiceTests"),
            Chapters = [new Chapter { Name = "第一章", Number = 1, Questions = [], KnowledgePoints = [] }]
        };

        foreach (var (type, count) in groups)
        {
            for (var index = 0; index < count; index++)
            {
                project.Chapters[0].Questions!.Add(
                    CreateQuestion(type, $"{type}题目 {index + 1}"));
            }
        }

        return project;
    }

    [Fact]
    public void Generate_ReturnsEmptyWhenCandidatesInsufficient()
    {
        var project = CreateProject((QuestionType.SingleChoice, 2), (QuestionType.Essay, 2));
        var service = new ExamPaperService();

        var paper = service.Generate(project, ExamSettings.Create("测试课程", 60, 10, 2, null));

        Assert.Empty(paper);
    }

    [Fact]
    public void Generate_ExcludesTrueFalseQuestions()
    {
        var project = CreateProject(
            (QuestionType.SingleChoice, 5),
            (QuestionType.TrueFalse, 5),
            (QuestionType.FillBlank, 5),
            (QuestionType.TermDefinition, 5),
            (QuestionType.Essay, 5));
        var service = new ExamPaperService();

        var paper = service.Generate(project, ExamSettings.Create("测试课程", 60, 10, 2, null));

        Assert.DoesNotContain(paper, question => question.Type == QuestionType.TrueFalse);
    }

    [Fact]
    public void Generate_UsesStandardTypeRatios()
    {
        var project = CreateProject(
            (QuestionType.SingleChoice, 8),
            (QuestionType.FillBlank, 8),
            (QuestionType.TermDefinition, 8),
            (QuestionType.Essay, 8));
        var service = new ExamPaperService();

        var paper = service.Generate(project, ExamSettings.Create("测试课程", 60, 10, 2, null));

        Assert.Equal(10, paper.Count);
        Assert.Equal(3, paper.Count(question => question.Type == QuestionType.SingleChoice));
        Assert.Equal(2, paper.Count(question => question.Type == QuestionType.FillBlank));
        Assert.Equal(1, paper.Count(question => question.Type == QuestionType.TermDefinition));
        Assert.Equal(4, paper.Count(question => question.Type == QuestionType.Essay));
    }

    [Fact]
    public void Generate_OrdersQuestionsByType()
    {
        var project = CreateProject(
            (QuestionType.SingleChoice, 5),
            (QuestionType.FillBlank, 5),
            (QuestionType.TermDefinition, 5),
            (QuestionType.Essay, 5));
        var service = new ExamPaperService();

        var paper = service.Generate(project, ExamSettings.Create("测试课程", 60, 8, 2, null));
        var orders = paper.Select(question => question.Type switch
        {
            QuestionType.SingleChoice => 0,
            QuestionType.FillBlank => 1,
            QuestionType.TermDefinition => 2,
            _ => 3
        }).ToList();

        Assert.Equal(orders.OrderBy(order => order), orders);
    }
}
