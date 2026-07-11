using System.Text;
using KnowledgeBaseQaAgent.Desktop.Models;

namespace KnowledgeBaseQaAgent.Desktop.Services;

public sealed class TextChunker
{
    private const int MaxChunkChars = 1200;
    private const int OverlapChars = 180;

    public IReadOnlyList<TextChunk> Chunk(ParsedDocument document)
    {
        var chunks = new List<TextChunk>();
        foreach (var section in document.Sections)
        {
            foreach (var text in ChunkSection(section.Text))
            {
                chunks.Add(new TextChunk(text, section.SourceLabel, chunks.Count));
            }
        }

        return chunks;
    }

    private static IEnumerable<string> ChunkSection(string input)
    {
        var normalized = NormalizeWhitespace(input);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            yield break;
        }

        if (normalized.Length <= MaxChunkChars)
        {
            yield return normalized;
            yield break;
        }

        var start = 0;
        while (start < normalized.Length)
        {
            var length = Math.Min(MaxChunkChars, normalized.Length - start);
            var end = start + length;
            if (end < normalized.Length)
            {
                var boundary = normalized.LastIndexOfAny(['。', '！', '？', '.', '!', '?', '\n'], end - 1, length);
                if (boundary > start + MaxChunkChars / 2)
                {
                    end = boundary + 1;
                }
            }

            yield return normalized[start..end].Trim();
            if (end >= normalized.Length)
            {
                yield break;
            }

            start = Math.Max(0, end - OverlapChars);
        }
    }

    private static string NormalizeWhitespace(string input)
    {
        var builder = new StringBuilder(input.Length);
        var previousWasSpace = false;
        foreach (var ch in input)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                    previousWasSpace = true;
                }
            }
            else
            {
                builder.Append(ch);
                previousWasSpace = false;
            }
        }

        return builder.ToString().Trim();
    }
}
