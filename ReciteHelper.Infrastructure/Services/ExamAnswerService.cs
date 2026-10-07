using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Entities;
using ReciteHelper.Infrastructure.Utilities;

namespace ReciteHelper.Infrastructure.Services;

public sealed class ExamAnswerService(IAnswerJudge answerJudge) : IExamAnswerService
{
    public bool IsCorrect(Question question, string? userAnswer)
    {
        return JudgeAnswer.Run(question, userAnswer);
    }

    public async Task<bool> IsCorrectAsync(Question question, string? userAnswer)
    {
        // Objective question types are decided by exact matching; subjective
        // questions use the same semantic (SBERT) judge as the practice flow so
        // one answer cannot be "correct" in practice and "wrong" in an exam.
        if (question is { IsTermDefinition: false, IsEssay: false })
            return JudgeAnswer.Run(question, userAnswer);

        if (string.IsNullOrWhiteSpace(userAnswer) || string.IsNullOrWhiteSpace(question.CorrectAnswer))
            return false;

        try
        {
            return await answerJudge.JudgeAsync(userAnswer, question.CorrectAnswer);
        }
        catch (Exception)
        {
            // Model or inference failure: degrade to the character-level heuristic
            // instead of failing the whole submission.
            return JudgeAnswer.Run(question, userAnswer);
        }
    }
}
