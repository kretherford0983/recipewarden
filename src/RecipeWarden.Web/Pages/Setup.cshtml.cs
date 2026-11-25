using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Audit;
using RecipeWarden.Infrastructure.Backup;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Identity;
using RecipeWarden.Infrastructure.Import;
using RecipeWarden.Infrastructure.Setup;

namespace RecipeWarden.Web.Pages;

/// <summary>
/// SEC-15, FR-BKP-06, BR-64, BR-66, AC-84: the first-run page. It exists only while no Admin account exists and the
/// installer issued a setup token (otherwise 404); every action needs that token. "Start new" creates the first
/// Admin; "Restore from backup" restores an archive (validated like any restore). Either one disables the page for good.
/// </summary>
[RequestSizeLimit(Admin.BackupsModel.MaxRequestBytes)]
[RequestFormLimits(MultipartBodyLengthLimit = Admin.BackupsModel.MaxRequestBytes)]
public sealed class SetupModel(SetupService setup, UserAdminService accounts, RestoreService restore, BackupOptions options, BackupGate gate,
    MaintenanceMode maintenance, AuditLog audit, DataOptions data) : PageModel
{
    public string? Error { get; private set; }

    public long MaxUploadBytes => options.MaxUploadBytes;

    public async Task<IActionResult> OnGetAsync() => await setup.IsAvailableAsync() ? Page() : NotFound();

    public async Task<IActionResult> OnPostCreateAsync(string? token, string? email, string? password)
    {
        if (!await setup.IsAvailableAsync())
        {
            return NotFound();
        }

        if (!await setup.CheckTokenAsync(token))
        {
            return Fail("The setup token is not right. It was printed by the installer; run \"sudo recipewarden setup-token\" for a new one.");
        }

        var created = await accounts.CreateAdminAsync((email ?? string.Empty).Trim(), password ?? string.Empty);
        if (!created.Succeeded)
        {
            return Fail(string.Join(" ", created.Errors));
        }

        setup.Complete("admin");
        await audit.RecordAsync("Setup.Complete", "User", email, new { how = "new" }, HttpContext.RequestAborted);
        return Redirect("/account/login?setup=done");
    }

    public async Task<IActionResult> OnPostRestoreAsync(string? token, IFormFile? archive, IFormFile? kitFile, string? passphrase)
    {
        if (!await setup.IsAvailableAsync())
        {
            return NotFound();
        }

        if (!await setup.CheckTokenAsync(token))
        {
            return Fail("The setup token is not right. It was printed by the installer; run \"sudo recipewarden setup-token\" for a new one.");
        }

        if (archive is not { Length: > 0 })
        {
            return Fail("Choose a backup archive.");
        }

        var temp = Path.Combine(data.WorkDirectory, "upload-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            await using (var stream = archive.OpenReadStream())
            {
                var error = await UploadValidator.SaveArchiveAsync(stream, archive.FileName, archive.Length, ArchiveUploadKind.Backup, options.MaxUploadBytes, temp,
                    HttpContext.RequestAborted);
                if (error is not null)
                {
                    return Fail(error);
                }
            }

            string? kit = null;
            if (kitFile is { Length: > 0 and <= Admin.BackupsModel.MaxKitBytes })
            {
                using var reader = new StreamReader(kitFile.OpenReadStream());
                kit = await reader.ReadToEndAsync(HttpContext.RequestAborted);
            }

            var prepared = await restore.PrepareAsync(temp, kit is null ? null : new RecoveryKey(kit, passphrase ?? string.Empty),
                cancellationToken: HttpContext.RequestAborted);
            if (prepared.Error is not null)
            {
                return Fail("Restore refused: " + prepared.Error);
            }

            using var p = prepared.Prepared!;
            using var held = await gate.TryEnterAsync(TimeSpan.FromSeconds(5), HttpContext.RequestAborted);
            string? applied;
            using (maintenance.Enter("restore"))
            {
                applied = await restore.ApplyAsync(p, new RestoreOptions(PreRestoreBackup: false, Origin: "setup"), HttpContext.RequestAborted);
            }

            if (applied is not null)
            {
                return Fail(applied);
            }

            setup.Complete("restore"); // BR-66: sign-in now uses the accounts from the backup
            return Redirect("/account/login?setup=restored");
        }
        finally
        {
            BackupService.TryDelete(temp);
        }
    }

    private PageResult Fail(string error)
    {
        Error = error;
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return Page();
    }
}
