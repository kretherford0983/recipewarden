using RecipeWarden.Core.Import;
using RecipeWarden.Importers.Ingredients;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>
/// FR-IMP-11, BR-118, plan 6 (corpus row): the 574-line corpus through both parsers. The disagreements are kept in
/// tests/contracts/corpus-disagreements.txt, so a change in either parser (a library upgrade, a .NET parser fix)
/// shows up as a diff in review. To accept a new list: RW_ACCEPT_SNAPSHOT=1 dotnet test ... --filter-class "*Corpus*".
/// This is also the parser oracle (tools/compare-parsers): each line is a lead for a possible .NET parser bug.
/// </summary>
[Trait("Req", "FR-IMP-11")]
public sealed class FR_IMP_11_CorpusDisagreementTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_disagreements_on_the_corpus_match_the_reviewed_list()
    {
        var corpus = Path.Combine(PythonTestKit.RepoRoot, "tests", "RecipeWarden.Importers.Tests", "Corpus", "ingredient-lines.txt");
        var raws = File.ReadLines(corpus).Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('|')[1].Trim()).ToList();
        var (worker, _) = PythonTestKit.Worker(new Python.PythonOptions());
        var lines = new List<string>();
        foreach (var batch in raws.Chunk(500))
        {
            var call = await worker.ParseAsync(batch, fdc: false, Ct);
            call.Ok.ShouldBeTrue(call.Detail ?? "no Python environment: run build/python.sh dev");
            var readings = Python.WorkerContract.Readings(call.Result!, batch.Length)!;
            for (var i = 0; i < batch.Length; i++)
            {
                if (IngredientLineParser.Parse(batch[i]) is { } ours && SecondOpinionRules.Compare(ours, readings[i]) is { } opinion)
                {
                    lines.Add($"{batch[i]} | {opinion.Differences} | {opinion.OtherReading}");
                }
            }
        }

        var actual = "# FR-IMP-11: corpus lines the NLP parser reads differently (BR-118). Generated; review changes.\n"
            + $"# {lines.Count} of {raws.Count} lines\n" + string.Join('\n', lines) + "\n";
        var snapshot = Path.Combine(PythonTestKit.Contracts, "corpus-disagreements.txt");
        if (Environment.GetEnvironmentVariable("RW_ACCEPT_SNAPSHOT") == "1")
        {
            await File.WriteAllTextAsync(snapshot, actual, Ct);
        }

        File.Exists(snapshot).ShouldBeTrue("no reviewed list yet: run with RW_ACCEPT_SNAPSHOT=1 and review the file");
        var expected = (await File.ReadAllTextAsync(snapshot, Ct)).ReplaceLineEndings("\n");
        if (expected != actual)
        {
            await File.WriteAllTextAsync(snapshot + ".received", actual, Ct);
        }

        actual.ShouldBe(expected, "the parsers' disagreements changed; compare with " + snapshot + ".received");
        lines.Count.ShouldBeLessThan(raws.Count / 4, "far more disagreements than the spike found (F4)");
    }
}
