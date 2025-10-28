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
// Returns a zip file either by picking up an existing zip or zipping the tag folder on-the-fly.
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

        // Ensure base and terminal folders exist
        Directory.CreateDirectory(artifactsRoot);
        var terminalDir = Path.Combine(artifactsRoot, terminalId);
        if (!Directory.Exists(terminalDir))
        {
            Directory.CreateDirectory(terminalDir);
            return Results.NotFound($"Terminal folder created at '{terminalDir}'. Upload artifacts and retry.");
        }

        var existingZip = Path.Combine(terminalDir, $"{tag}.zip");
        if (File.Exists(existingZip))
        {
            var stream = File.Open(existingZip, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var fileName = $"{terminalId}-{tag}.zip";
            return Results.File(stream, "application/zip", fileName, enableRangeProcessing: true);
        }

        var tagFolder = Path.Combine(terminalDir, tag);
        if (!Directory.Exists(tagFolder))
        {
            Directory.CreateDirectory(tagFolder);
            return Results.NotFound($"Tag folder created at '{tagFolder}'. Place files there or provide '{tag}.zip' and retry.");
        }

        // Create a temp zip and stream it back; ensure cleanup after response completes.
        var tempZip = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.zip");
        ZipFile.CreateFromDirectory(tagFolder, tempZip, CompressionLevel.Optimal, includeBaseDirectory: false);

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
