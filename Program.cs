using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;

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

Log.Title("IIS Zip Deployer");

// Resolve package zip path
Log.Step("Resolve package zip");
string? packageZipPath = null;

// 1) Try Artifact API if configured
var apiBase = config["ArtifactApi:BaseUrl"];
var apiTerminalId = config["ArtifactApi:TerminalId"];
var apiTag = config["ArtifactApi:Tag"];
if (!string.IsNullOrWhiteSpace(apiBase) && !string.IsNullOrWhiteSpace(apiTerminalId) && !string.IsNullOrWhiteSpace(apiTag))
{
    try
    {
        var apiUrl = $"{apiBase.TrimEnd('/')}/artifacts/{apiTerminalId}/{apiTag}";
        Log.Info($"Downloading package from API: {apiUrl}");
        using var http = new HttpClient();
        using var resp = await http.GetAsync(apiUrl, HttpCompletionOption.ResponseHeadersRead);
        if (!resp.IsSuccessStatusCode)
        {
            Log.Warn($"API returned {(int)resp.StatusCode} {resp.ReasonPhrase}. Falling back to local resolution.");
        }
        else
        {
            var tempZip = Path.Combine(Path.GetTempPath(), $"deployer_{Guid.NewGuid():N}.zip");
            await using (var fs = File.Create(tempZip))
            {
                await resp.Content.CopyToAsync(fs);
            }
            packageZipPath = tempZip;
            Log.Ok($"Downloaded package to: {tempZip}");
        }
    }
    catch (Exception ex)
    {
        Log.Warn($"Failed to download from API: {ex.Message}");
    }
}

// 2) If not from API, use configured or default local zips
if (packageZipPath is null)
{
    if (!string.IsNullOrWhiteSpace(localZipPath) && File.Exists(localZipPath))
    {
        packageZipPath = localZipPath;
        Log.Info($"Using local zip: {packageZipPath}");
    }
    else if (File.Exists(Path.Combine("artifacts", "packages", "site.zip")))
    {
        packageZipPath = Path.Combine("artifacts", "packages", "site.zip");
        Log.Info($"Using default local zip: {packageZipPath}");
    }
    else
    {
        Log.Step("No configured package found");
        Console.Write("Enter path to package .zip: ");
        var input = Console.ReadLine()?.Trim('"', ' ');
        if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
        {
            Log.Error("Package zip not found.");
            return;
        }
        packageZipPath = input;
    }
}

// Backup current IIS site content locally
Log.Step("Backup current site");
var backupZip = Path.Combine(localBackupDir, $"backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip");
if (!Directory.Exists(iisPhysicalPath))
{
    Directory.CreateDirectory(iisPhysicalPath);
    Log.Warn($"IIS physical path did not exist; created '{iisPhysicalPath}'. Skipping backup.");
}
else
{
    Log.Info($"Zipping '{iisPhysicalPath}' to {backupZip} ...");
    ZipDirectory(iisPhysicalPath, backupZip);
    Log.Ok($"Backup saved: {backupZip}");
}

// Stop IIS site (Windows only)
if (OperatingSystem.IsWindows())
{
    Log.Step($"Stop IIS site: {iisSiteName}");
    IisHelper.StopSite(iisSiteName);
}
else
{
    Log.Warn("Non-Windows OS detected; skipping IIS stop/start.");
}

// Deploy new package
if (string.IsNullOrWhiteSpace(packageZipPath) || !File.Exists(packageZipPath))
{
    Log.Error("Package zip not available. Aborting.");
    return;
}
Log.Step($"Deploy package to: {iisPhysicalPath}");
DeployZip(packageZipPath!, iisPhysicalPath);
Log.Ok("Files extracted");

// Start IIS site (Windows only)
if (OperatingSystem.IsWindows())
{
    Log.Step($"Start IIS site: {iisSiteName}");
    IisHelper.StartSite(iisSiteName);
}

Log.Ok("Deployment complete");


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
        try { Directory.Delete(dir, recursive: true); } catch (Exception ex) { Log.Warn($"Could not delete dir '{dir}': {ex.Message}"); }
    }
    foreach (var file in Directory.GetFiles(destDir))
    {
        try { File.Delete(file); } catch (Exception ex) { Log.Warn($"Could not delete file '{file}': {ex.Message}"); }
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
            Log.Warn($"appcmd not found at {AppCmd}; skipping IIS command.");
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
        if (!string.IsNullOrWhiteSpace(outText)) Log.Info(outText.Trim());
        if (!string.IsNullOrWhiteSpace(errText)) Log.Error(errText.Trim());
        if (p.ExitCode != 0) Log.Warn($"appcmd exited with code {p.ExitCode}");
    }
}

static class Log
{
    private static readonly object _lock = new();

    private static void Write(ConsoleColor color, string level, string message)
    {
        lock (_lock)
        {
            var prevColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"[{DateTime.Now:HH:mm:ss}] ");
            Console.ForegroundColor = color;
            Console.Write($"{level,-5} ");
            Console.ForegroundColor = prevColor;
            Console.WriteLine(message);
        }
    }

    public static void Title(string text)
    {
        lock (_lock)
        {
            var prev = Console.ForegroundColor;
            var line = new string('─', text.Length + 4);
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"┌{line}┐");
            Console.WriteLine($"│  {text}  │");
            Console.WriteLine($"└{line}┘");
            Console.ForegroundColor = prev;
        }
    }

    public static void Step(string message) => Write(ConsoleColor.Cyan, "STEP", message);
    public static void Info(string message) => Write(ConsoleColor.Gray, "INFO", message);
    public static void Ok(string message) => Write(ConsoleColor.Green, "OK", message);
    public static void Warn(string message) => Write(ConsoleColor.Yellow, "WARN", message);
    public static void Error(string message) => Write(ConsoleColor.Red, "ERR", message);
}
