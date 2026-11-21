using RecipeWarden.Core.Recipes;
using RecipeWarden.Core.Security;

namespace RecipeWarden.Core.Tests;

/// <summary>BR-05, AC-04: deleted recipes stay restorable for 30 days, then purge; only Admin purges early.</summary>
[Trait("Req", "BR-05")]
[Trait("Req", "FR-REC-04")]
[Trait("Req", "AC-04")]
public sealed class BR_05_TrashRulesTests
{
    private static readonly DateTimeOffset Deleted = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Retention_is_thirty_days()
    {
        TrashRules.RetentionPeriod.ShouldBe(TimeSpan.FromDays(30));
    }

    [Fact]
    public void Day_29_is_restorable_and_not_due_for_purge()
    {
        var now = Deleted.AddDays(29);
        TrashRules.CanRestore(Deleted, now).ShouldBeTrue();
        TrashRules.IsPurgeDue(Deleted, now).ShouldBeFalse();
    }

    [Fact]
    public void One_tick_before_thirty_days_is_still_restorable()
    {
        var now = Deleted + TrashRules.RetentionPeriod - TimeSpan.FromTicks(1);
        TrashRules.IsPurgeDue(Deleted, now).ShouldBeFalse();
    }

    [Fact]
    public void Exactly_thirty_days_is_due_for_purge_and_no_longer_restorable()
    {
        var now = Deleted + TrashRules.RetentionPeriod;
        TrashRules.IsPurgeDue(Deleted, now).ShouldBeTrue();
        TrashRules.CanRestore(Deleted, now).ShouldBeFalse();
    }

    [Fact]
    public void Purge_cutoff_is_now_minus_retention()
    {
        TrashRules.PurgeCutoff(Deleted).ShouldBe(Deleted.AddDays(-30));
    }

    [Fact]
    public void Purge_date_is_shown_from_the_delete_date()
    {
        TrashRules.PurgesOn(Deleted).ShouldBe(Deleted.AddDays(30));
    }

    [Fact]
    public void Only_admin_can_purge_immediately()
    {
        TrashRules.CanPurgeImmediately([AppRoles.Admin]).ShouldBeTrue();
        TrashRules.CanPurgeImmediately([AppRoles.Editor]).ShouldBeFalse();
        TrashRules.CanPurgeImmediately([]).ShouldBeFalse();
        TrashRules.CanPurgeImmediately(["admin"]).ShouldBeFalse(); // role names are exact
    }
}
