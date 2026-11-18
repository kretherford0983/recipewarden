using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Core.Preferences;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Identity;

namespace RecipeWarden.Infrastructure.Tests;

[Trait("Req", "FR-INST-04")]
[Trait("Req", "SEC-05")]
public sealed class SEC_05_UserAdminServiceTests
{
    [Fact]
    [Trait("Req", "SEC-11")]
    public async Task Roles_are_seeded()
    {
        await using var tdb = await TestDatabase.CreateAsync();
        await using var scope = tdb.Provider.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        (await roles.RoleExistsAsync(AppRoles.Admin)).ShouldBeTrue();
        (await roles.RoleExistsAsync(AppRoles.Editor)).ShouldBeTrue();
    }

    [Fact]
    public async Task Creates_an_admin_with_the_admin_role()
    {
        await using var tdb = await TestDatabase.CreateAsync();
        await using var scope = tdb.Provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<UserAdminService>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var result = await service.CreateAdminAsync("admin@example.com", "correct horse battery");

        result.Succeeded.ShouldBeTrue();
        var user = (await users.FindByEmailAsync("admin@example.com")).ShouldNotBeNull();
        (await users.IsInRoleAsync(user, AppRoles.Admin)).ShouldBeTrue();
        (await users.CheckPasswordAsync(user, "correct horse battery")).ShouldBeTrue();
    }

    [Fact]
    [Trait("Req", "SEC-01")]
    public async Task Refuses_passwords_shorter_than_12_characters()
    {
        await using var tdb = await TestDatabase.CreateAsync();
        await using var scope = tdb.Provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<UserAdminService>();

        var result = await service.CreateAdminAsync("admin@example.com", "short-pass1");

        result.Succeeded.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Contains("12", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refuses_a_duplicate_account()
    {
        await using var tdb = await TestDatabase.CreateAsync();
        await using var scope = tdb.Provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<UserAdminService>();

        (await service.CreateAdminAsync("admin@example.com", "correct horse battery")).Succeeded.ShouldBeTrue();
        var second = await service.CreateAdminAsync("admin@example.com", "another long password");

        second.Succeeded.ShouldBeFalse();
        second.Errors.Single().ShouldContain("already exists");
    }

    [Fact]
    [Trait("Req", "FR-UI-01")]
    public async Task Theme_preference_is_saved_and_updated()
    {
        await using var tdb = await TestDatabase.CreateAsync();
        await using var scope = tdb.Provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<UserAdminService>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var prefs = scope.ServiceProvider.GetRequiredService<UserPreferenceStore>();
        await service.CreateAdminAsync("admin@example.com", "correct horse battery");
        var id = (await users.FindByEmailAsync("admin@example.com"))!.Id;

        (await prefs.GetThemeAsync(id, TestContext.Current.CancellationToken)).ShouldBeNull();
        await prefs.SetThemeAsync(id, Theme.Dark, TestContext.Current.CancellationToken);
        (await prefs.GetThemeAsync(id, TestContext.Current.CancellationToken)).ShouldBe(Theme.Dark);
        await prefs.SetThemeAsync(id, Theme.Light, TestContext.Current.CancellationToken);
        (await prefs.GetThemeAsync(id, TestContext.Current.CancellationToken)).ShouldBe(Theme.Light);
    }
}
