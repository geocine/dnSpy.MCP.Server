# BepInEx Plugin Structure (v6.0.0-pre.1)

## Basic Plugin Template

```csharp
using BepInEx;

namespace MyFirstPlugin
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        private void Awake()
        {
            // Plugin startup logic
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
        }
    }
}
```

## Key Components

### 1. BepInPlugin Attribute (REQUIRED)
Without this attribute, BepInEx will ignore your plugin!

```csharp
[BepInPlugin("org.bepinex.plugins.exampleplugin", "Example Plug-In", "1.0.0.0")]
public class ExamplePlugin : BaseUnityPlugin
```

**Parameters:**
- **GUID**: Unique identifier (use reverse domain notation, e.g., "com.author.pluginname")
- **Name**: Human-readable plugin name
- **Version**: Must follow semver format (e.g., "1.0.0")

### 2. BaseUnityPlugin
Inherits from UnityEngine.MonoBehaviour, so you can use Unity lifecycle methods:
- `Awake()` - Called when plugin loads
- `Start()` - Called after all Awake methods
- `Update()` - Called every frame
- `OnDestroy()` - Called when plugin unloads

### 3. Logger
Built-in logging system:
```csharp
Logger.LogInfo("Information message");
Logger.LogWarning("Warning message");
Logger.LogError("Error message");
Logger.LogDebug("Debug message");
```

## Plugin Metadata Attributes

### Dependencies
```csharp
// Hard dependency (required)
[BepInDependency("com.bepinex.plugin.required")]

// Soft dependency (optional)
[BepInDependency("com.bepinex.plugin.optional", BepInDependency.DependencyFlags.SoftDependency)]

// Version-specific dependency
[BepInDependency("com.bepinex.plugin.versioned", "~1.2")]
```

### Process Filtering
```csharp
[BepInProcess("GameName.exe")]
[BepInProcess("AnotherGame.exe")]
```

### Incompatibilities
```csharp
[BepInIncompatibility("some.conflicting.plugin")]
```
