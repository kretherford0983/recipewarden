using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Infrastructure.Setup;

/// <summary>
/// SEC-15, FR-BKP-06, AC-84, ADR 0040: the first-run /setup page exists only while no Admin account exists, setup
/// has not completed, and a one-time token was issued (by the installer, through the CLI). The token's SHA-256 is kept
/// in &lt;data&gt;/setup/token.json; the token itself is only printed. Once setup or a restore completes, a marker file
/// disables the page for good.
/// </summary>
public sealed class SetupService(DataOptions data, UserManager<AppUser> users, TimeProvider clock)
{
    public const int TokenBytes = 24;

    private string TokenPath => Path.Combine(data.SetupDirectory, "token.json");

    private string CompletePath => Path.Combine(data.SetupDirectory, "complete");

    public bool IsCompleted => File.Exists(CompletePath);

    /// <summary>True only while the page may be shown: no Admin, not completed, and a token exists.</summary>
    public async Task<bool> IsAvailableAsync() =>
        !IsCompleted && File.Exists(TokenPath) && (await users.GetUsersInRoleAsync(AppRoles.Admin).ConfigureAwait(false)).Count == 0;

    /// <summary>Issues a new token (replacing any earlier one). Null when an Admin exists or setup is complete.</summary>
    public async Task<string?> IssueTokenAsync()
    {
        if (IsCompleted || (await users.GetUsersInRoleAsync(AppRoles.Admin).ConfigureAwait(false)).Count > 0)
        {
            return null;
        }

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        Directory.CreateDirectory(data.SetupDirectory);
        var json = JsonSerializer.Serialize(new { sha256 = Hash(token), createdAt = clock.GetUtcNow() });
        var temp = TokenPath + ".tmp";
        await File.WriteAllTextAsync(temp, json).ConfigureAwait(false);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Move(temp, TokenPath, overwrite: true);
        return token;
    }

    /// <summary>Constant-time comparison of the presented token with the stored hash.</summary>
    public async Task<bool> CheckTokenAsync(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 200 || !await IsAvailableAsync().ConfigureAwait(false))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(TokenPath).ConfigureAwait(false));
            var stored = Convert.FromHexString(doc.RootElement.GetProperty("sha256").GetString() ?? string.Empty);
            return CryptographicOperations.FixedTimeEquals(stored, Convert.FromHexString(Hash(token)));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or IOException)
        {
            return false;
        }
    }

    /// <summary>BR-66, SEC-15: after setup or a restore, the page is disabled permanently and the token removed.</summary>
    public void Complete(string how)
    {
        Directory.CreateDirectory(data.SetupDirectory);
        File.WriteAllText(CompletePath, how + " " + clock.GetUtcNow().ToString("O", System.Globalization.CultureInfo.InvariantCulture) + "\n");
        if (File.Exists(TokenPath))
        {
            File.Delete(TokenPath);
        }
    }

    private static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
