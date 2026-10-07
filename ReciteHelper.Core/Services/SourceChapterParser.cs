using System.Text.RegularExpressions;

namespace ReciteHelper.Core.Services;

/// <summary>
/// Local (non-AI) source-chapter detection: recognizes explicit chapter headings
/// such as Chinese-numbered textbook units, parses the numerals and splits the
/// text accordingly (extracted from ProjectCreationService).
/// </summary>
public static partial class SourceChapterParser
{
    private const int StructuredChapterChunkSize = 800;

    public static bool ContainsExplicitChapterMarkers(string text)
    {
        return ExplicitChapterHeadingRegex().Matches(text ?? string.Empty)
            .Cast<Match>()
            .Select(match => NormalizeChapterOrdinal(match.Groups["ordinal"].Value))
            .Where(ordinal => !string.IsNullOrWhiteSpace(ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() >= 2;
    }

    public static SourceChapterSplit SplitLocally(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return SourceChapterSplit.Empty;

        var matches = ExplicitChapterHeadingRegex().Matches(text);
        if (matches.Count == 0)
            return SourceChapterSplit.Empty;

        var markers = matches
            .Cast<Match>()
            .Select(match => new SourceChapterMarker(
                match.Groups["heading"].Index,
                CleanChapterName(match.Groups["heading"].Value),
                NormalizeChapterOrdinal(match.Groups["ordinal"].Value),
                ParseChapterNumber(match.Groups["ordinal"].Value)))
            .Where(marker => marker.Index >= 0 &&
                             !string.IsNullOrWhiteSpace(marker.Heading) &&
                             !string.IsNullOrWhiteSpace(marker.Ordinal) &&
                             marker.Number is > 0)
            .OrderBy(marker => marker.Index)
            .ToList();
        if (markers.Count == 0)
            return SourceChapterSplit.Empty;

        var chapters = new List<SourceChapter>();
        var preface = text[..markers[0].Index].Trim();
        if (preface.Length >= StructuredChapterChunkSize / 2)
            chapters.Add(new SourceChapter(InferPrefaceChapterName(preface), preface, 0));

        for (var index = 0; index < markers.Count; index++)
        {
            var marker = markers[index];
            var nextIndex = index + 1 < markers.Count ? markers[index + 1].Index : text.Length;
            var name = CleanChapterName(marker.Heading);
            var content = text[marker.Index..nextIndex].Trim();
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(content))
                chapters.Add(new SourceChapter(name, content, marker.Number));
        }

        return new SourceChapterSplit(chapters, IsCompleteContinuousChapterSequence(markers));
    }

    private static bool IsCompleteContinuousChapterSequence(IReadOnlyList<SourceChapterMarker> markers)
    {
        var numbers = markers
            .Select(marker => marker.Number)
            .Where(number => number is > 0)
            .Select(number => number!.Value)
            .Distinct()
            .Order()
            .ToList();
        if (numbers.Count == 0 || numbers[0] != 1)
            return false;

        for (var index = 0; index < numbers.Count; index++)
        {
            if (numbers[index] != index + 1)
                return false;
        }

        return true;
    }

    private static int? ParseChapterNumber(string value)
    {
        var normalized = NormalizeChapterOrdinal(value);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;
        if (int.TryParse(normalized, out var numeric))
            return numeric;

        return ChineseNumberToInt(normalized);
    }

    private static int? ChineseNumberToInt(string text)
    {
        text = text.Replace("〇", "零", StringComparison.Ordinal)
            .Replace("两", "二", StringComparison.Ordinal);

        var map = new Dictionary<char, int>
        {
            ['零'] = 0,
            ['一'] = 1,
            ['二'] = 2,
            ['三'] = 3,
            ['四'] = 4,
            ['五'] = 5,
            ['六'] = 6,
            ['七'] = 7,
            ['八'] = 8,
            ['九'] = 9
        };

        var result = 0;
        var section = 0;
        var number = 0;
        foreach (var character in text)
        {
            if (map.TryGetValue(character, out var mapped))
            {
                number = mapped;
                continue;
            }

            switch (character)
            {
                case '十':
                    section += (number == 0 ? 1 : number) * 10;
                    number = 0;
                    break;
                case '百':
                    section += (number == 0 ? 1 : number) * 100;
                    number = 0;
                    break;
                case '千':
                    section += (number == 0 ? 1 : number) * 1000;
                    number = 0;
                    break;
                case '万':
                    result += (section + number) * 10000;
                    section = 0;
                    number = 0;
                    break;
                default:
                    return null;
            }
        }

        var total = result + section + number;
        return total > 0 ? total : null;
    }

    public static string NormalizeChapterOrdinal(string value)
    {
        return WhitespaceRegex().Replace(value ?? string.Empty, string.Empty).Trim();
    }

    private static string InferPrefaceChapterName(string preface)
    {
        var firstMeaningfulLine = preface
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => line.Contains("绪论", StringComparison.Ordinal) ||
                                    line.Contains("导论", StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(firstMeaningfulLine))
            return CleanChapterName(firstMeaningfulLine.Length <= 40 ? firstMeaningfulLine : firstMeaningfulLine[..40]);

        return "绪论";
    }

    public static string CleanChapterName(string? name)
    {
        var cleaned = WhitespaceRegex().Replace(name ?? string.Empty, " ").Trim();
        cleaned = cleaned.Trim(' ', '：', ':', '-', '—');
        return cleaned;
    }

    [GeneratedRegex(@"(?m)(?:^|(?<=[\r\n。！？；;：:])\s*)(?<heading>第\s*(?<ordinal>(?:[一二三四五六七八九十百千万〇零两\d]\s*){1,8})章[^\r\n]{0,60})")]
    private static partial Regex ExplicitChapterHeadingRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}

public sealed record SourceChapter(string Name, string Content, int? Number = null);

public sealed record SourceChapterMarker(int Index, string Heading, string Ordinal, int? Number);

public sealed record SourceChapterSplit(List<SourceChapter> Chapters, bool IsCompleteContinuous)
    {
        public static SourceChapterSplit Empty { get; } = new([], false);
    }
