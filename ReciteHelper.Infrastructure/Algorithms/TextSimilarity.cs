namespace ReciteHelper.Infrastructure.Algorithms;

/// <summary>
/// Character-level similarity metrics replacing the unmaintained FuzzyString package
/// (last release ~2014). Semantics mirror the metrics the package provided:
/// OverlapCoefficient, longest-common-subsequence ratio and longest-common-substring
/// ratio, averaged and compared against FuzzyString's "Strong" tolerance (0.85).
/// </summary>
public static class TextSimilarity
{
    public static double OverlapCoefficient(string first, string second)
    {
        if (first.Length == 0 || second.Length == 0)
            return 0;

        var firstSet = first.ToHashSet();
        var secondSet = second.ToHashSet();
        var intersection = firstSet.Intersect(secondSet).Count();
        var denominator = Math.Min(firstSet.Count, secondSet.Count);
        return denominator == 0 ? 0 : (double)intersection / denominator;
    }

    public static double LongestCommonSubsequenceRatio(string first, string second)
    {
        var maxLength = Math.Max(first.Length, second.Length);
        if (maxLength == 0)
            return 1;

        return (double)LongestCommonSubsequenceLength(first, second) / maxLength;
    }

    public static double LongestCommonSubstringRatio(string first, string second)
    {
        var maxLength = Math.Max(first.Length, second.Length);
        if (maxLength == 0)
            return 1;

        return (double)LongestCommonSubstringLength(first, second) / maxLength;
    }

    /// <summary>Average of the three metrics, compared against FuzzyString's Strong tolerance (0.85).</summary>
    public static bool ApproximatelyEquals(string first, string second, double threshold = 0.85)
    {
        if (first.Length == 0 || second.Length == 0)
            return false;

        var average = (OverlapCoefficient(first, second)
                       + LongestCommonSubsequenceRatio(first, second)
                       + LongestCommonSubstringRatio(first, second)) / 3.0;
        return average > threshold;
    }

    private static int LongestCommonSubsequenceLength(string first, string second)
    {
        var rows = first.Length + 1;
        var columns = second.Length + 1;
        var table = new int[rows, columns];

        for (var i = 1; i < rows; i++)
        {
            for (var j = 1; j < columns; j++)
            {
                table[i, j] = first[i - 1] == second[j - 1]
                    ? table[i - 1, j - 1] + 1
                    : Math.Max(table[i - 1, j], table[i, j - 1]);
            }
        }

        return table[first.Length, second.Length];
    }

    private static int LongestCommonSubstringLength(string first, string second)
    {
        var rows = first.Length + 1;
        var columns = second.Length + 1;
        var table = new int[rows, columns];
        var longest = 0;

        for (var i = 1; i < rows; i++)
        {
            for (var j = 1; j < columns; j++)
            {
                if (first[i - 1] != second[j - 1])
                    continue;

                table[i, j] = table[i - 1, j - 1] + 1;
                if (table[i, j] > longest)
                    longest = table[i, j];
            }
        }

        return longest;
    }
}
