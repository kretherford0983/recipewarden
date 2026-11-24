using System.Net;

namespace RecipeWarden.Web.Tests;

/// <summary>FR-IMP-11, AC-104, BR-118, D4: the real NLP parser's second reading, shown on review and never applied.</summary>
[Trait("Req", "FR-IMP-11")]
public sealed class FR_IMP_11_SecondOpinionPageTests : IClassFixture<FR_IMP_11_SecondOpinionPageTests.Factory>
{
    private readonly RecipeWardenFactory factory;

    public FR_IMP_11_SecondOpinionPageTests(Factory f) => factory = f.Host;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public sealed class Factory : IDisposable
    {
        public Factory()
        {
            Host = new RecipeWardenFactory();
            PythonWebTestKit.UseRealWorker(Host);
        }

        public RecipeWardenFactory Host { get; }

        public void Dispose() => Host.Dispose();
    }

    [Fact]
    [Trait("Req", "AC-104")]
    public async Task A_line_read_differently_is_marked_on_review_and_saving_keeps_the_text()
    {
        using var editor = await factory.SignInAsync("imp11-editor@example.com");
        var pasted = await editor.PostFormAsync("/manage/import?handler=Paste",
            ("PasteText", "Milk Rolls\n\nIngredients\n1-1/2 cups milk\n2 large eggs\n3 cups flour\n\nDirections\nMix and bake."));
        pasted.StatusCode.ShouldBe(HttpStatusCode.Redirect, await pasted.TextAsync());
        var review = pasted.Headers.Location!.ToString();

        var page = WebUtility.HtmlDecode(await editor.GetStringAsync(review, Ct));
        page.ShouldContain("<span class=\"badge\">Second reading</span> <q>1-1/2 cups milk</q> — A second reading differs on the amount: ½–1 cup milk.");
        page.ShouldNotContain("<q>2 large eggs</q> —");
        page.ShouldNotContain("<q>3 cups flour</q> —");

        var saved = await editor.PostFormAsync(review,
            ("Input.Title", "Milk Rolls"), ("Input.Servings", "8"), ("Input.Ingredients", "1-1/2 cups milk\n2 large eggs\n3 cups flour"),
            ("Input.Steps", "Mix and bake."), ("Input.SourceType", "Manual"), ("Action", "publish"));
        saved.StatusCode.ShouldBe(HttpStatusCode.Redirect, await saved.TextAsync());
        var recipe = WebUtility.HtmlDecode(await editor.GetStringAsync(saved.Headers.Location!.ToString(), Ct));
        recipe.ShouldContain("1½ cups milk"); // the .NET reading, not the second one
        recipe.ShouldNotContain("½–1 cup milk");
    }
}

/// <summary>AC-103, BR-119: without a worker an import is reviewed with the v1 warnings only, and nothing fails.</summary>
[Trait("Req", "AC-103")]
public sealed class FR_IMP_11_NoWorkerTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    [Fact]
    public async Task Without_a_worker_the_review_has_no_second_readings()
    {
        using var editor = await factory.SignInAsync("imp11n-editor@example.com");
        var pasted = await editor.PostFormAsync("/manage/import?handler=Paste",
            ("PasteText", "Milk Rolls\n\nIngredients\n1-1/2 cups milk\n\nDirections\nMix."));
        pasted.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var page = await editor.GetStringAsync(pasted.Headers.Location!.ToString(), TestContext.Current.CancellationToken);
        page.ShouldNotContain("Second reading");
    }
}
