using System.CommandLine;
using RecipeWarden.Cli.Admin;
using RecipeWarden.Cli.ApplyConfig;
using RecipeWarden.Cli.Health;
using RecipeWarden.Cli.Hosting;
using RecipeWarden.Cli.Nutrition;

var configOption = new Option<string?>("--config")
{
    Description = $"Path to the server config (default: ${CliConfig.ConfigPathVariable} or {CliConfig.DefaultConfigPath})",
    Recursive = true,
};

var root = new RootCommand("RecipeWarden server administration");
root.Options.Add(configOption);

// admin create
var email = new Option<string>("--email") { Description = "Email (also the user name)", Required = true };
var passwordStdin = new Option<bool>("--password-stdin") { Description = "Read the password from standard input" };
var adminCreate = new Command("create", "Create an Admin account") { email, passwordStdin };
adminCreate.SetAction(async (parse, ct) =>
{
    var config = CliConfig.Load(CliConfig.ResolvePath(parse.GetValue(configOption)));
    var fromStdin = parse.GetValue(passwordStdin);
    return await AdminCreateCommand.RunAsync(config, parse.GetValue(email)!, () => AdminCreateCommand.ReadPassword(fromStdin), Console.Out, Console.Error, ct);
});
var admin = new Command("admin", "Account administration") { adminCreate };
root.Subcommands.Add(admin);

// health
var health = new Command("health", "Print a health summary");
health.SetAction(async (parse, ct) =>
{
    var config = CliConfig.Load(CliConfig.ResolvePath(parse.GetValue(configOption)));
    return await HealthCommand.RunAsync(config, Console.Out, ct);
});
root.Subcommands.Add(health);

// apply-config
var dryRun = new Option<bool>("--dry-run") { Description = "Validate and show what would change; change nothing" };
var noRestart = new Option<bool>("--no-restart") { Description = "Write the drop-in and reload systemd, but do not restart" };
var noReload = new Option<bool>("--no-reload") { Description = "Write the drop-in only; do not reload or restart systemd" };
var applyConfig = new Command("apply-config", "Open Backup:AllowedFolders in the service sandbox (FR-BKP-09)") { dryRun, noRestart, noReload };
applyConfig.SetAction(async (parse, ct) =>
{
    var config = CliConfig.Load(CliConfig.ResolvePath(parse.GetValue(configOption)));
    var folders = config.GetSection("Backup:AllowedFolders").GetChildren().Select(c => c.Value).ToList();
    var runner = new ApplyConfigRunner(
        new HostFileSystem(),
        new Systemctl(),
        new HttpHealthProbe(CliConfig.HealthUri(config), TimeProvider.System),
        Console.Out,
        Console.Error);
    var osUser = Environment.GetEnvironmentVariable("SUDO_USER") ?? Environment.UserName;
    return await runner.RunAsync(
        new ApplyConfigRequest(folders, parse.GetValue(dryRun), parse.GetValue(noRestart), Environment.IsPrivilegedProcess, osUser)
        {
            NoReload = parse.GetValue(noReload),
        }, ct);
});
root.Subcommands.Add(applyConfig);

// nutrition-import (FR-NUT-05, ADR 0034)
var datasetFile = new Argument<string>("zip") { Description = "A USDA FoodData Central CSV .zip (SR Legacy or Foundation Foods)" };
var datasetVersion = new Option<string?>("--version") { Description = "A name for this dataset, e.g. \"SR Legacy 2018-04\" (default: the file name)" };
var nutritionImport = new Command("nutrition-import", "Apply a newer USDA nutrient dataset from a file (FR-NUT-05)") { datasetFile, datasetVersion };
nutritionImport.SetAction(async (parse, ct) =>
{
    var config = CliConfig.Load(CliConfig.ResolvePath(parse.GetValue(configOption)));
    return await NutritionImportCommand.RunAsync(config, parse.GetValue(datasetFile)!, parse.GetValue(datasetVersion), Console.Out, Console.Error, ct);
});
root.Subcommands.Add(nutritionImport);

return await root.Parse(args).InvokeAsync();
