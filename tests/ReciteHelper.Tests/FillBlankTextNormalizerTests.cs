using ReciteHelper.Core.Services;
using Xunit;

namespace ReciteHelper.Tests;

public class FillBlankTextNormalizerTests
{
    [Fact]
    public void NormalizeForImport_KeepsExplicitMarkers()
    {
        var text = "数量性状可以用 ________ 假说解释。";
        var answers = new List<string> { "多基因" };

        var normalized = FillBlankTextNormalizer.NormalizeForImport(text, answers);

        Assert.Contains(FillBlankTextNormalizer.BlankMarker, normalized);
        Assert.Single(answers);
    }

    [Fact]
    public void NormalizeForImport_KeepsAllExplicitMarkersWhenMoreThanAnswers()
    {
        var text = "A ____ 和 ____ 两个概念。";
        var answers = new List<string> { "甲" };

        var normalized = FillBlankTextNormalizer.NormalizeForImport(text, answers);

        Assert.Equal(2, CountMarkers(normalized));
    }

    [Fact]
    public void NormalizeForImport_RestoresMarkerAtAnswerPosition()
    {
        var text = "基因效应包括加性效应、显性效应。";
        var answers = new List<string> { "加性" };

        var normalized = FillBlankTextNormalizer.NormalizeForImport(text, answers);

        Assert.Contains(FillBlankTextNormalizer.BlankMarker, normalized);
        Assert.DoesNotContain("加性效应、", normalized.Replace(FillBlankTextNormalizer.BlankMarker, string.Empty));
    }

    [Fact]
    public void NormalizeForImport_AppendsFallbackWhenNothingMatches()
    {
        var text = "请写出细胞膜的结构特点。";
        var answers = new List<string> { "磷脂双分子层", "糖蛋白" };

        var normalized = FillBlankTextNormalizer.NormalizeForImport(text, answers);

        Assert.Contains("待填空位", normalized);
        Assert.Equal(2, CountMarkers(normalized));
    }

    [Fact]
    public void NormalizeForDisplay_StripsFallbackSuffix()
    {
        var text = "请写出细胞的定义。　待填空位：________";
        var answers = new List<string> { "细胞" };

        var display = FillBlankTextNormalizer.NormalizeForDisplay(text, answers);

        Assert.DoesNotContain("待填空位", display);
    }

    private static int CountMarkers(string text)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(FillBlankTextNormalizer.BlankMarker, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += FillBlankTextNormalizer.BlankMarker.Length;
        }

        return count;
    }
}
