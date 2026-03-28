# BepInEx: Mono vs IL2CPP Side-by-Side Comparison

## Quick Identification

**How to identify your game type:**

1. **Check game folder:**
   - Mono: `GameName_Data/Managed/Assembly-CSharp.dll` exists
   - IL2CPP: `GameName_Data/il2cpp_data/` folder exists

2. **Check executable:**
   - Mono: One .exe file
   - IL2CPP: .exe + `GameAssembly.dll`

3. **BepInEx detection:**
   - Run BepInEx once, it will detect and log the game type

## Plugin Structure Comparison

### File: Plugin.cs

#### Mono
```csharp
using BepInEx;
using UnityEngine;

namespace MyPlugin
{
    [BepInPlugin("com.example.plugin", "My Plugin", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        private void Awake()
        {
            Logger.LogInfo("Plugin loaded!");
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1))
            {
                Logger.LogInfo("F1 pressed");
            }
        }
    }
}
```

#### IL2CPP
```csharp
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnhollowerRuntimeLib;
using UnityEngine;

namespace MyPlugin
{
    [BepInPlugin("com.example.plugin", "My Plugin", "1.0.0")]
    public class Plugin : BasePlugin
    {
        public override void Load()
        {
            Log.LogInfo("Plugin loaded!");

            // Must register and add MonoBehaviour manually
            ClassInjector.RegisterTypeInIl2Cpp<MyBehaviour>();
            var go = new GameObject("MyPlugin");
            go.AddComponent<MyBehaviour>();
            GameObject.DontDestroyOnLoad(go);
        }
    }

    // Separate MonoBehaviour class
    public class MyBehaviour : MonoBehaviour
    {
        public MyBehaviour(IntPtr ptr) : base(ptr) { }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1))
            {
                Plugin.Log.LogInfo("F1 pressed");
            }
        }
    }
}
```

## Feature-by-Feature Comparison

### 1. Project Setup

| Aspect | Mono | IL2CPP |
|--------|------|--------|
| **Template** | `dotnet new bep6plugin_unitymono` | `dotnet new bep6plugin_il2cpp` |
| **Target Framework** | `net35`, `net46`, or `netstandard2.0` | `net6.0` |
| **NuGet Package** | `BepInEx.Unity.Mono` | `BepInEx.Unity.IL2CPP` |
| **Game Assemblies** | `GameName_Data/Managed/*.dll` | `BepInEx/unhollowed/*.dll` |

### 2. Plugin Base Class

| Aspect | Mono | IL2CPP |
|--------|------|--------|
| **Base Class** | `BaseUnityPlugin` | `BasePlugin` |
| **Inheritance** | Inherits `MonoBehaviour` | Does NOT inherit `MonoBehaviour` |
| **Namespace** | `using BepInEx;` | `using BepInEx.Unity.IL2CPP;` |

### 3. Initialization

| Aspect | Mono | IL2CPP |
|--------|------|--------|
| **Entry Method** | `void Awake()` | `void Load()` (override) |
| **Lifecycle** | Unity lifecycle methods work | Must add MonoBehaviour manually |
| **Logger** | `Logger.LogInfo()` | `Log.LogInfo()` |

### 4. MonoBehaviour Usage

#### Mono - Built-in
```csharp
[BepInPlugin("...", "...", "...")]
public class Plugin : BaseUnityPlugin  // IS a MonoBehaviour
{
    void Awake() { }     // ✓ Works
    void Start() { }     // ✓ Works
    void Update() { }    // ✓ Works
    void OnGUI() { }     // ✓ Works
}
```

#### IL2CPP - Manual Setup Required
```csharp
[BepInPlugin("...", "...", "...")]
public class Plugin : BasePlugin  // NOT a MonoBehaviour
{
    void Awake() { }   // ✗ Never called
    void Update() { }  // ✗ Never called

    public override void Load()
    {
        // Must create separate MonoBehaviour
        ClassInjector.RegisterTypeInIl2Cpp<MyBehaviour>();
        AddComponent<MyBehaviour>();
    }
}

public class MyBehaviour : MonoBehaviour
{
    public MyBehaviour(IntPtr ptr) : base(ptr) { }  // Required!

    void Update() { }  // ✓ Now this works
}
```

### 5. Custom Types

#### Mono - Direct Usage
```csharp
// Just create the class
public class MyData
{
    public string name;
    public int value;
}

// Use it
var data = new MyData { name = "test", value = 42 };
```

#### IL2CPP - Registration Required
```csharp
// Must register if inheriting IL2CPP types
public class MyComponent : MonoBehaviour
{
    // REQUIRED constructor
    public MyComponent(IntPtr ptr) : base(ptr) { }
}

// Register before use
public override void Load()
{
    ClassInjector.RegisterTypeInIl2Cpp<MyComponent>();

    // Now can use
    var go = new GameObject();
    go.AddComponent<MyComponent>();
}
```

### 6. Type System

#### Mono - Standard .NET
```csharp
string text = "Hello"