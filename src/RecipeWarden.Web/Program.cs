using RecipeWarden.Web.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddRecipeWarden();

var app = builder.Build();
await app.InitializeDatabaseAsync();
app.UseRecipeWarden();

await app.RunAsync();

/// <summary>Entry point; partial so WebApplicationFactory can reference it.</summary>
public partial class Program
{
}
