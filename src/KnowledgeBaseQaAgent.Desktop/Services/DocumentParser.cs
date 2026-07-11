using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using HtmlAgilityPack;
using KnowledgeBaseQaAgent.Desktop.Models;
using Markdig;
using UglyToad.PdfPig;

namespace KnowledgeBaseQaAgent.Desktop.Services;

public sealed class DocumentParser
{
    public async Task<ParsedDocument> ParseAsync(string path, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => ParsePdf(path),
            ".docx" => ParseDocx(path),
            ".md" or ".markdown" => ParseMarkdown(path),
            ".html" or ".htm" => ParseHtml(path),
            ".txt" => new ParsedDocument(path, Path.GetFileName(path), [new ParsedSection(await File.ReadAllTextAsync(path, cancellationToken), "text")]),
            _ => throw new NotSupportedException($"Unsupported document type: {extension}")
        };
    }

    public static string ComputeFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string ComputeTextHash(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static ParsedDocument ParsePdf(string path)
    {
        using var document = PdfDocument.Open(path);
        var sections = document.GetPages()
            .Select(page => new ParsedSection(page.Text, $"page {page.Number}"))
            .Where(section => !string.IsNullOrWhiteSpace(section.Text))
            .ToArray();
        return new ParsedDocument(path, Path.GetFileName(path), sections);
    }

    private static ParsedDocument ParseDocx(string path)
    {
        using var document = WordprocessingDocument.Open(path, false);
        var body = document.MainDocumentPart?.Document?.Body;
        var paragraphs = body?.Descendants<Paragraph>()
            .Select(paragraph => string.Concat(paragraph.Descendants<Text>().Select(text => text.Text)))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray() ?? [];

        return new ParsedDocument(path, Path.GetFileName(path), [new ParsedSection(string.Join(Environment.NewLine, paragraphs), "document")]);
    }

    private static ParsedDocument ParseMarkdown(string path)
    {
        var markdown = File.ReadAllText(path);
        var html = Markdown.ToHtml(markdown);
        var text = ExtractHtmlText(html);
        return new ParsedDocument(path, Path.GetFileName(path), [new ParsedSection(text, "markdown")]);
    }

    private static ParsedDocument ParseHtml(string path)
    {
        var html = File.ReadAllText(path);
        var text = ExtractHtmlText(html);
        return new ParsedDocument(path, Path.GetFileName(path), [new ParsedSection(text, "html")]);
    }

    private static string ExtractHtmlText(string html)
    {
        var document = new HtmlAgilityPack.HtmlDocument();
        document.LoadHtml(html);
        foreach (var node in document.DocumentNode.SelectNodes("//script|//style") ?? Enumerable.Empty<HtmlNode>())
        {
            node.Remove();
        }

        var text = WebUtility.HtmlDecode(document.DocumentNode.InnerText);
        return Regex.Replace(text, @"\s+", " ").Trim();
    }
}
