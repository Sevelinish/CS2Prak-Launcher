using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace Cs2Prak.Core.Highlights;

public sealed class HighlighterException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public static class HighlighterClient
{
    public const string Protocol = "1.0";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public static JsonNode Call(string command, JsonNode? payload = null,
                                TimeSpan? timeout = null)
    {
        var endpoint = HighlighterProcess.Ensure();
        return Send(endpoint, command, payload, timeout);
    }

    public static JsonNode? TryCall(string command, JsonNode? payload = null,
                                    TimeSpan? timeout = null)
    {
        try { return Call(command, payload, timeout); }
        catch (Exception) { return null; }
    }

    private static JsonNode Send(HighlighterEndpoint endpoint, string command, JsonNode? payload,
                                 TimeSpan? timeout)
    {
        var body = (payload ?? new JsonObject()).ToJsonString();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint.BaseUrl}/v1/{command}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.Token);

        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(5));

        HttpResponseMessage response;
        try
        {
            response = Http.Send(request, HttpCompletionOption.ResponseContentRead, cts.Token);
        }
        catch (Exception e)
        {
            throw new HighlighterException("transport", $"HighlighterCS2 is not answering: {e.Message}");
        }

        using (response)
        {
            var reply = response.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();

            if (JsonNode.Parse(reply) is not JsonObject envelope)
                throw new HighlighterException("transport",
                    $"HighlighterCS2 returned {(int)response.StatusCode} with an unreadable body.");

            if (envelope["ok"]?.GetValue<bool>() == true)
                return envelope["data"]?.DeepClone() ?? new JsonObject();

            var error = envelope["error"] as JsonObject;
            throw new HighlighterException(
                error?["code"]?.GetValue<string>() ?? "internal",
                error?["message"]?.GetValue<string>() ?? $"HighlighterCS2 failed ({(int)response.StatusCode}).");
        }
    }

    public static JsonNode Handshake() => Call("handshake", new JsonObject
    {
        ["clientName"] = "CS2Prak-Launcher",
        ["clientVersion"] = AppInfo.Version,
        ["protocol"] = Protocol,
    }, TimeSpan.FromSeconds(30));

    public static void Shutdown()
    {
        if (HighlighterProcess.Current is not { } endpoint) return;

        try { Send(endpoint, "shutdown", null, TimeSpan.FromSeconds(5)); }
        catch (Exception) { }

        HighlighterProcess.Stop();
    }
}
