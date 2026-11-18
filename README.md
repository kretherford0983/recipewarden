# RecipeWarden

Self-hosted, single-household recipe manager (.NET 10, Razor Pages + htmx, SQLite), run as a
systemd service behind a Cloudflare Tunnel. The specification is `docs/spec.md`; the working
agreement for contributors (human or AI) is `CLAUDE.md`.

```bash
dotnet build RecipeWarden.slnx          # warnings are errors
build/test.sh                           # all tests + coverage gates
cd src/RecipeWarden.Web && ASPNETCORE_ENVIRONMENT=Development dotnet run   # http://127.0.0.1:5080, data in ./.data
dotnet run --project src/RecipeWarden.Cli -- --config src/RecipeWarden.Web/appsettings.Development.json admin create --email you@example.com
```

Status: milestone M0 (Foundations). See `docs/traceability.md`.
