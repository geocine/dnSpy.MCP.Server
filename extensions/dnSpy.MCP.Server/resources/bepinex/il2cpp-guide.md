# BepInEx IL2CPP Plugin Development Guide

## Unity IL2CPP vs Mono: Key Differences

### Plugin Base Class

**Mono:**
```csharp
using BepInEx;

[BepInPlugin("com.example.plugin", "My Plugin", "1.0.0")]
public class Plugin : BaseUnityPlugin  // Inherits MonoBehaviour
{
    private void Awake() { }  // Unity lifecycle method
}
```

**IL2CPP:**
```csharp
using BepInEx;
using BepInEx.Unity.IL2CPP;  // IL2CPP-specific namespace

[BepInPlugin("com.example.plugin", "My Plugin", "1.0.0")]
public class Plugin : BasePlugin  // NOT MonoBehaviour
{
    public override void Load() { }  // BepInEx load method, NOT Awake
}
```

### Key Differences Table

| Feature | Mono | IL2CPP |
|---------|------|--------|
| Base Class | `BaseUnityPlugin` | `BasePlugin` |
| Namespace | `BepInEx` | `BepInEx.Unity.IL2CPP` |
| Entry Point | `Awake()` | `Load()` |
| MonoBehaviour | Yes (is MonoBehaviour) | No (separate system) |
| Logger | `Logger` | `Log` |
| Update Loop | `Update()` method | Must add MonoBehaviour manually |
| Harmony | HarmonyX | HarmonyX (same) |

## IL2CPP Plugin Structure

### Basic Template
```csharp
using BepInEx;
using BepInEx.Unity.IL2CPP;
using BepInEx.Logging;
using HarmonyLib;

namespace MyIL2CPPPlugin
{
    [BepInPlugin("com.example.myplugin", "My IL2CPP Plugin", "1.0.0")]
    public class Plugin : BasePlugin
    {
        public static ManualLogSource Logger;

        public override void Load()
        {
            Logger = Log;
            Logger.LogInfo("Plugin loaded!");

            // Apply Harmony patches
            var harmony = new Harmony("com.example.myplugin");
            harmony.PatchAll();

            // Add custom MonoBehaviour if needed
            AddComponent<MyBehaviour>();
        }
    }
}
```

## Working with IL2CPP Types

### ClassInjector - Registering Custom Types
IL2CPP requires explicit type registration for C# types you want to use:

```csharp
using UnhollowerRuntimeLib;

public override void Load()
{
    // Register custom MonoBehaviour type
    ClassInjector.RegisterTypeInIl2Cpp<MyCustomBehaviour>();

    // Now you can use it
    var go = new GameObject("MyObject");
    go.AddComponent<MyCustomBehaviour>();
}

// Custom MonoBehaviour must have IntPtr constructor
public class MyCustomBehaviour : MonoBehaviour
{
    public MyCustomBehaviour(IntPtr ptr) : base(ptr) { }

    void Update()
    {
        // Your update logic
    }
}
```

### IntPtr Constructor Requirement
**ALL** IL2CPP types you create must have this constructor:

```csharp
public class MyClass : SomeIL2CPPType
{
    // REQUIRED for IL2CPP
    public MyClass(IntPtr ptr) : base(ptr) { }

    // Your custom constructors
    public MyClass() : base(ClassInjector.DerivedConstructorPointer<MyClass>())
    {
        ClassInjector.DerivedConstructorBody(this);
    }
}
```

## Adding MonoBehaviour Components

### Method 1: Using AddComponent Helper
```csharp
using BepInEx.Unity.IL2CPP.Utils.Collections;

public override void Load()
{
    // Register first
    ClassInjector.RegisterTypeInIl2Cpp<GameManager>();

    // Add to existing GameObject
    var manager = Camera.main.gameObject.AddComponent<GameManager>();

    // Or create new GameObject
    var go = new GameObject("Manager");
    go.AddComponent<GameManager>();
    GameObject.DontDestroyOnLoad(go);
}

public class GameManager : MonoBehaviour
{
    public GameManager(IntPtr ptr) : base(ptr) { }

    void Awake()
    {
        Plugin.Logger.LogInfo("GameManager awake");
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F5))
        {
            Plugin.Logger.LogInfo("F5 pressed!");
        }
    }
}
```

### Method 2: Coroutines in IL2CPP
```csharp
using System.Collections;
using MelonLoader.TinyJSON;

public class TimerBehaviour : MonoBehaviour
{
    public TimerBehaviour(IntPtr ptr) : base(ptr) { }

    void Start()
    {
        StartCoroutine(MyCoroutine().WrapToIl2Cpp());
    }

    private IEnumerator MyCoroutine()
    {
        Plugin.Logger.LogInfo("Coroutine started");
        yield return new WaitForSeconds(5f);
        Plugin.Logger.LogInfo("5 seconds passed");
    }
}
```

## IL2CPP Type Conversions

### Il2CppSystem Types
IL2CPP uses special types from the `Il2CppSystem` namespace:

```csharp
// String conversion
string monoString = "Hello"