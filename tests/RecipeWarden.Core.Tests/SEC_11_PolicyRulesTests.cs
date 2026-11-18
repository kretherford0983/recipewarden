using RecipeWarden.Core.Security;

namespace RecipeWarden.Core.Tests;

[Trait("Req", "SEC-11")]
public sealed class SEC_11_PolicyRulesTests
{
    [Fact]
    public void Two_roles_exist() => AppRoles.All.ShouldBe([AppRoles.Admin, AppRoles.Editor], ignoreOrder: true);

    [Fact]
    public void Editors_and_admins_can_edit_recipes() =>
        AppPolicies.EditorRoles.ShouldBe([AppRoles.Editor, AppRoles.Admin], ignoreOrder: true);

    [Fact]
    public void Only_admins_can_administer() => AppPolicies.AdminRoles.ShouldBe([AppRoles.Admin]);

    [Fact]
    [Trait("Req", "SEC-01")]
    [Trait("Req", "SEC-02")]
    [Trait("Req", "SEC-03")]
    public void Account_rules_match_the_spec()
    {
        AccountRules.MinimumPasswordLength.ShouldBe(12);
        AccountRules.MaxFailedAccessAttempts.ShouldBe(5);
        AccountRules.LockoutDuration.ShouldBe(TimeSpan.FromMinutes(15));
        AccountRules.SessionSlidingExpiry.ShouldBe(TimeSpan.FromHours(8));
        AccountRules.SecurityStampValidationInterval.ShouldBe(TimeSpan.FromMinutes(5));
    }
}
