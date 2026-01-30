using AKML.SQL.Core.DataStructures;
using AKML.SQL.Core.Services;
using AKML.SQL.Shared;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace AKML.SQL.Core;

/// <summary>
/// AKML-SQL Core Service Entry Point
/// This is the out-of-process gRPC server that handles SQL parsing,
/// IntelliSense, and metadata operations.
/// </summary>
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Ensure app data directories exist
        EnsureDirectoriesExist();

        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", AkmlConstants.ProductName)
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: Path.Combine(AkmlConstants.Paths.LogFolder, "core-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            Log.Information("Starting {ProductName} Core Service v{Version}",
                AkmlConstants.ProductName, AkmlConstants.ProductVersion);

            var builder = WebApplication.CreateBuilder(args);

            // Add Serilog
            builder.Host.UseSerilog();

            // Configure Kestrel for Named Pipes
            builder.WebHost.ConfigureKestrel(options =>
            {
                // Named Pipe endpoint (primary)
                options.ListenNamedPipe(AkmlConstants.PipeName, listenOptions =>
                {
                    listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
                });

                // TCP fallback endpoint (for debugging)
                if (builder.Environment.IsDevelopment())
                {
                    options.ListenLocalhost(AkmlConstants.DefaultGrpcPort, listenOptions =>
                    {
                        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
                    });
                }
            });

            // Add services
            ConfigureServices(builder.Services);

            var app = builder.Build();

            // Configure gRPC
            app.MapGrpcService<BridgeServiceImpl>();
            
            // Health check endpoint
            app.MapGet("/health", () => Results.Ok(new
            {
                Status = "Healthy",
                Version = AkmlConstants.ProductVersion,
                Timestamp = DateTime.UtcNow
            }));

            Log.Information("Core Service listening on named pipe: {PipeName}", AkmlConstants.PipeName);
            
            if (builder.Environment.IsDevelopment())
            {
                Log.Information("Development TCP endpoint: localhost:{Port}", AkmlConstants.DefaultGrpcPort);
            }

            await app.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Core Service terminated unexpectedly");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Add gRPC
        services.AddGrpc(options =>
        {
            options.MaxReceiveMessageSize = 16 * 1024 * 1024; // 16 MB
            options.MaxSendMessageSize = 16 * 1024 * 1024;
            options.EnableDetailedErrors = true;
        });

        // Add Core services
        services.AddSingleton<IMetadataService, MetadataService>();
        services.AddSingleton<IDocumentManager, DocumentManager>();

        // Add Sprint 5 services
        services.AddSingleton<SqlContextAnalyzer>();
        services.AddSingleton<MetadataCache>();
        services.AddSingleton<ICompletionService, CompletionService>();

        // Add Sprint 6 services - Formatting & Styles
        services.AddSingleton<IFormatStyleService, FormatStyleService>();
        services.AddSingleton<ISqlParserService>(sp =>
            new SqlParserService(
                sp.GetRequiredService<ILogger<SqlParserService>>(),
                sp.GetRequiredService<IFormatStyleService>()));

        // Add Sprint 7 services - Refactoring Tools
        services.AddSingleton<IRefactoringService, RefactoringService>();

        // Add Sprint 8 services - History & Snippets
        services.AddSingleton<IQueryHistoryService, QueryHistoryService>();
        services.AddSingleton<ISnippetService, SnippetService>();

        // Add Sprint 9 services - Tab Management & Code Analysis
        services.AddSingleton<ITabColoringService, TabColoringService>();
        services.AddSingleton<ICodeAnalysisService, CodeAnalysisService>();

        // Add Sprint 10 services - AI Integration
        services.AddSingleton<IAiService, AiService>();

        // Add Sprint 11 services - Performance & Multi-Version
        services.AddSingleton<IPerformanceService, PerformanceService>();
        services.AddSingleton<ISqlVersionService, SqlVersionService>();

        // Add Sprint 12 services - Licensing & Auto-Update
        services.AddSingleton<ILicenseService, LicenseService>();
        services.AddSingleton<IUpdateService, UpdateService>();

        // Add caching
        services.AddMemoryCache();

        // Add logging
        services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders();
            loggingBuilder.AddSerilog(dispose: true);
        });
    }

    private static void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(AkmlConstants.Paths.AppDataFolder);
        Directory.CreateDirectory(AkmlConstants.Paths.LogFolder);
        Directory.CreateDirectory(AkmlConstants.Paths.CacheFolder);
        Directory.CreateDirectory(AkmlConstants.Paths.SettingsFolder);
    }
}
