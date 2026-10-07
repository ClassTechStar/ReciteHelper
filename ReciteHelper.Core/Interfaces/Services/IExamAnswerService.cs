using ReciteHelper.Core.Entities;

namespace ReciteHelper.Core.Interfaces.Services;

public interface IExamAnswerService
{
    bool IsCorrect(Question question, string? userAnswer);

    /// <summary>
    /// Async judging: uses the semantic (SBERT) judge for subjective questions and
    /// falls back to the character-level heuristic when the model is unavailable.
    /// </summary>
    Task<bool> IsCorrectAsync(Question question, string? userAnswer);
}
