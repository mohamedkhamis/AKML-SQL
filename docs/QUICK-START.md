# AKML-SQL Quick Start Guide

## For .NET Developers

This guide gets you up and running with AKML-SQL development in 10 minutes.

---

## Step 1: System Requirements Check

Open PowerShell and verify your setup:

```powershell
# Check .NET SDK
dotnet --list-sdks
# Should show 8.0.x or higher

# Check Visual Studio
# Open Visual Studio Installer and verify these workloads:
# - Visual Studio extension development
# - .NET desktop development
```

---

## Step 2: Clone/Extract and Open

```powershell
# Navigate to solution directory
cd C:\Projects\AKML-SQL

# Open in Visual Studio
start AKML-SQL.sln
```

---

## Step 3: First Build

In Visual Studio:

1. **Solution Explorer** → Right-click solution → **Restore NuGet Packages**
2. Set **Configuration** to `Debug`
3. Press `Ctrl+Shift+B` to build

Expected output:
```
Build: 5 succeeded, 0 failed, 0 skipped
```

---

## Step 4: Run Core Service

**Option A: Visual Studio**
1. Right-click `AKML.SQL.Core` → **Set as Startup Project**
2. Press `F5` to run with debugger

**Option B: Command Line**
```powershell
cd src\AKML.SQL.Core
dotnet run
```

You should see:
```
[15:30:45 INF] Starting AKML-SQL Core Service v1.0.0
[15:30:45 INF] Core Service listening on named pipe: akml-sql-bridge
[15:30:45 INF] Development TCP endpoint: localhost:50051
```

---

## Step 5: Run Unit Tests

```powershell
dotnet test
```

Or in Visual Studio: **Test** → **Run All Tests** (`Ctrl+R, A`)

---

## Step 6: Test the gRPC Service

With Core running, you can test using a simple client:

```csharp
// Quick test in LINQPad or a console app
using Grpc.Net.Client;
using AKML.SQL.Shared.Contracts;

var channel = GrpcChannel.ForAddress("http://localhost:50051");
var client = new BridgeService.BridgeServiceClient(channel);

var response = await client.PingAsync(new PingRequest 
{ 
    ClientVersion = "1.0.0",
    SsmsVersion = "20.0"
});

Console.WriteLine($"Server: {response.ServerVersion}");
Console.WriteLine($"Message: {response.Message}");
```

---

## Project Structure Quick Reference

| Project | Framework | Purpose |
|---------|-----------|---------|
| `AKML.SQL.Core` | .NET 8 | Background service, gRPC server |
| `AKML.SQL.SSMS` | .NET Framework 4.7.2 | VSIX extension for SSMS |
| `AKML.SQL.Shared` | .NET Standard 2.0 | Shared contracts, models |

---

## Key Files to Know

| File | Purpose |
|------|---------|
| `Program.cs` | Core service entry point, Kestrel config |
| `BridgeServiceImpl.cs` | gRPC service implementation |
| `SqlParserService.cs` | ScriptDom parsing engine |
| `CompletionService.cs` | IntelliSense logic |
| `bridge.proto` | gRPC contract definitions |
| `AkmlSqlPackage.cs` | VSIX package entry point |
| `GrpcClientService.cs` | VSIX gRPC client |

---

## Common Development Tasks

### Add a new gRPC method

1. Edit `src/AKML.SQL.Core/Protos/bridge.proto`
2. Rebuild Core project (generates C# code)
3. Implement in `BridgeServiceImpl.cs`

### Add a SQL keyword

Edit `CompletionService.cs`:
```csharp
private static readonly string[] SqlKeywords = new[]
{
    // Add your keyword here
};
```

### Debug VSIX in SSMS

1. Set VSIX project as startup
2. In Debug properties, set **Start external program** to SSMS path
3. F5 starts SSMS with extension attached

---

## Troubleshooting

**Build fails with "SDK not found"**
→ Install .NET 8 SDK from https://dot.net

**gRPC connection refused**
→ Ensure Core service is running on port 50051

**VSIX won't load**
→ Check VS extension development workload is installed

---

## Next Steps

1. Read the full [README.md](../README.md)
2. Review the [Sprint Planning](SPRINT-PLAN.md)
3. Explore the code in Visual Studio
4. Run and modify the unit tests
5. Start implementing Sprint 2 features!

---

Happy coding! 🚀
