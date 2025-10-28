# IIS Zip Deployer

A C# .NET console app to deploy a local .zip package into an IIS site with a local backup of the previous site contents.

## Requirements
- .NET 8+ SDK
- Windows with IIS (for stop/start site); `appcmd.exe` must be available at `C:\Windows\System32\inetsrv\appcmd.exe`

## Configuration
Configure via `appsettings.json` (or environment variables):

- `Deployment:LocalZipPath` (optional) — path to the package zip to deploy. If not set, the app will use `artifacts\packages\site.zip` if present, otherwise it will prompt for a path.
- `Deployment:LocalBackupDir` (default `artifacts\backups`) — where backups of the current site are stored.
- `Deployment:IisSiteName` (e.g., `Default Web Site`)
- `Deployment:IisPhysicalPath` (e.g., `C:\inetpub\wwwroot`)

## Usage
```
dotnet build
# optional: create a sample package
./package-sample.ps1
# run (uses Deployment:LocalZipPath or artifacts\packages\site.zip)
dotnet run --project .\terminal-s3-iis-deployer.csproj
```
The app will:
1. Zip the current IIS site folder to `LocalBackupDir/backup_*.zip` (creating the site folder if missing)
2. Stop the IIS site (Windows), extract the new zip to the site folder, then start the site

## Notes
- Running IIS commands requires elevated privileges.
- Ensure the IIS physical path is correct and the app has file system permissions.
