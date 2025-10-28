using System.IO.Compression;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

static bool IsValidSegment(string value)
    => !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value, "^[A-Za-z0-9._-]+$");

// GET /artifacts/{terminalId}/{tag}
// tag is the zip file name (without .zip) under Artifacts:Root
// terminalId is the folder inside that zip; API returns a zip containing ONLY that terminal folder's contents.
app.MapGet("/artifacts/{terminalId}/{tag}", async (HttpContext http, string terminalId, string tag) =>
{
    try
    {
        if (!IsValidSegment(terminalId) || !IsValidSegment(tag))
        {
            return Results.BadRequest("Invalid terminalId or tag. Allowed: letters, digits, '.', '-', '_'.");
        }

        var artifactsRoot = app.Configuration["Artifacts:Root"];
        if (string.IsNullOrWhiteSpace(artifactsRoot))
        {
            return Results.Problem("Artifacts root is not configured. Set Artifacts:Root in appsettings.", statusCode: 500);
        }

        Directory.CreateDirectory(artifactsRoot);
        var sourceZip = Path.Combine(artifactsRoot, $"{tag}.zip");
        if (!File.Exists(sourceZip))
        {
            return Results.NotFound($"Source zip not found: {sourceZip}");
        }

        // Build a new zip containing only the entries under the terminalId folder within the source zip
        var tempZip = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.zip");
        var prefix = terminalId.TrimEnd('/') + "/";

        using (var src = ZipFile.OpenRead(sourceZip))
        using (var outStream = File.Create(tempZip))
        using (var dest = new ZipArchive(outStream, ZipArchiveMode.Create, leaveOpen: false))
        {
            var matched = false;
            foreach (var entry in src.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (name.EndsWith("/")) { matched = true; continue; } // directory placeholder
                var relPath = name.Substring(prefix.Length);
                if (string.IsNullOrEmpty(relPath) || relPath.EndsWith("/")) { matched = true; continue; }

                var newEntry = dest.CreateEntry(relPath, CompressionLevel.Optimal);
                await using var srcStream = entry.Open();
                await using var newStream = newEntry.Open();
                await srcStream.CopyToAsync(newStream);
                matched = true;
            }

            if (!matched)
            {
                // No entries found for terminal folder
                return Results.NotFound($"Folder '{terminalId}' not found inside zip '{tag}.zip'.");
            }
        }

        http.Response.RegisterForDispose(new TempFileDeleter(tempZip));
        var tempStream = File.Open(tempZip, FileMode.Open, FileAccess.Read, FileShare.Read);
        var tempFileName = $"{terminalId}-{tag}.zip";
        return Results.File(tempStream, "application/zip", tempFileName, enableRangeProcessing: true);
    }
    catch (UnauthorizedAccessException ex)
    {
        return Results.Problem($"Access denied: {ex.Message}", statusCode: 403);
    }
    catch (FileNotFoundException ex)
    {
        return Results.Problem($"File not found: {ex.Message}", statusCode: 404);
    }
    catch (IOException ex)
    {
        return Results.Problem($"I/O error: {ex.Message}", statusCode: 500);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Unexpected error: {ex.Message}", statusCode: 500);
    }
})
.WithName("GetArtifactZip")
.WithOpenApi();

app.Run();

sealed class TempFileDeleter : IDisposable
{
    private readonly string _path;
    public TempFileDeleter(string path) => _path = path;
    public void Dispose()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch { /* ignore */ }
    }
}
