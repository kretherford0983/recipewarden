using RecipeWarden.Core.Audit;
using RecipeWarden.Core.Security;

namespace RecipeWarden.Core.Tests;

[Trait("Req", "SEC-12")]
[Trait("Req", "AC-72")]
public sealed class SEC_12_UserAdminRulesTests
{
    [Fact]
    public void The_last_admin_cannot_be_demoted_locked_or_deleted()
    {
        UserAdminRules.Check(UserAction.ChangeRole, "a", "b", targetIsAdmin: true, adminCount: 1, AppRoles.Editor).ShouldNotBeNull().ShouldContain("last Admin");
        UserAdminRules.Check(UserAction.ChangeRole, "a", "b", true, 1, UserAdminRules.NoRole).ShouldNotBeNull();
        UserAdminRules.Check(UserAction.Lock, "a", "b", true, 1).ShouldNotBeNull();
        UserAdminRules.Check(UserAction.Delete, "a", "b", true, 1).ShouldNotBeNull();
    }

    [Fact]
    public void With_two_admins_one_can_be_demoted()
    {
        UserAdminRules.Check(UserAction.ChangeRole, "a", "b", true, 2, AppRoles.Editor).ShouldBeNull();
        UserAdminRules.Check(UserAction.Delete, "a", "b", true, 2).ShouldBeNull();
        UserAdminRules.Check(UserAction.ChangeRole, "a", "b", true, 1, AppRoles.Admin).ShouldBeNull(); // staying Admin is fine
    }

    [Theory]
    [InlineData(UserAction.ChangeRole, "own role")]
    [InlineData(UserAction.Lock, "own account")]
    [InlineData(UserAction.Delete, "own account")]
    public void Nobody_acts_on_their_own_account(UserAction action, string fragment) =>
        UserAdminRules.Check(action, "a", "a", false, 3, AppRoles.Editor).ShouldNotBeNull().ShouldContain(fragment);

    [Theory]
    [InlineData(null)]
    [InlineData("Owner")]
    [InlineData("admin")]
    public void Only_known_roles_can_be_assigned(string? role) =>
        UserAdminRules.Check(UserAction.ChangeRole, "a", "b", false, 1, role).ShouldNotBeNull().ShouldContain("Choose");

    [Fact]
    public void Editors_and_readers_can_be_managed() =>
        UserAdminRules.Check(UserAction.Lock, "a", "b", false, 1).ShouldBeNull();
}

[Trait("Req", "FR-ADM-01")]
[Trait("Req", "SEC-72")]
public sealed class FR_ADM_01_AuditRulesTests
{
    [Theory]
    [InlineData("Recipe", AuditChange.Create, "Recipe.Create")]
    [InlineData("AppUser", AuditChange.Update, "User.Update")]
    [InlineData("IdentityUserRole", AuditChange.Delete, "UserRole.Delete")]
    [InlineData("IdentityRole", AuditChange.Create, "Role.Create")]
    [InlineData("RecipeCollection", AuditChange.Update, "Collection.Update")]
    public void Actions_are_named_after_the_entity(string entity, AuditChange change, string action) =>
        AuditRules.ActionName(entity, change).ShouldBe(action);

    [Fact]
    public void Long_names_are_capped() => AuditRules.ActionName(new string('x', 100), AuditChange.Create).Length.ShouldBe(AuditRules.MaxActionLength);

    [Fact]
    public void Bookkeeping_and_sign_in_counters_are_not_audited()
    {
        AuditRules.ShouldAudit("AuditEntry", AuditChange.Create, []).ShouldBeFalse();
        AuditRules.ShouldAudit("StorageSnapshot", AuditChange.Create, []).ShouldBeFalse();
        AuditRules.ShouldAudit("AppUser", AuditChange.Update, ["AccessFailedCount", "ConcurrencyStamp"]).ShouldBeFalse();
        AuditRules.ShouldAudit("Recipe", AuditChange.Update, []).ShouldBeFalse();
    }

    [Fact]
    public void Real_writes_are_audited()
    {
        AuditRules.ShouldAudit("Recipe", AuditChange.Create, []).ShouldBeTrue();
        AuditRules.ShouldAudit("Recipe", AuditChange.Delete, []).ShouldBeTrue();
        AuditRules.ShouldAudit("Recipe", AuditChange.Update, ["Title"]).ShouldBeTrue();
        AuditRules.ShouldAudit("AppUser", AuditChange.Update, ["PasswordHash", "SecurityStamp"]).ShouldBeTrue();
        AuditRules.ShouldAudit("AppUser", AuditChange.Update, ["LockoutEnd", "Email"]).ShouldBeTrue();
    }

    [Fact]
    public void Truncate_keeps_short_text()
    {
        AuditRules.Truncate("abc", 5).ShouldBe("abc");
        AuditRules.Truncate("abcdef", 3).ShouldBe("abc");
        AuditRules.Truncate(null, 3).ShouldBe(string.Empty);
    }
}
