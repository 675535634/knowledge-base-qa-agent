using System.Text.RegularExpressions;

namespace KnowledgeBaseQaAgent.Desktop.Services;

public static partial class AnswerPostProcessor
{
    public static string CleanForVisitor(string answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return "";
        }

        var text = StripCodeFence(answer.Trim());
        text = BoldMarkerRegex().Replace(text, "$1");
        text = InlineCitationRegex().Replace(text, "");
        text = MarkdownHeadingRegex().Replace(text, "");
        text = MarkdownBulletRegex().Replace(text, "- ");
        text = MarkdownNumberBoldRegex().Replace(text, "$1");
        text = LooseAsteriskRegex().Replace(text, "");
        text = SpaceBeforePunctuationRegex().Replace(text, "$1");
        text = MultiSpaceRegex().Replace(text, " ");
        text = MultiBlankLineRegex().Replace(text, $"{Environment.NewLine}{Environment.NewLine}");
        return text.Trim();
    }

    private static string StripCodeFence(string value)
    {
        var text = value.Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal))
        {
            return text;
        }

        return text.Replace("```markdown", "", StringComparison.OrdinalIgnoreCase)
            .Replace("```text", "", StringComparison.OrdinalIgnoreCase)
            .Replace("```json", "", StringComparison.OrdinalIgnoreCase)
            .Replace("```", "", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    [GeneratedRegex(@"\*\*(.*?)\*\*", RegexOptions.Singleline)]
    private static partial Regex BoldMarkerRegex();

    [GeneratedRegex(@"\s*(?:\[\d{1,3}\])+", RegexOptions.Compiled)]
    private static partial Regex InlineCitationRegex();

    [GeneratedRegex(@"(?m)^\s{0,3}#{1,6}\s*")]
    private static partial Regex MarkdownHeadingRegex();

    [GeneratedRegex(@"(?m)^\s*[*]\s+")]
    private static partial Regex MarkdownBulletRegex();

    [GeneratedRegex(@"(?m)^\s*[*]\s*(\d+[.、])")]
    private static partial Regex MarkdownNumberBoldRegex();

    [GeneratedRegex(@"(?m)(^|\s)\*(?=\S)")]
    private static partial Regex LooseAsteriskRegex();

    [GeneratedRegex(@"\s+([，。；：！？、,.!?;:])")]
    private static partial Regex SpaceBeforePunctuationRegex();

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex MultiSpaceRegex();

    [GeneratedRegex(@"(\r?\n){3,}")]
    private static partial Regex MultiBlankLineRegex();
}
