using System.Text.Json;
using System.Text.Json.Nodes;
using RecipeWarden.Core.AiParser;

namespace RecipeWarden.Importers.Ai;

/// <summary>A model reply: its text and the tokens used (FR-IMP-10).</summary>
public sealed record AiReply(string? Text, long InputTokens, long OutputTokens, string? Error);

/// <summary>
/// FR-IMP-09, D-08: the two wire formats, as plain JSON (no SDKs): the Claude Messages API and OpenAI-compatible chat
/// completions. No tools are offered to the model (SEC-47). Pure: the request is sent by SafeFetcher in Infrastructure.
/// </summary>
public static class AiProtocol
{
    /// <summary>The JSON body for a single system + user turn.</summary>
    public static byte[] Request(AiProvider provider, string model, string system, string user, int maxTokens = AiParserRules.MaxOutputTokens)
    {
        JsonObject body = provider == AiProvider.Claude
            ? new JsonObject
            {
                ["model"] = model,
                ["max_tokens"] = maxTokens,
                ["system"] = system,
                ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = user }),
            }
            : new JsonObject
            {
                ["model"] = model,
                ["max_tokens"] = maxTokens,
                ["temperature"] = 0,
                ["stream"] = false,
                ["messages"] = new JsonArray(
                    new JsonObject { ["role"] = "system", ["content"] = system },
                    new JsonObject { ["role"] = "user", ["content"] = user }),
            };
        return JsonSerializer.SerializeToUtf8Bytes(body);
    }

    /// <summary>Authentication and version headers. The key is never logged (SEC-45).</summary>
    public static Dictionary<string, string> Headers(AiProvider provider, string? apiKey)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["Accept"] = "application/json" };
        if (provider == AiProvider.Claude)
        {
            headers["anthropic-version"] = AiParserRules.ClaudeVersion;
            if (!string.IsNullOrEmpty(apiKey))
            {
                headers["x-api-key"] = apiKey;
            }
        }
        else if (!string.IsNullOrEmpty(apiKey))
        {
            headers["Authorization"] = "Bearer " + apiKey;
        }

        return headers;
    }

    /// <summary>Reads the reply text and token counts; never throws on odd JSON (the reply is untrusted, SEC-47).</summary>
    public static AiReply ReadResponse(AiProvider provider, byte[]? body)
    {
        if (body is null || body.Length == 0)
        {
            return new AiReply(null, 0, 0, "The model sent an empty answer.");
        }

        try
        {
            var root = JsonNode.Parse(body);
            if (root is not JsonObject obj)
            {
                return new AiReply(null, 0, 0, "The model's answer was not understood.");
            }

            if (obj["error"] is JsonNode error)
            {
                var message = error is JsonObject e ? e["message"]?.GetValue<string>() : error.ToString();
                return new AiReply(null, 0, 0, "The AI service refused: " + Short(message));
            }

            if (provider == AiProvider.Claude)
            {
                var text = string.Concat((obj["content"] as JsonArray ?? [])
                    .OfType<JsonObject>()
                    .Where(c => c["type"]?.GetValue<string>() == "text")
                    .Select(c => c["text"]?.GetValue<string>() ?? string.Empty));
                return new AiReply(text, Long(obj["usage"]?["input_tokens"]), Long(obj["usage"]?["output_tokens"]), text.Length == 0 ? "The model sent no text." : null);
            }

            var content = obj["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
            return new AiReply(content, Long(obj["usage"]?["prompt_tokens"]), Long(obj["usage"]?["completion_tokens"]),
                string.IsNullOrEmpty(content) ? "The model sent no text." : null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return new AiReply(null, 0, 0, "The model's answer was not understood.");
        }
    }

    private static long Long(JsonNode? node)
    {
        try
        {
            return node?.GetValue<long>() ?? 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return 0;
        }
    }

    private static string Short(string? message) =>
        string.IsNullOrWhiteSpace(message) ? "no reason given" : message.Length > 200 ? message[..200] : message;
}
