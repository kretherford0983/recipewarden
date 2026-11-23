using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.AiExchange;
using RecipeWarden.Core.AiParser;
using RecipeWarden.Core.Import;
using RecipeWarden.Core.Security;
using RecipeWarden.Importers.Ai;
using RecipeWarden.Infrastructure.Audit;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Import;
using RecipeWarden.Infrastructure.Settings;

namespace RecipeWarden.Infrastructure.Ai;

/// <summary>Where the Claude API is reached. Only tests change it (the fake internet has no TLS).</summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public string ClaudeEndpoint { get; set; } = AiParserRules.ClaudeEndpoint;
}

/// <summary>The result of an AI parse: a recipe for review, or why there is none.</summary>
public sealed record AiParseResult(ImportedRecipe? Recipe, string? Error, bool Attempted)
{
    public static AiParseResult NotUsed(string reason) => new(null, reason, false);
}

/// <summary>This month's usage for /admin and /admin/ai (FR-IMP-10).</summary>
public sealed record AiUsage(long InputTokens, long OutputTokens, int Calls, long? Cap)
{
    public long Total => InputTokens + OutputTokens;
}

/// <summary>
/// FR-IMP-09/10, FR-NUT-06, SEC-44 to 47, AC-17, AC-18, ADR 0042: the optional AI parser. Off until an Admin turns it
/// on; when off, nothing is sent anywhere. Every call goes through SafeFetcher (the Claude host by name; an
/// OpenAI-compatible endpoint on the LAN only at the exact host and port the Admin allowed), sends only recipe text,
/// is logged without the key, text or reply, and stops at the monthly token cap. Replies are read as recipe-md like
/// any import (plain text, BR-13) and always go to review.
/// </summary>
public sealed class AiParserService(AppDbContext db, SettingsStore settings, SafeFetcher fetcher, IDataProtectionProvider protection, AiOptions options,
    IAuditActor actor, TimeProvider clock)
{
    private const string KeyPurpose = "RecipeWarden.AiApiKey.v1";

    public const string Enabled = "Ai.Enabled";
    public const string Provider = "Ai.Provider";
    public const string Endpoint = "Ai.Endpoint";
    public const string Model = "Ai.Model";
    public const string ApiKey = "Ai.ApiKey";
    public const string Cap = "Ai.MonthlyTokenCap";
    public const string AllowLan = "Ai.AllowLan";

    private IDataProtector Protector => protection.CreateProtector(KeyPurpose);

    public async Task<AiSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var all = await settings.GetManyAsync([Enabled, Provider, Endpoint, Model, Cap, AllowLan], cancellationToken).ConfigureAwait(false);
        return new AiSettings(
            all.GetValueOrDefault(Enabled) == "true",
            all.TryGetValue(Provider, out var p) && Enum.TryParse<AiProvider>(p, out var provider) ? provider : AiProvider.Claude,
            all.GetValueOrDefault(Endpoint),
            all.GetValueOrDefault(Model),
            all.TryGetValue(Cap, out var c) && long.TryParse(c, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cap) && cap > 0 ? cap : null,
            all.GetValueOrDefault(AllowLan) == "true");
    }

    /// <summary>SEC-45: the UI only ever learns whether a key is set.</summary>
    public async Task<bool> HasKeyAsync(CancellationToken cancellationToken = default) =>
        await settings.GetAsync(ApiKey, cancellationToken).ConfigureAwait(false) is not null;

    /// <summary>Saves settings; a non-empty <paramref name="newKey"/> replaces the key, <paramref name="clearKey"/> removes it.</summary>
    public async Task<string?> SaveSettingsAsync(AiSettings value, string? newKey, bool clearKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        var hasKey = !clearKey && (!string.IsNullOrWhiteSpace(newKey) || await HasKeyAsync(cancellationToken).ConfigureAwait(false));
        if (AiParserRules.Validate(value, hasKey) is { } error)
        {
            return error;
        }

        if (!string.IsNullOrWhiteSpace(newKey) && (newKey.Length > 500 || newKey.Any(char.IsControl)))
        {
            return "That API key does not look right.";
        }

        await settings.SetAsync(Enabled, value.Enabled ? "true" : null, cancellationToken).ConfigureAwait(false);
        await settings.SetAsync(Provider, value.Provider.ToString(), cancellationToken).ConfigureAwait(false);
        await settings.SetAsync(Endpoint, value.Provider == AiProvider.Claude ? null : value.Endpoint?.Trim(), cancellationToken).ConfigureAwait(false);
        await settings.SetAsync(Model, value.Model!.Trim(), cancellationToken).ConfigureAwait(false);
        await settings.SetAsync(Cap, value.MonthlyTokenCap?.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
        await settings.SetAsync(AllowLan, value.Provider != AiProvider.Claude && value.AllowLan ? "true" : null, cancellationToken).ConfigureAwait(false);
        if (clearKey)
        {
            await settings.SetAsync(ApiKey, null, cancellationToken).ConfigureAwait(false);
        }
        else if (!string.IsNullOrWhiteSpace(newKey))
        {
            await settings.SetAsync(ApiKey, Protector.Protect(newKey.Trim()), cancellationToken).ConfigureAwait(false); // SEC-45: encrypted at rest
        }

        return null;
    }

    public async Task<AiUsage> UsageThisMonthAsync(CancellationToken cancellationToken = default)
    {
        var start = AiParserRules.MonthStart(clock.GetUtcNow());
        var rows = await db.AiCallLogs.AsNoTracking().Where(l => l.At >= start).Select(l => new { l.InputTokens, l.OutputTokens })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new AiUsage(rows.Sum(r => r.InputTokens), rows.Sum(r => r.OutputTokens), rows.Count, (await GetSettingsAsync(cancellationToken).ConfigureAwait(false)).MonthlyTokenCap);
    }

    public async Task<IReadOnlyList<AiCallLog>> RecentCallsAsync(int count, CancellationToken cancellationToken = default) =>
        await db.AiCallLogs.AsNoTracking().OrderByDescending(l => l.Id).Take(count).ToListAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>FR-IMP-09: recipe text → a recipe-md draft for review. Never called when the parser is off (AC-17).</summary>
    public async Task<AiParseResult> ParseAsync(string text, AiPurpose purpose, CancellationToken cancellationToken = default)
    {
        var call = await CallAsync(purpose, AiParserRules.ParseInstructions, AiParserRules.ParsePrompt(text), cancellationToken).ConfigureAwait(false);
        if (call.Error is not null)
        {
            return new AiParseResult(null, call.Error, call.Attempted);
        }

        // SEC-47: the reply is untrusted recipe-md; the first block that validates is taken, as plain text.
        var block = AiReplyReader.Read(call.Text).FirstOrDefault(b => b.IsValid);
        if (block is null)
        {
            await MarkLastAsync(AiCallResult.Invalid, "No valid recipe-md in the reply", cancellationToken).ConfigureAwait(false);
            return new AiParseResult(null, "The AI reply did not contain a usable recipe, so the rules-based result is shown.", true);
        }

        var recipe = block.Recipe! with { Remarks = [.. block.Recipe!.Remarks, AiParserRules.AiParsedNotice] };
        return new AiParseResult(recipe, null, true);
    }

    /// <summary>FR-NUT-06: picks one of the local candidates for an unmatched ingredient, or none. An Editor confirms it.</summary>
    public async Task<(int? Index, string? Error)> SuggestMappingAsync(string ingredient, IReadOnlyList<string> candidates, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
        {
            return (null, "No foods in the local data look like this ingredient; map it by search.");
        }

        var call = await CallAsync(AiPurpose.NutritionMapping, AiParserRules.MappingInstructions, AiParserRules.MappingPrompt(ingredient, candidates), cancellationToken)
            .ConfigureAwait(false);
        if (call.Error is not null)
        {
            return (null, call.Error);
        }

        var index = AiParserRules.ReadMappingReply(call.Text, candidates.Count);
        return index is null ? (null, "The AI found no match among the local foods.") : (index, null);
    }

    /// <summary>FR-IMP-10 "Test connection": a tiny request with the saved settings.</summary>
    public async Task<string?> TestAsync(CancellationToken cancellationToken = default)
    {
        var call = await CallAsync(AiPurpose.Test, "Reply with the single word: ok", "ok?", cancellationToken, allowWhenDisabled: true, maxTokens: 10)
            .ConfigureAwait(false);
        return call.Error;
    }

    private sealed record Call(string? Text, string? Error, bool Attempted);

    private async Task<Call> CallAsync(AiPurpose purpose, string system, string user, CancellationToken ct, bool allowWhenDisabled = false,
        int maxTokens = AiParserRules.MaxOutputTokens)
    {
        var s = await GetSettingsAsync(ct).ConfigureAwait(false);
        if (!s.Enabled && !allowWhenDisabled)
        {
            return new Call(null, AiParserRules.Disabled, false); // AC-17: nothing is sent
        }

        if (string.IsNullOrWhiteSpace(s.Model))
        {
            return new Call(null, "The AI parser has no model set.", false);
        }

        var usage = await UsageThisMonthAsync(ct).ConfigureAwait(false);
        if (AiParserRules.IsCapped(s, usage.Total) && purpose != AiPurpose.Test)
        {
            await LogAsync(s, purpose, 0, 0, 0, AiCallResult.Capped, "Monthly token cap reached", ct).ConfigureAwait(false);
            return new Call(null, AiParserRules.CapReached, false);
        }

        Uri uri;
        PermittedEndpoint? permit = null;
        if (s.Provider == AiProvider.Claude)
        {
            uri = new Uri(options.ClaudeEndpoint);
            if (!string.Equals(uri.IdnHost, new Uri(AiParserRules.ClaudeEndpoint).IdnHost, StringComparison.OrdinalIgnoreCase))
            {
                permit = new PermittedEndpoint(uri.Scheme, uri.IdnHost, uri.Port); // tests only (AiOptions)
            }
        }
        else
        {
            var (parsed, lanPermit, error) = OutboundEndpointRules.Parse(AiParserRules.OpenAiUrl(s.Endpoint ?? string.Empty), s.AllowLan);
            if (parsed is null)
            {
                return new Call(null, "Endpoint: " + error, false);
            }

            uri = parsed;
            permit = lanPermit;
        }

        string? key = null;
        if (await settings.GetAsync(ApiKey, ct).ConfigureAwait(false) is { } protectedKey)
        {
            try
            {
                key = Protector.Unprotect(protectedKey);
            }
            catch (CryptographicException)
            {
                return new Call(null, "The saved API key can no longer be read (the server's keys changed). Enter it again on Admin → AI parser.", false);
            }
        }

        var watch = Stopwatch.StartNew();
        var response = await fetcher.SendAsync(new OutboundRequest(uri, HttpMethod.Post, AiProtocol.Request(s.Provider, s.Model!, system, user, maxTokens),
            "application/json", AiProtocol.Headers(s.Provider, key), permit, AiParserRules.Timeout, AiParserRules.MaxResponseBytes), ct).ConfigureAwait(false);
        var ms = (int)watch.ElapsedMilliseconds;
        if (response.Failure is FetchFailure.Refused or FetchFailure.Blocked)
        {
            await LogAsync(s, purpose, 0, 0, ms, AiCallResult.Refused, response.Error, ct).ConfigureAwait(false);
            return new Call(null, "The AI endpoint is not allowed: " + response.Error, true);
        }

        var reply = AiProtocol.ReadResponse(s.Provider, response.Body);
        if (!response.Ok || reply.Error is not null)
        {
            var error = reply.Error ?? response.Error ?? "The AI service did not answer.";
            await LogAsync(s, purpose, reply.InputTokens, reply.OutputTokens, ms, AiCallResult.Failed, error, ct).ConfigureAwait(false);
            return new Call(null, error, true);
        }

        await LogAsync(s, purpose, reply.InputTokens, reply.OutputTokens, ms, AiCallResult.Ok, null, ct).ConfigureAwait(false);
        return new Call(reply.Text, null, true);
    }

    private async Task LogAsync(AiSettings s, AiPurpose purpose, long input, long output, int ms, AiCallResult result, string? error, CancellationToken ct)
    {
        db.AiCallLogs.Add(new AiCallLog
        {
            At = clock.GetUtcNow(),
            Purpose = purpose,
            Provider = s.Provider,
            Model = Core.Audit.AuditRules.Truncate(s.Model, 100),
            InputTokens = input,
            OutputTokens = output,
            DurationMs = ms,
            Result = result,
            Error = error is null ? null : Core.Audit.AuditRules.Truncate(error, 300),
            UserId = actor.Current.UserId,
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task MarkLastAsync(AiCallResult result, string error, CancellationToken ct)
    {
        var last = await db.AiCallLogs.OrderByDescending(l => l.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (last is not null)
        {
            last.Result = result;
            last.Error = error;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }
}
