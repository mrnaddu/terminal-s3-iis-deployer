using System.Diagnostics;
using System.IO.Compression;
using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var iisSiteName = config["Deployment:IisSiteName"] ?? "Default Web Site";
var iisPhysicalPath = config["Deployment:IisPhysicalPath"] ?? @"C:\\inetpub\\wwwroot";
var localZipPath = config["Deployment:LocalZipPath"];
var localBackupDir = config["Deployment:LocalBackupDir"] ?? Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "backups");
Directory.CreateDirectory(localBackupDir);

Console.WriteLine("=== IIS Zip Deployer ===");

// Resolve package zip path
string? packageZipPath = null;
if (!string.IsNullOrWhiteSpace(localZipPath) && File.Exists(localZipPath))
{
    packageZipPath = localZipPath;
    Console.WriteLine($"Using local zip: {packageZipPath}");
}
else if (File.Exists(Path.Combine("artifacts", "packages", "site.zip")))
{
    packageZipPath = Path.Combine("artifacts", "packages", "site.zip");
    Console.WriteLine($"Using default local zip: {packageZipPath}");
}
else
{
    Console.Write("Enter path to package .zip: ");
    var input = Console.ReadLine()?.Trim('"', ' ');
    if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
    {
        Console.WriteLine("ERROR: Package zip not found.");
        return;
    }
    packageZipPath = input;
}

// Backup current IIS site content locally
var backupZip = Path.Combine(localBackupDir, $"backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip");
if (!Directory.Exists(iisPhysicalPath))
{
    Directory.CreateDirectory(iisPhysicalPath);
    Console.WriteLine($"IIS physical path did not exist; created '{iisPhysicalPath}'. Skipping backup.");
}
else
{
    Console.WriteLine($"Zipping current site folder '{iisPhysicalPath}' to {backupZip} ...");
    ZipDirectory(iisPhysicalPath, backupZip);
    Console.WriteLine($"Backup retained locally at '{backupZip}'.");
}

// Stop IIS site (Windows only)
if (OperatingSystem.IsWindows())
{
    Console.WriteLine($"Stopping IIS site '{iisSiteName}' ...");
    IisHelper.StopSite(iisSiteName);
}
else
{
    Console.WriteLine("Non-Windows OS detected; skipping IIS stop/start.");
}

// Deploy new package
if (string.IsNullOrWhiteSpace(packageZipPath) || !File.Exists(packageZipPath))
{
    Console.WriteLine("ERROR: Package zip not available. Aborting.");
    return;
}
Console.WriteLine($"Deploying zip to '{iisPhysicalPath}' ...");
DeployZip(packageZipPath!, iisPhysicalPath);

// Start IIS site (Windows only)
if (OperatingSystem.IsWindows())
{
    Console.WriteLine($"Starting IIS site '{iisSiteName}' ...");
    IisHelper.StartSite(iisSiteName);
}

Console.WriteLine("Deployment complete.");


static void ZipDirectory(string sourceDir, string zipPath)
{
    if (!Directory.Exists(sourceDir))
        throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");

    if (File.Exists(zipPath)) File.Delete(zipPath);
    ZipFile.CreateFromDirectory(sourceDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
}

static void DeployZip(string zipPath, string destDir)
{
    if (!File.Exists(zipPath))
        throw new FileNotFoundException("Zip to deploy not found", zipPath);

    Directory.CreateDirectory(destDir);

    // Clear destination directory (except root)
    foreach (var dir in Directory.GetDirectories(destDir))
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
    }
    foreach (var file in Directory.GetFiles(destDir))
    {
        try { File.Delete(file); } catch { /* ignore */ }
    }

    ZipFile.ExtractToDirectory(zipPath, destDir);
}

static class IisHelper
{
    private const string AppCmd = @"C:\\Windows\\System32\\inetsrv\\appcmd.exe";

    public static void StopSite(string siteName)
    {
        RunAppCmd($"stop site /site.name:\"{siteName}\"");
    }

    public static void StartSite(string siteName)
    {
        RunAppCmd($"start site /site.name:\"{siteName}\"");
    }

    private static void RunAppCmd(string args)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!File.Exists(AppCmd))
        {
            Console.WriteLine($"WARN: appcmd not found at {AppCmd}; skipping IIS command.");
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = AppCmd,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(AppCmd)!
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        var outText = p.StandardOutput.ReadToEnd();
        var errText = p.StandardError.ReadToEnd();
        if (!string.IsNullOrWhiteSpace(outText)) Console.WriteLine(outText.Trim());
        if (!string.IsNullOrWhiteSpace(errText)) Console.WriteLine(errText.Trim());
    }
}
