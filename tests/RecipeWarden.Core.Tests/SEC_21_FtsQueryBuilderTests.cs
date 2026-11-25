using System.Text;
using System.Text.RegularExpressions;
using RecipeWarden.Core.Search;

namespace RecipeWarden.Core.Tests;

/// <summary>
/// SEC-21, AC-22: search input is tokenized and quoted by the app; raw input never reaches FTS5 as an expression.
/// Every expression the builder produces is a list of quoted terms joined by OR, optionally with a trailing prefix star.
/// </summary>
[Trait("Req", "SEC-21")]
[Trait("Req", "AC-22")]
[Trait("Req", "FR-SRCH-01")]
public sealed partial class SEC_21_FtsQueryBuilderTests
{
    [GeneratedRegex("""^("[^"]+"\*?)( OR "[^"]+"\*?)*$""")]
    private static partial Regex SafeExpression();

    [GeneratedRegex("""^"[^"]+"\*?$""")]
    private static partial Regex SafeTerm();

    [Fact]
    public void Two_words_become_two_quoted_terms_with_the_last_as_prefix()
    {
        var query = FtsQuery.Build("chicken lime");

        query.Terms.ShouldBe(["chicken", "lime"]);
        query.MatchAny.ShouldBe("\"chicken\" OR \"lime\"*");
        query.TermExpressions.ShouldBe(["\"chicken\"", "\"lime\"*"]);
    }

    [Fact]
    public void Trailing_space_means_the_last_word_is_complete()
    {
        FtsQuery.Build("chicken lime ").MatchAny.ShouldBe("\"chicken\" OR \"lime\"");
    }

    [Fact]
    public void Single_letter_last_word_is_not_a_prefix()
    {
        FtsQuery.Build("chicken a").TermExpressions.ShouldBe(["\"chicken\"", "\"a\""]);
    }

    [Theory]
    [InlineData("\" OR 1=1 --", new[] { "or", "1", "1" })]
    [InlineData("NEAR(", new[] { "near" })]
    [InlineData("chicken AND NOT lime", new[] { "chicken", "and", "not", "lime" })]
    [InlineData("title:chicken", new[] { "title", "chicken" })]
    [InlineData("{title notes}: chicken", new[] { "title", "notes", "chicken" })]
    [InlineData("-lime ^chicken", new[] { "lime", "chicken" })]
    [InlineData("chick*", new[] { "chick" })]
    [InlineData("'; DROP TABLE Recipe; --", new[] { "drop", "table", "recipe" })]
    [InlineData("\"\"\"", new string[0])]
    [InlineData("crème brûlée", new[] { "crème", "brûlée" })]
    public void Operators_and_quotes_are_reduced_to_plain_terms(string input, string[] terms)
    {
        var query = FtsQuery.Build(input);

        query.Terms.ShouldBe(terms.Distinct().ToArray());
        if (!query.IsEmpty)
        {
            SafeExpression().IsMatch(query.MatchAny).ShouldBeTrue(query.MatchAny);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!! *** ()")]
    [InlineData("\u0000\u0001")]
    public void Input_without_words_is_empty(string? input)
    {
        var query = FtsQuery.Build(input);

        query.IsEmpty.ShouldBeTrue();
        query.MatchAny.ShouldBe(string.Empty);
        query.TermExpressions.ShouldBeEmpty();
    }

    [Fact]
    public void Terms_are_lower_case_and_de_duplicated_in_order()
    {
        FtsQuery.Build("Lime chicken LIME").Terms.ShouldBe(["lime", "chicken"]);
    }

    [Fact]
    public void At_most_eight_terms_are_used()
    {
        var query = FtsQuery.Build(string.Join(' ', Enumerable.Range(1, 20).Select(i => $"word{i}")));
        query.Terms.Count.ShouldBe(FtsQuery.MaxTerms);
        query.Terms[^1].ShouldBe("word8");
    }

    [Fact]
    public void Long_words_are_cut()
    {
        FtsQuery.Build(new string('a', 100)).Terms.Single().Length.ShouldBe(FtsQuery.MaxTermLength);
    }

    [Fact]
    public void Only_the_start_of_very_long_input_is_read()
    {
        var input = new string(' ', FtsQuery.MaxInputLength) + "hidden";
        FtsQuery.Build(input).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Digits_and_letters_in_other_scripts_are_kept()
    {
        FtsQuery.Build("350 градусов 料理").Terms.ShouldBe(["350", "градусов", "料理"]);
    }

    [Fact]
    public void Random_input_always_yields_a_safe_expression()
    {
        // Seeded so a failure can be reproduced; covers quotes, operators, control and surrogate characters.
        var random = new Random(20261120);
        const string alphabet = "abcXYZ019 \"'*^:(){}[]-+.,;\\/\t\n\r\u0000é́‮中🍲ORANDNOTNEAR";
        for (var i = 0; i < 10_000; i++)
        {
            var sb = new StringBuilder();
            var length = random.Next(0, 60);
            for (var j = 0; j < length; j++)
            {
                sb.Append(alphabet[random.Next(alphabet.Length)]);
            }

            var input = sb.ToString();
            var query = FtsQuery.Build(input);

            if (query.IsEmpty)
            {
                query.MatchAny.ShouldBeEmpty();
                continue;
            }

            SafeExpression().IsMatch(query.MatchAny).ShouldBeTrue($"input {Escape(input)} gave {query.MatchAny}");
            query.TermExpressions.ShouldAllBe(t => SafeTerm().IsMatch(t));
            query.Terms.ShouldAllBe(t => t.All(char.IsLetterOrDigit) || t.Any(c => char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark));
        }
    }

    private static string Escape(string s) =>
        string.Concat(s.Select(c => c < 0x20 || c > 0x7e ? $"\\u{(int)c:x4}" : c.ToString()));
}
