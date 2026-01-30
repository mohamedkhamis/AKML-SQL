# AKML-SQL

**Advanced IntelliSense Extension for SQL Server Management Studio**

AKML-SQL is a professional IntelliSense extension for SSMS (similar to Redgate SQL Prompt) that provides smart code completion, SQL formatting, refactoring tools, and AI-powered assistance.

---

## 🏗️ Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                        SSMS Process                              │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │              AKML.SQL.SSMS (VSIX)                        │   │
│  │         .NET Framework 4.7.2 for SSMS 18/19             │   │
│  │         .NET 8 for SSMS 20+                              │   │
│  └──────────────────────┬──────────────────────────────────┘   │
└─────────────────────────┼───────────────────────────────────────┘
                          │ gRPC over Named Pipes
                          ▼
┌─────────────────────────────────────────────────────────────────┐
│                    Core Service Process                          │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │              AKML.SQL.Core (.NET 8)                      │   │
│  │  • SQL Parsing (ScriptDom)                               │   │
│  │  • IntelliSense Engine                                   │   │
│  │  • Metadata Caching                                      │   │
│  │  • Formatting Engine                                     │   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

---

## 📋 Prerequisites

Before you begin, ensure you have:

| Requirement | Version | Download |
|------------|---------|----------|
| Visual Studio 2022 | 17.8+ | [Download](https://visualstudio.microsoft.com/downloads/) |
| .NET SDK | 8.0+ | [Download](https://dotnet.microsoft.com/download/dotnet/8.0) |
| .NET Framework | 4.7.2 Developer Pack | [Download](https://dotnet.microsoft.com/download/dotnet-framework/net472) |
| SSMS | 18, 19, or 20+ | [Download](https://docs.microsoft.com/sql/ssms/download-sql-server-management-studio-ssms) |

### Visual Studio Workloads Required

Install these workloads via Visual Studio Installer:

1. **Visual Studio extension development** - Required for VSIX projects
2. **.NET desktop development** - Required for WPF and Framework support
3. **ASP.NET and web development** - Required for gRPC/Kestrel

---

## 🚀 Quick Start (Step-by-Step)

### Step 1: Extract the Solution

```powershell
# Extract the ZIP to your preferred location
Expand-Archive -Path "AKML-SQL.zip" -DestinationPath "C:\Projects\AKML-SQL"
cd C:\Projects\AKML-SQL
```

### Step 2: Restore NuGet Packages

```powershell
# Using dotnet CLI
dotnet restore AKML-SQL.sln

# Or open in Visual Studio and let it restore automatically
```

### Step 3: Build the Solution

**Option A: Command Line**
```powershell
# Build all projects
dotnet build AKML-SQL.sln -c Debug

# Or use the provided script
.\tools\build.ps1 -Configuration Debug -Restore
```

**Option B: Visual Studio**
1. Open `AKML-SQL.sln` in Visual Studio 2022
2. Set build configuration to `Debug`
3. Press `Ctrl+Shift+B` to build

### Step 4: Run Tests

```powershell
# Run all tests
dotnet test AKML-SQL.sln

# Or use the provided script
.\tools\test.ps1
```

### Step 5: Run the Core Service (for Development)

```powershell
# Navigate to Core project
cd src\AKML.SQL.Core

# Run the service
dotnet run

# You should see:
# [INFO] Starting AKML-SQL Core Service v1.0.0
# [INFO] Core Service listening on named pipe: akml-sql-bridge
```

### Step 6: Test gRPC Connection

With the Core service running, you can test the connection:

```powershell
# In a new terminal, use grpcurl or create a test client
# The service listens on localhost:50051 in development mode
```

---

## 📁 Project Structure

```
AKML-SQL/
├── AKML-SQL.sln                    # Solution file
├── Directory.Build.props           # Shared build properties
├── README.md                       # This file
│
├── src/
│   ├── AKML.SQL.Core/              # .NET 8 Background Service
│   │   ├── Program.cs              # Entry point with Kestrel config
│   │   ├── Services/
│   │   │   ├── BridgeServiceImpl.cs    # gRPC service implementation
│   │   │   ├── SqlParserService.cs     # ScriptDom parsing
│   │   │   ├── CompletionService.cs    # IntelliSense engine
│   │   │   ├── MetadataService.cs      # Schema caching
│   │   │   └── DocumentManager.cs      # Document state
│   │   ├── Protos/
│   │   │   └── bridge.proto        # gRPC contract definitions
│   │   └── appsettings.json
│   │
│   ├── AKML.SQL.SSMS/              # VSIX Extension
│   │   ├── AkmlSqlPackage.cs       # VS Package entry point
│   │   ├── source.extension.vsixmanifest
│   │   ├── Services/
│   │   │   ├── CoreProcessManager.cs   # Core process lifecycle
│   │   │   └── GrpcClientService.cs    # gRPC client (Grpc.Core)
│   │   └── Commands/
│   │       └── Commands.cs         # Menu commands
│   │
│   └── AKML.SQL.Shared/            # Shared Library (netstandard2.0)
│       ├── AkmlConstants.cs        # Shared constants
│       ├── Models/
│       │   └── Models.cs           # Shared models
│       └── Extensions/
│           └── Extensions.cs       # String extensions, fuzzy match
│
├── tests/
│   ├── AKML.SQL.Core.Tests/
│   └── AKML.SQL.Shared.Tests/
│
├── docs/                           # Documentation
└── tools/
    ├── build.ps1                   # Build script
    ├── test.ps1                    # Test runner
    └── publish.ps1                 # Package creator
```

---

## 🔧 Configuration

### Core Service Configuration

Edit `src/AKML.SQL.Core/appsettings.json`:

```json
{
  "AkmlSql": {
    "MetadataCacheMinutes": 5,      // How long to cache schema data
    "MaxCompletionItems": 100,      // Max items in completion list
    "EnableTelemetry": false,       // Send anonymous usage data
    "LogLevel": "Information"       // Logging verbosity
  }
}
```

### Named Pipe Configuration

The service uses named pipes for IPC. The pipe name is defined in `AkmlConstants.cs`:

```csharp
public const string PipeName = "akml-sql-bridge";
```

---

## 🛠️ Development Guide

### Adding a New gRPC Method

1. **Define the method in `bridge.proto`**:
```protobuf
service BridgeService {
  rpc MyNewMethod(MyRequest) returns (MyResponse);
}

message MyRequest {
  string data = 1;
}

message MyResponse {
  bool success = 1;
}
```

2. **Rebuild to generate C# code**:
```powershell
dotnet build src\AKML.SQL.Core
```

3. **Implement in `BridgeServiceImpl.cs`**:
```csharp
public override Task<MyResponse> MyNewMethod(MyRequest request, ServerCallContext context)
{
    // Implementation
    return Task.FromResult(new MyResponse { Success = true });
}
```

4. **Call from VSIX client**:
```csharp
var response = await _client.MyNewMethodAsync(new MyRequest { Data = "test" });
```

### Adding New Completion Items

Edit `CompletionService.cs` to add new keywords or functions:

```csharp
private static readonly string[] SqlKeywords = new[]
{
    "SELECT", "FROM", "WHERE", // ... existing
    "MY_NEW_KEYWORD"           // Add here
};
```

### Testing with SSMS

1. Build the solution in Release mode
2. Copy `AKML.SQL.Core.exe` to a known location
3. Install the VSIX in SSMS
4. The extension will auto-start the Core service

---

## 🐛 Troubleshooting

### Common Issues

**Issue: "Core service executable not found"**
```
Solution: Ensure AKML.SQL.Core is built and the path is correct.
Check AkmlSqlPackage.cs -> GetCoreExecutablePath() for search locations.
```

**Issue: gRPC connection timeout**
```
Solution: 
1. Verify Core service is running (check Task Manager)
2. Check firewall isn't blocking localhost:50051
3. Look at Core service logs in %LOCALAPPDATA%\AKML-SQL\Logs
```

**Issue: "Package load failure" in SSMS**
```
Solution:
1. Ensure .NET Framework 4.7.2 is installed
2. Check SSMS version compatibility
3. Run SSMS as Administrator once
4. Clear VS component cache: %LOCALAPPDATA%\Microsoft\VisualStudio\*\ComponentModelCache
```

### Logging

Logs are stored in `%LOCALAPPDATA%\AKML-SQL\Logs\`:
- `core-{date}.log` - Core service logs
- `ssms-{date}.log` - VSIX extension logs

### Debug Mode

To run Core with extra logging:
```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src\AKML.SQL.Core
```

---

## 📊 Sprint Roadmap

| Sprint | Features | Status |
|--------|----------|--------|
| 1 | Solution Architecture & IPC Bridge | ✅ Included |
| 2 | Text Buffer Sync & Parsing | 🔲 Next |
| 3 | WPF Suggestion Window | 🔲 Planned |
| 4 | Schema Harvesting | 🔲 Planned |
| 5 | Context-Aware IntelliSense (MVP) | 🔲 Planned |
| 6 | SQL Formatter | 🔲 Planned |
| 7-13 | Advanced Features | 🔲 Planned |

---

## 📄 License

Copyright © 2024 Azka Innovation. All rights reserved.

---

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Run tests
5. Submit a pull request

---

## 📞 Support

- Documentation: `docs/` folder
- Issues: GitHub Issues
- Email: support@azka.com.eg
