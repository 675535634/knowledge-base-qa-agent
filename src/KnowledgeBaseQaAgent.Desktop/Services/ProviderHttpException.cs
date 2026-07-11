using System.Net;
using System.Net.Http;

namespace KnowledgeBaseQaAgent.Desktop.Services;

public sealed class ProviderHttpException : InvalidOperationException
{
    public ProviderHttpException(
        string operation,
        string providerId,
        string model,
        string endpoint,
        HttpStatusCode statusCode,
        string responseBody)
        : base(BuildMessage(operation, providerId, model, endpoint, statusCode, responseBody))
    {
        Operation = operation;
        ProviderId = providerId;
        Model = model;
        Endpoint = endpoint;
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public string Operation { get; }
    public string ProviderId { get; }
    public string Model { get; }
    public string Endpoint { get; }
    public HttpStatusCode StatusCode { get; }
    public string ResponseBody { get; }

    private static string BuildMessage(
        string operation,
        string providerId,
        string model,
        string endpoint,
        HttpStatusCode statusCode,
        string responseBody)
    {
        var body = string.IsNullOrWhiteSpace(responseBody) ? "<empty>" : Trim(responseBody, 1800);
        return $"""
        Provider request failed.
        Operation: {operation}
        Provider: {providerId}
        Model: {model}
        Endpoint: {endpoint}
        Status: {(int)statusCode} {statusCode}
        Response: {body}
        """;
    }

    private static string Trim(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";
}

public static class ProviderHttpDiagnostics
{
    public static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        string providerId,
        string model,
        string endpoint,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new ProviderHttpException(operation, providerId, model, endpoint, response.StatusCode, body);
    }
}
