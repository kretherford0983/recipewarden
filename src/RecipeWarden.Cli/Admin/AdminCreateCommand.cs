using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RecipeWarden.Infrastructure;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Identity;

namespace RecipeWarden.Cli.Admin;

/// <summary>FR-INST-04, SEC-05: create an Admin account from the server shell.</summary>
internal static class AdminCreateCommand
{
    public static async Task<int> RunAsync(IConfiguration config, string email, string password, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddRecipeWardenData(config);
        services.AddRecipeWardenIdentityCore();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(cancellationToken);

        var result = await scope.ServiceProvider.GetRequiredService<UserAdminService>().CreateAdminAsync(email, password);
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
