# WARP.md

This file provides guidance to WARP (warp.dev) when working with code in this repository.

Project: .NET console app that deploys a local .zip into an IIS site, backing up the current site locally first.

Commands
- Restore/build/run

```pwsh path=null start=null
# restore deps
dotnet restore

# build (Debug)
dotnet build

# run the app
dotnet run --project terminal-s3-iis-deployer.csproj

# build Release artifacts (e.g., for distribution)
dotnet publish -c Release
```

- Lint/format

```pwsh path=null start=null
# format C# code per analyzer/style settings
dotnet format
```

- Tests

```pwsh path=null start=null
# run all tests (when test projects exist)
dotnet test

# run a single test by name
dotnet test --filter "FullyQualifiedName~Namespace.ClassName.MethodName"

# run tests with a trait/category
dotnet test --filter "TestCategory=Smoke"
```

Configuration and runtime
- Configuration sources: `appsettings.json`, `appsettings.{DOTNET_ENVIRONMENT}.json`, then environment variables (colon-delimited keys). Example overrides:

```pwsh path=null start=null
# examples (PowerShell)
$env:Deployment__LocalZipPath = ".\artifacts\packages\site.zip"
$env:Deployment__LocalBackupDir = ".\artifacts\backups"
$env:Deployment__IisSiteName = "Default Web Site"
$env:Deployment__IisPhysicalPath = "C:\inetpub\wwwroot"

# choose config environment file if present (appsettings.Development.json)
$env:DOTNET_ENVIRONMENT = "Development"
```

- On non-Windows OS, IIS stop/start is skipped; deployment still extracts into the configured path.

High-level architecture
- Entry point: `Program.cs` orchestrates: resolve package zip -> local backup -> optional IIS stop -> extract -> optional IIS start.
- Config keys used: `Deployment:LocalZipPath`, `Deployment:LocalBackupDir`, `Deployment:IisSiteName`, `Deployment:IisPhysicalPath`.
- IIS control (Windows only): `IisHelper` wraps `C:\Windows\System32\inetsrv\appcmd.exe` to stop/start the site.
- Deployment: Clears destination directory contents, then `ZipFile.ExtractToDirectory` to `Deployment:IisPhysicalPath`.

Operational notes (from README)
- Requirements: .NET 8+ SDK, Windows with IIS (for stop/start), `appcmd.exe` at `C:\Windows\System32\inetsrv\appcmd.exe`.
- Elevated privileges are typically required to control IIS and write into its physical path.

Repository pointers
- Single executable project; no test projects are currently present. Add standard `*.Tests.csproj` to enable `dotnet test` locally/CI.
