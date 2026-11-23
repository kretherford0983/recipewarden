using Microsoft.Extensions.Configuration;
using System.CommandLine;
using RecipeWarden.Cli.Admin;
using RecipeWarden.Cli.ApplyConfig;
using RecipeWarden.Cli.Health;
using RecipeWarden.Cli.Hosting;
using RecipeWarden.Cli.Nutrition;
using RecipeWarden.Cli.Operations;
using RecipeWarden.Infrastructure.Data;

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

// health (FR-OPS-02 for SSH use)
var health = new Command("health", "Print a health summary (exit 0 OK, 1 Warning, 2 Critical)");
health.SetAction(async (parse, ct) =>
{
    var config = CliConfig.Load(CliConfig.ResolvePath(parse.GetValue(configOption)));
    return await OperationsCommands.HealthAsync(config, Console.Out, ct);
});
root.Subcommands.Add(health);

// backup (FR-BKP-01; also the installer's pre-upgrade backup)
var backupFolder = new Option<string?>("--folder") { Description = "An allowed backup folder (default: the built-in folder)" };
var backupKind = new Option<string?>("--kind") { Description = "manual (default) or pre-upgrade" };
var backup = new Command("backup", "Make a backup now and print its path") { backupFolder, backupKind };
backup.SetAction(async (parse, ct) =>
{
    var config = CliConfig.Load(CliConfig.ResolvePath(parse.GetValue(configOption)));
    return await OperationsCommands.BackupAsync(config, parse.GetValue(backupFolder), parse.GetValue(backupKind), Console.Out, Console.Error, ct);
});
root.Subcommands.Add(backup);

// restore (FR-BKP-07, AC-85)
var restoreArchive = new Argument<string>("archive") { Description = "A backup archive (.tar.gz or .tar.gz.rwenc)" };
var restoreKit = new Option<string?>("--kit") { Description = "The recovery kit file (encrypted archives)" };
var restorePassStdin = new Option<bool>("--passphrase-stdin") { Description = "Read the kit passphrase from standard input" };
var restoreCheck = new Option<bool>("--check") { Description = "Only check the archive (as the service user); change nothing" };
var restoreApply = new Option<bool>("--apply") { Description = "Restore as the service user; the service must be stopped" };
var restoreNoPre = new Option<bool>("--no-pre-restore") { Description = "With --apply: skip the pre-restore backup (fresh installs)" };
var restoreYes = new Option<bool>("--yes") { Description = "Do not ask for the confirmation word" };
var restore = new Command("restore", "Restore a backup: check it, stop the service, restore, migrate, start (run with sudo)")
{
    restoreArchive, restoreKit, restorePassStdin, restoreCheck, restoreApply, restoreNoPre, restoreYes,
};
restore.SetAction(async (parse, ct) =>
{
    var configPath = Path.GetFullPath(CliConfig.ResolvePath(parse.GetValue(configOption)));
    var config = CliConfig.Load(configPath);
    var kit = parse.GetValue(restoreKit);
    var passphrase = kit is null ? null : AdminCreateCommand.ReadSecret(parse.GetValue(restorePassStdin), "Recovery kit passphrase: ");
    if (parse.GetValue(restoreCheck) || parse.GetValue(restoreApply))
    {
        return await OperationsCommands.RestoreOfflineAsync(config, parse.GetValue(restoreArchive)!, kit, passphrase, parse.GetValue(restoreApply),
            !parse.GetValue(restoreNoPre), "cli", Console.Out, Console.Error, ct);
    }

    if (!Environment.IsPrivilegedProcess)
    {
        await Console.Error.WriteLineAsync("error: run with sudo (it stops and starts the service), or use --check / --apply as the service user.");
        return 1;
    }

    var data = new DataOptions();
    config.GetSection(DataOptions.SectionName).Bind(data);
    var orchestrator = new RestoreOrchestrator(new HostRunner(Console.Out, Console.Error), new Systemctl(),
        new HttpHealthProbe(CliConfig.HealthUri(config), TimeProvider.System), data, Console.Out, Console.Error, Console.ReadLine);
    return await orchestrator.RunAsync(new RestoreRequest(parse.GetValue(restoreArchive)!, kit is null ? null : Path.GetFullPath(kit), passphrase,
        parse.GetValue(restoreYes), configPath), ct);
});
root.Subcommands.Add(restore);

// setup-token (SEC-15)
var setupToken = new Command("setup-token", "Print a one-time token for the /setup page (only before the first Admin exists)");
setupToken.SetAction(async (parse, ct) =>
{
    var config = CliConfig.Load(CliConfig.ResolvePath(parse.GetValue(configOption)));
    return await OperationsCommands.SetupTokenAsync(config, Console.Out, Console.Error, ct);
});
root.Subcommands.Add(setupToken);

// internal-audit (hidden): lets root-run commands leave an audit entry written by the service user
var auditAction = new Argument<string>("action");
var auditDetails = new Argument<string?>("details") { Arity = ArgumentArity.ZeroOrOne };
var internalAudit = new Command("internal-audit", "Record an audit entry (used by apply-config)") { auditAction, auditDetails };
internalAudit.Hidden = true;
internalAudit.SetAction(async (parse, ct) =>
{
    var config = CliConfig.Load(CliConfig.ResolvePath(parse.GetValue(configOption)));
    return await OperationsCommands.InternalAuditAsync(config, parse.GetValue(auditAction)!, parse.GetValue(auditDetails), ct);
});
root.Subcommands.Add(internalAudit);

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
    var exit = await runner.RunAsync(
        new ApplyConfigRequest(folders, parse.GetValue(dryRun), parse.GetValue(noRestart), Environment.IsPrivilegedProcess, osUser)
        {
            NoReload = parse.GetValue(noReload),
        }, ct);

    // FR-ADM-01, FR-BKP-09: the change is also recorded in the audit log, written by the service user (never as root,
    // so no database file can end up owned by root). Best effort: the install may not have a database yet.
    if (exit == 0 && !parse.GetValue(dryRun) && Environment.IsPrivilegedProcess)
    {
        var data = new DataOptions();
        config.GetSection(DataOptions.SectionName).Bind(data);
        if (File.Exists(data.DatabasePath))
        {
            var host = new HostRunner(TextWriter.Null, TextWriter.Null);
            if (await host.OwnerOfAsync(data.Directory, ct) is { } owner and not "root")
            {
                await host.RunCliAsAsync(owner, ["--config", Path.GetFullPath(CliConfig.ResolvePath(parse.GetValue(configOption))), "internal-audit", "ApplyConfig.Run",
                    "by " + osUser + ": " + string.Join(", ", folders)], null, ct);
            }
        }
    }

    return exit;
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
