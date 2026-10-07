using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Infrastructure.Algorithms;
using System.Collections.Concurrent;

namespace ReciteHelper.Infrastructure.Services;

public class SbertModelJudge : IAnswerJudge
{
    private const double SimilarityThreshold = 0.70d;
    private const int MaxCachedPairs = 1024;

    private readonly string _modelPath = Path.Combine(
        AppContext.BaseDirectory,
        "Resources",
        "Models",
        "sbert.onnx");
    private readonly string _vocabPath = Path.Combine(
        AppContext.BaseDirectory,
        "Resources",
        "vocab.txt");

#pragma warning disable CS0618
    private readonly LevenshteinDistanceJudge _fallbackJudge = new();
#pragma warning restore CS0618

    // Loading the ONNX session is expensive; build the embedding generator once
    // and reuse it for every judgement instead of per answer.
    private readonly object _generatorLock = new();
    private IEmbeddingGenerator<string, Embedding<float>>? _embeddingGenerator;

    // The exam flow scores the same subjective pair more than once (scoring page
    // and review page), so memoize recent pairs with a simple bounded cache.
    private readonly ConcurrentDictionary<string, double> _similarityCache = new();

    public async Task<double> CalculateSimilarityAsync(string userAnswer, string correctAnswer)
    {
        var cacheKey = $"{userAnswer}\u0001{correctAnswer}";
        if (_similarityCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var result = await CalculateSimilarityCoreAsync(userAnswer, correctAnswer);

        if (_similarityCache.Count >= MaxCachedPairs)
            _similarityCache.Clear();
        _similarityCache[cacheKey] = result;

        return result;
    }

    private async Task<double> CalculateSimilarityCoreAsync(string userAnswer, string correctAnswer)
    {
        var embeddingGenerator = GetEmbeddingGenerator();
        if (embeddingGenerator is null)
            return await _fallbackJudge.CalculateSimilarityAsync(userAnswer, correctAnswer);

        // One batched inference for both texts instead of two model runs.
        var embeddings = await embeddingGenerator.GenerateAsync([userAnswer, correctAnswer]);

        var vecUser = embeddings[0].Vector;
        var vecTarget = embeddings[1].Vector;

        var sbertSim = CosineSimilarity.SpecCalculate(vecUser.Span, vecTarget.Span);

        var userSet = userAnswer.ToHashSet();
        var targetSet = correctAnswer.ToHashSet();
        var jaccard = (double)userSet.Intersect(targetSet).Count() / userSet.Union(targetSet).Count();

        var sbertWeight = Math.Clamp(correctAnswer.Length / 7.0, 0.6, 0.9);
        var jaccardWeight = 1.0 - sbertWeight;

        return (sbertSim * sbertWeight) + (jaccard * jaccardWeight);
    }

    public async Task<bool> JudgeAsync(string? userAnswer, string? correctAnswer)
    {
        ArgumentNullException.ThrowIfNull(userAnswer, nameof(userAnswer));
        ArgumentNullException.ThrowIfNull(correctAnswer, nameof(correctAnswer));

        return await CalculateSimilarityAsync(userAnswer, correctAnswer) >= SimilarityThreshold;
    }

    private IEmbeddingGenerator<string, Embedding<float>>? GetEmbeddingGenerator()
    {
        if (!File.Exists(_modelPath) || !File.Exists(_vocabPath))
            return null;

        if (_embeddingGenerator is not null)
            return _embeddingGenerator;

        lock (_generatorLock)
        {
            if (_embeddingGenerator is null)
            {
                var builder = Kernel.CreateBuilder();
                builder.AddBertOnnxEmbeddingGenerator(
                    onnxModelPath: _modelPath,
                    vocabPath: _vocabPath);

                var kernel = builder.Build();
                _embeddingGenerator = kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
            }

            return _embeddingGenerator;
        }
    }
}
