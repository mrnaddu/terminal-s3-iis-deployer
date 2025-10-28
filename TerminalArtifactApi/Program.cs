using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/artifacts/{terminalId}/{zipName}", (string terminalId, string zipName) =>
{
    if (!IsValid(terminalId) || !IsValid(zipName.Replace(".zip", "")))
        return Results.BadRequest("Invalid path segments.");

    var root = app.Configuration["Artifacts:Root"];
    if (string.IsNullOrWhiteSpace(root))
        return Results.Problem("Artifacts:Root not configured.", statusCode: 500);

    var terminalDir = Path.Combine(root, terminalId);
    if (!Directory.Exists(terminalDir))
        return Results.NotFound($"Terminal '{terminalId}' not found.");

    var fileName = zipName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? zipName : $"{zipName}.zip";
    var zipPath = Path.Combine(terminalDir, fileName);

    if (!File.Exists(zipPath))
        return Results.NotFound($"Zip '{fileName}' not found.");

    try
    {
        var stream = File.Open(zipPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Results.File(stream, "application/zip", fileName, enableRangeProcessing: true);
    }
    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
    {
        return Results.Problem(ex.Message, statusCode: ex is UnauthorizedAccessException ? 403 : 500);
    }
})
.WithName("GetArtifactZip")
.WithOpenApi();

app.Run();

static bool IsValid(string value) =>
    !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value, "^[A-Za-z0-9._-]+$");
