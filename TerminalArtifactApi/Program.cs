using Microsoft.OpenApi.Models;
using System.Text.RegularExpressions;

namespace TerminalArtifactApi;

public class Program
{
    static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Terminal Artifact API",
                Description = "API for downloading terminal deployment artifacts",
                Version = "v1"
            });
        });
        var app = builder.Build();

        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Terminal Artifact API v1");
            c.RoutePrefix = string.Empty;
        });

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
        .WithSummary("Download terminal artifact")
        .WithDescription("Downloads a zip file artifact for the specified terminal")
        .WithOpenApi(operation =>
        {
            operation.Parameters[0].Description = "Terminal identifier";
            operation.Parameters[1].Description = "Zip file name (with or without .zip extension)";
            return operation;
        });

        app.Run();
    }

    static bool IsValid(string value) =>
        !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value, "^[A-Za-z0-9._-]+$");
}