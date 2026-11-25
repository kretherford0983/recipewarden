namespace RecipeWarden.Tests.Shared;

/// <summary>A clock tests can set and move, for time rules such as the 30-day trash (BR-05). No sleeps.</summary>
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset now = start;

    public ManualClock()
        : this(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero))
    {
    }

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan by) => now += by;
}
