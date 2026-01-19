# Agent Development Guide - OPC DA Agent

## Project Overview

Dual-component OPC DA data collection system:
- **Windows Agent (C#, .NET Framework 4.8)**: OPC DA client with HTTP REST API
- **Linux Collector (Go 1.24.0)**: Data collection with MQTT/HTTP publishing and Web UI

---

## Build Commands

### C# Windows Agent

```bash
# Build using MSBuild
cd csharp_agent
msbuild OPC_DA_Agent.csproj /p:Configuration=Release

# Output: bin\Release\OPC_DA_Agent.exe

# Run
cd bin\Release
OPC_DA_Agent.exe --config config.json
```

### Go Linux Collector

```bash
cd go_collector

# Native build
go build -o collector collector_main.go ConfigManager.go collector_web.go KeyTransformer.go Types.go

# Using build script
./build_collector.sh

# Run
./collector --config collector.ini --web-port 9090
```

### Testing

**No automated tests exist** in this project. Testing is manual:
- Start the agent/collector and verify API endpoints
- Check logs for errors
- Use the Web UI (Go collector) to test MQTT/HTTP connections

---

## C# Code Style

### Naming Conventions
- **Classes/Methods/Properties**: `PascalCase` - `Config`, `LoadFromFile()`, `OpcServerUrl`
- **Private fields**: `_camelCase` - `_logger`, `_config`
- **Constants**: `PascalCase` - `LogFilePath`, `MaxConnections`

### Import Style
```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
```
Standard libraries first (System.*), then third-party. Sort alphabetically.

### JSON Serialization
Use `[JsonProperty]` attributes for camelCase JSON keys:
```csharp
[JsonProperty("opc_server_url")]
public string OpcServerUrl { get; set; }
```

### Error Handling
```csharp
try {
    await _opcService.ConnectAsync();
} catch (Exception ex) {
    _logger.Error("Connection failed", ex);
    Console.WriteLine($"Error: {ex.Message}");
}

// Validation pattern
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
    await _opcService.ConnectAsync();
    await _httpServer.StartAsync();
}

// Use ConfigureAwait(false) in library code
await Task.Delay(1000).ConfigureAwait(false);
```

---

## Go Code Style

### Naming Conventions
- **Exported types/functions**: `PascalCase` - `ConfigManager`, `LoadConfig()`
- **Private types/functions**: `camelCase` - `collectLoop()`, `parseLogLevel()`
- **Constants**: `PascalCase` or `UPPER_SNAKE` - `MaxRetries`, `DEFAULT_TIMEOUT`
- **Package names**: `lowercase` - `main`, `config`

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
Standard library first (alphabetical), then third-party. Blank line between groups.

### Struct Tags for Multiple Formats
```go
type AppConfig struct {
    Title     string `json:"title" ini:"title"`
    Debug     bool   `json:"debug" ini:"debug"`
    OpcHost   string `json:"opc_host" ini:"opc_host"`
    MqttConfig *MqttConfig `json:"mqtt,omitempty"`
}
```

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
    data, err := os.ReadFile(path)
    if err != nil {
        return nil, err
    }
    // ...
}
```

### Struct Initialization
```go
collector := &Collector{
    config:      config,
    transformer: NewKeyTransformer(),
}
```

### Concurrency
```go
// Goroutines for non-blocking operations
go func() {
    webServer := NewWebServer(*configPath)
    webServer.Start(*webPort)
}()

// Channels for signaling
sigChan := make(chan os.Signal, 1)
signal.Notify(sigChan, syscall.SIGINT, syscall.SIGTERM)
<-sigChan
```

---

## Configuration Formats

### C# (JSON)
```json
{
  "opc_server_url": "opcda://localhost/OPCServer.WinCC",
  "http_port": 8080,
  "update_interval_ms": 1000,
  "log_file": "logs\\opc_agent.log",
  "log_level": "Info"
}
```

### Go (INI)
```ini
[main]
title=System Title
debug=False
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
tag_opc1=channel1.device1.value
tag_dbn1=device1_value
```

---

## Important Notes

1. **No automated tests** - Manual testing required
2. **Mixed languages** - C# for Windows, Go for Linux (no cross-compatibility)
3. **Error messages** - Use Chinese for user-facing messages (codebase convention)
4. **Logging** - Always log errors with context, never suppress exceptions
5. **Null checks** - C#: check for null; Go: check for nil and zero values
6. **File organization** - Language-segregated: `csharp_agent/` and `go_collector/`

---

## External Dependencies

### C# (.NET Framework 4.8)
- **Newtonsoft.Json**: JSON serialization

### Go 1.24.0
- **github.com/gorilla/mux**: HTTP router
- **gopkg.in/ini.v1**: INI file parsing
- **github.com/eclipse/paho.mqtt.golang**: MQTT client
