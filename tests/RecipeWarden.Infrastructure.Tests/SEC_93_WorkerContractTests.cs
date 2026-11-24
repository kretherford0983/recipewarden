using System.Text.Json;
using RecipeWarden.Core.Python;
using RecipeWarden.Infrastructure.Python;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>
/// SEC-93, plan 4.2: the .NET client accepts every golden answer in tests/contracts (the same files pytest checks the
/// worker against) and rejects every file in tests/contracts/invalid, and every value it keeps has been checked.
/// </summary>
[Trait("Req", "SEC-93")]
public sealed class SEC_93_WorkerContractTests
{
    private static WorkerEnvelope<T>? Read<T>(string file, System.Text.Json.Serialization.Metadata.JsonTypeInfo<WorkerEnvelope<T>> type)
        where T : class => JsonSerializer.Deserialize(File.ReadAllBytes(Path.Combine(PythonTestKit.Contracts, file)), type);

    private static WorkerJson Json => (WorkerJson)WorkerContract.Options.TypeInfoResolver!;

    [Fact]
    public void The_golden_answers_are_accepted_and_mapped()
    {
        var parse = Read("parse-ingredients.response.json", Json.WorkerEnvelopeParseResult)!;
        WorkerContract.CheckEnvelope(parse).ShouldBeNull();
        var readings = WorkerContract.Readings(parse.Result!, 4)!;
        readings[0]!.Quantity.ShouldBe(0.5m);
        readings[0]!.QuantityMax.ShouldBe(1m);
        readings[0]!.Foods[0].DataType.ShouldBe("survey_fndds_food");
        readings[3].ShouldBeNull();
        parse.Engine.Describe("ingredient-parser-nlp").ShouldBe("ingredient-parser-nlp 2.10.0");

        var suggest = Read("suggest-foods.response.json", Json.WorkerEnvelopeSuggestResult)!;
        WorkerContract.CheckEnvelope(suggest).ShouldBeNull();
        WorkerContract.Suggestions(suggest.Result!, ["brown sugar", "gochujang"])!.Select(s => s.Foods.Count).ShouldBe([1, 0]);

        var scrape = Read("scrape-html.response.json", Json.WorkerEnvelopeScrapeResult)!;
        WorkerContract.CheckEnvelope(scrape).ShouldBeNull();
        WorkerContract.Recipe(scrape.Result!)!.Title.ShouldBe("Lemon Lentil Soup");

        var selftest = Read("selftest.response.json", Json.WorkerEnvelopeSelftestResult)!;
        WorkerContract.CheckEnvelope(selftest).ShouldBeNull();
        selftest.Result!.Passed.ShouldBeTrue();

        var error = Read("error.response.json", Json.WorkerEnvelopeParseResult)!;
        WorkerContract.CheckEnvelope(error).ShouldBeNull();
        error.Error!.Code.ShouldBe("too_many_items");
    }

    public static TheoryData<string> InvalidFiles() =>
        [.. Directory.GetFiles(Path.Combine(PythonTestKit.Contracts, "invalid"), "*.json").Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(InvalidFiles))]
    public void Every_invalid_answer_is_rejected(string file)
    {
        var path = Path.Combine(PythonTestKit.Contracts, "invalid", file);
        bool Rejected<T>(System.Text.Json.Serialization.Metadata.JsonTypeInfo<WorkerEnvelope<T>> type, Func<T, bool> content)
            where T : class
        {
            try
            {
                var envelope = JsonSerializer.Deserialize(File.ReadAllBytes(path), type);
                return WorkerContract.CheckEnvelope(envelope) is not null || (envelope!.Result is { } r && !content(r));
            }
            catch (JsonException)
            {
                return true;
            }
        }

        // Read as whichever answer it pretends to be; each must fail somewhere.
        (Rejected(Json.WorkerEnvelopeParseResult, r => WorkerContract.Readings(r, r.Lines.Count) is not null)
            && Rejected(Json.WorkerEnvelopeSuggestResult, r => WorkerContract.Suggestions(r, [.. r.Suggestions.Select(x => x.Name)]) is not null))
            .ShouldBeTrue(file);
    }

    [Theory]
    [InlineData("1.5", true)]
    [InlineData("12", true)]
    [InlineData("0.333333", true)]
    [InlineData("-1", false)]
    [InlineData("1e9", false)]
    [InlineData("NaN", false)]
    [InlineData("1.1234567", false)]
    [InlineData("12345678", false)]
    [InlineData(" 1", false)]
    public void Quantities_must_be_plain_decimals(string quantity, bool accepted)
    {
        var line = new WireLine(quantity, null, null, ["milk"], null, null, 1m, 1m, []);
        (WorkerContract.Readings(new ParseResult([line]), 1) is not null).ShouldBe(accepted);
    }

    [Fact]
    public void Counts_lengths_ids_and_confidences_are_capped()
    {
        WorkerContract.Readings(new ParseResult([null]), 2).ShouldBeNull(); // not one reading per line
        var many = new WireLine("1", null, null, [.. Enumerable.Repeat("x", 11)], null, null, 1m, 1m, []);
        WorkerContract.Readings(new ParseResult([many]), 1).ShouldBeNull();
        var food = new WireFood(0, "sr_legacy_food", "x", 1m);
        WorkerContract.Suggestions(new SuggestResult([new WireSuggestion("x", [food])]), ["x"]).ShouldBeNull();
        var branded = new WireFood(1, "branded_food", "x", 1m);
        WorkerContract.Suggestions(new SuggestResult([new WireSuggestion("x", [branded])]), ["x"]).ShouldBeNull();
        var sure = new WireFood(1, "sr_legacy_food", "x", 1.5m);
        WorkerContract.Suggestions(new SuggestResult([new WireSuggestion("x", [sure])]), ["x"]).ShouldBeNull();
        WorkerContract.Recipe(new ScrapeResult(false, true, Title: new string('t', 5000))).ShouldBeNull();
        WorkerContract.Recipe(new ScrapeResult(false, true, TotalMinutes: -5)).ShouldBeNull();
        WorkerContract.Recipe(new ScrapeResult(true, false))!.Ingredients.ShouldBeEmpty();
    }

    [Fact]
    public void Envelopes_must_be_consistent()
    {
        var engine = new WorkerEngine("1.0.0", "3.13.18", new() { ["ingredient-parser-nlp"] = "2.10.0" });
        WorkerContract.CheckEnvelope<ParseResult>(null).ShouldBe("empty answer");
        WorkerContract.CheckEnvelope(new WorkerEnvelope<ParseResult>(1, true, engine)).ShouldBe("ok answer without a result");
        WorkerContract.CheckEnvelope(new WorkerEnvelope<ParseResult>(1, false, engine, new ParseResult([]))).ShouldBe("error answer without an error");
        WorkerContract.CheckEnvelope(new WorkerEnvelope<ParseResult>(1, false, engine, null, new WorkerError("", "x"))).ShouldBe("bad error block");
        WorkerContract.CheckEnvelope(new WorkerEnvelope<ParseResult>(1, true, engine with { Worker = "" }, new ParseResult([]))).ShouldBe("bad engine block");
        engine.Describe("recipe-scrapers").ShouldBe("recipe-scrapers missing");
        engine.Describe().ShouldBe("ingredient-parser-nlp 2.10.0");
    }
}
