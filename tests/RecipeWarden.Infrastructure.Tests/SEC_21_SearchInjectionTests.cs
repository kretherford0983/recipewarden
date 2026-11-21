using Microsoft.EntityFrameworkCore;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>
/// SEC-21, AC-22: hostile search input against a real SQLite FTS5 table returns safely, never raises an FTS or SQL
/// error, and changes nothing.
/// </summary>
[Trait("Req", "SEC-21")]
[Trait("Req", "AC-22")]
public sealed class SEC_21_SearchInjectionTests
{
    public static TheoryData<string> Hostile() => new(
        "\" OR 1=1 --",
        "NEAR(",
        "NEAR(chicken lime, 2)",
        "chicken AND",
        "OR",
        "NOT lime",
        "title:chicken",
        "{title notes}: lime",
        "-lime",
        "^chicken",
        "*",
        "chick*",
        "\"",
        "\"\"",
        "'",
        "';",
        "'; DROP TABLE Recipes; --",
        "') OR ('1'='1",
        "rowid = 1",
        "lime) UNION SELECT name FROM sqlite_master --",
        "\u0000",
        "lime\u0000chicken",
        "\ud800",
        "%",
        "_",
        "\\",
        new string('a', 10_000),
        string.Join(' ', Enumerable.Repeat("x", 500)));

    [Theory]
    [MemberData(nameof(Hostile))]
    public async Task Hostile_input_returns_safely_and_changes_nothing(string input)
    {
        await using var s = await RecipeTestScope.CreateAsync();
        await s.AddAsync("Chicken Lime Soup", "1 lb chicken\n2 limes", "Cook.");
        await s.AddAsync("Draft", publish: false);

        var results = await s.SearchAsync(input); // any FTS or SQL error would throw here

        results.Total.ShouldBeInRange(0, 1);
        (await s.Db.Recipes.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(2);
        (await s.SearchAsync("chicken")).Total.ShouldBe(1); // the index still works
    }

    [Fact]
    public async Task Operator_words_are_searched_as_plain_words()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        await s.AddAsync("Salt or Pepper", "1 pinch salt", "Season.");
        await s.AddAsync("Chicken", "1 chicken", "Roast.");

        (await s.SearchAsync("or")).Items.Single().Title.ShouldBe("Salt or Pepper");
        (await s.SearchAsync("chicken NOT")).Items.Select(r => r.Title).ShouldContain("Chicken");
    }
}
