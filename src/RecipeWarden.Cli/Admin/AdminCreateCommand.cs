using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Identity;

namespace RecipeWarden.Cli.Admin;

/// <summary>FR-INST-04, SEC-05: create an Admin account from the server shell.</summary>
internal static class AdminCreateCommand
{
    public static Task<int> RunAsync(IConfiguration config, string email, string password, TextWriter output, TextWriter error, CancellationToken cancellationToken) =>
        RunAsync(config, email, () => password, output, error, cancellationToken);

    /// <summary>
    /// AC-100: the email is checked (format, then whether an account already has it) before
    /// <paramref name="readPassword"/> is called, so nobody types a password for an address that will be refused.
    /// </summary>
    public static async Task<int> RunAsync(IConfiguration config, string email, Func<string> readPassword, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(readPassword);

        if (AccountRules.ValidateEmail(email) is { } invalid)
        {
            await error.WriteLineAsync($"error: {invalid}");
            return 1;
        }

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddRecipeWardenData(config);
        services.AddRecipeWardenIdentityCore();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(cancellationToken);
        var accounts = scope.ServiceProvider.GetRequiredService<UserAdminService>();

        var check = await accounts.CheckNewAdminEmailAsync(email);
        var result = check.Succeeded ? await accounts.CreateAdminAsync(email, readPassword()) : check;
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors)
            {
                await error.WriteLineAsync($"error: {e}");
            }

            return 1;
        }

        await output.WriteLineAsync($"Admin account {email} created.");
        return 0;
    }

    /// <summary>Reads a password without echo when interactive, or one line from stdin when piped.</summary>
    public static string ReadPassword(bool fromStdin)
    {
        if (fromStdin || Console.IsInputRedirected)
        {
            return Console.In.ReadLine() ?? string.Empty;
        }

        Console.Error.Write("Password (12+ characters): ");
        var chars = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (chars.Count > 0)
                {
                    chars.RemoveAt(chars.Count - 1);
                }

                continue;
            }

            chars.Add(key.KeyChar);
        }

        Console.Error.WriteLine();
        return new string(chars.ToArray());
    }
}
