# Agent Development Guide - OPC DA Agent

## Project Overview

This is a dual-component OPC DA data collection system:
- **Windows Agent (C#)**: OPC DA client with HTTP REST API server
- **Linux Collector (Go)**: Data collection, MQTT/HTTP publishing, Web UI

---

## Build Commands

### C# Windows Agent

```bash
# Using MSBuild directly
msbuild OPC_DA_Agent.csproj /p:Configuration=Release

# Using build script (Windows)
build.bat

# Output: bin\Release\OPC_DA_Agent.exe
```

**Run:** `cd bin\Release && OPC_DA_Agent.exe --config config.json`

### Go Linux Collector

```bash
# Native build (Linux)
go build -o collector collector_main.go ConfigManager.go collector_web.go KeyTransformer.go

# Cross-compile from Windows
build_collector.bat   # Outputs collector_linux
build_collector.sh    # Linux native build script

# With dependencies
go mod tidy
```

**Run:** `./collector --config collector.ini --web-port 9090`

### Note on Tests

This project currently has **no automated tests**. Running a single test is not applicable.

---

## C# Code Style (Windows Agent)

### Naming Conventions

- **Classes/Structs**: `PascalCase` - `Config`, `OPCService`, `HttpServer`
- **Methods**: `PascalCase` - `LoadFromFile()`, `Validate()`, `Start()`
- **Properties**: `PascalCase` - `OpcServerUrl`, `HttpPort`, `TagsFile`
- **Private fields**: `_camelCase` - `_logger`, `_config`, `_isRunning`
- **Constants/Readonly**: `PascalCase` - `LogFilePath`, `MaxConnections`
- **Interfaces**: `PascalCase` starting with `I` (rarely used in this codebase)
- **Enums**: `PascalCase` - `LogLevel`, `SystemStatus`

### Import Style

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
```

- Standard namespace imports first (System.*)
- Third-party libraries after
- No unused imports
- Sort alphabetically within groups

### JSON Property Mapping

Use `[JsonProperty]` attributes for camelCase JSON serialization:

```csharp
[JsonProperty("opc_server_url")]
public string OpcServerUrl { get; set; }

[JsonProperty("update_interval_ms")]
public int UpdateInterval { get; set; }
```

### Error Handling

```csharp
// Try-catch with logging
try {
    await _opcService.ConnectAsync();
} catch (Exception ex) {
    _logger.Error("Connection failed", ex);
    Console.WriteLine($"Error: {ex.Message}");
}

// Validation with errors collection
public bool Validate(out List<string> errors) {
    errors = new List<string>();
    if (string.IsNullOrWhiteSpace(OpcServerUrl)) {
        errors.Add("OPC服务器地址不能为空");
    }
    return errors.Count == 0;
}
```

### Async/Await Pattern

```csharp
public async Task StartAsync() {
    // Use async for I/O operations
    await _opcService.ConnectAsync();
    await _httpServer.StartAsync();
}

// Always use ConfigureAwait(false) in library code
await Task.Delay(1000).ConfigureAwait(false);
```

### IDisposable Pattern

```csharp
public class SomeResource : IDisposable {
    private readonly object _lock = new object();
    private bool _disposed = false;

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing) {
        if (_disposed) return;

        if (disposing) {
            // Dispose managed resources
            _writer?.Close();
            _writer?.Dispose();
        }

        _disposed = true;
    }
}
```

---

## Go Code Style (Linux Collector)

### Naming Conventions

- **Exported types/functions**: `PascalCase` - `ConfigManager`, `LoadIni()`
- **Private types/functions**: `camelCase` - `collectLoop()`, `parseLogLevel()`
- **Interfaces**: `PascalCase` (rarely used)
- **Constants**: `PascalCase` or `UPPER_SNAKE` - `MaxRetries`, `DEFAULT_TIMEOUT`
- **Package names**: `lowercase` - `main`, `config`, `opc`

### Import Style

```go
import (
    "encoding/json"
    "fmt"
    "log"
    "os"

    "gopkg.in/ini.v1"
)
```

- Standard library first (grouped alphabetically)
- Third-party packages after
- Blank line between groups
- No unused imports

### Error Handling

```go
// Always check errors
config, err := cm.Load(path)
if err != nil {
    log.Fatalf("无法加载配置文件: %s", *configPath)
}

// Wrap errors with context
if err := collector.Start(); err != nil {
    return fmt.Errorf("采集器启动失败: %v", err)
}

// Multiple return values
func Load(path string) (*AppConfig, error) {
    data, err := ioutil.ReadFile(path)
    if err != nil {
        return nil, err
    }
    // ...
}
```

### Struct Initialization

```go
// Field names are required for clarity
collector := &Collector{
    config:      config,
    transformer: NewKeyTransformer(),
}

// Omitted fields default to zero values
task := &TaskConfig{
    Enabled:           true,
    JobIntervalSecond: 1,
    // TagPrecision defaults to 0
}
```

### Interface Usage

```go
// Define behavior, not implementation
type DataPublisher interface {
    Publish(data map[string]interface{}) error
    IsConnected() bool
}

// Implement interface implicitly
func (c *MqttClient) Publish(data map[string]interface{}) error {
    // Implementation
}
```

### Concurrency

```go
// Use goroutines for non-blocking operations
go func() {
    webServer := NewWebServer(*configPath)
    if err := webServer.Start(*webPort); err != nil {
        log.Printf("Web服务器启动失败: %v", err)
    }
}()

// Use channels for signaling
sigChan := make(chan os.Signal, 1)
signal.Notify(sigChan, syscall.SIGINT, syscall.SIGTERM)
<-sigChan
```

---

## Configuration Patterns

### C# (JSON)

```json
{
  "opc_server_url": "opcda://localhost/OPCServer.WinCC",
  "http_port": 8080,
  "update_interval_ms": 1000,
  "batch_size": 500,
  "enable_compression": true,
  "tags_file": "tags.json",
  "log_file": "logs\\opc_agent.log",
  "log_level": "Info"
}
```

### Go (INI)

```ini
[main]
title=System Title
debug=False
task_count=1
opc_host=172.16.32.98
opc_server=KEPware.KEPServerEx.V4

[mqtt]
enabled=True
broker=172.16.32.98
port=1883
topic=opc/data

[task1]
task=True
job_interval_second=1
tag_device=2025
tag_opc1=lt.sc.20251_M4102_ZZT
tag_dbn1=20251_M4102_ZZT
```

**Key points:**
- Use `.` for nested access (e.g., `config.OpcServer`)
- Section names in square brackets: `[main]`, `[mqtt]`, `[task1]`
- Multiple values separated by commas: `rtdb_host=host1,host2`

---

## File Organization

### C# (Windows Agent)

```
OPC_DA_Agent.csproj       # Project file
Program.cs                # Main entry point
Config.cs                 # Configuration management
Logger.cs                 # Logging utility
HttpServer.cs             # HTTP REST API
OPCService.cs             # OPC DA client
OPCBrowser.cs             # OPC browsing functionality
DataModel.cs              # Data transfer objects
```

### Go (Linux Collector)

```
collector_main.go         # Main entry point
ConfigManager.go          # Configuration loading/saving
collector_web.go          # Web API server
KeyTransformer.go         # Key name transformation
go.mod/go.sum             # Go modules
collector.ini             # Configuration file
```

---

## Common Patterns

### Dependency Injection

**C#:**
```csharp
public class HttpServer {
    private readonly Config _config;
    private readonly OPCService _opcService;
    private readonly Logger _logger;

    public HttpServer(Config config, OPCService opcService, Logger logger) {
        _config = config;
        _opcService = opcService;
        _logger = logger;
    }
}
```

**Go:**
```go
type Collector struct {
    config      *AppConfig
    transformer *KeyTransformer
    mqttClient  *MqttClient
    httpClient  *HttpClient
}

func NewCollector(config *AppConfig) *Collector {
    return &Collector{
        config:      config,
        transformer: NewKeyTransformer(),
    }
}
```

### Singleton Pattern

**C#:**
```csharp
private static Logger _instance;
public static Logger Instance => _instance ??= new Logger();
```

**Go:**
```go
var (
    once     sync.Once
    instance *Logger
)

func GetLogger() *Logger {
    once.Do(func() {
        instance = &Logger{}
    })
    return instance
}
```

---

## Important Notes

1. **No unit tests exist** - Add tests when modifying critical code
2. **Mixed languages** - C# for Windows, Go for Linux (no cross-language compatibility)
3. **Configuration formats** - C# uses JSON, Go supports both INI and JSON
4. **Error messages** - Use Chinese for user-facing messages (codebase convention)
5. **Logging** - Always log errors with context, don't suppress exceptions
6. **Null checks** - C#: check for null; Go: check for nil and zero values

---

## External Dependencies

### C# (.NET Framework 4.8)
- **Newtonsoft.Json**: JSON serialization

### Go 1.20
- **github.com/gorilla/mux**: HTTP router
- **gopkg.in/ini.v1**: INI file parsing
