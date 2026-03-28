# Runtime Patching with HarmonyX

BepInEx uses HarmonyX for runtime method patching. HarmonyX allows you to modify game methods without permanently changing game files.

## Basic Harmony Usage

### 1. Initialize Harmony
```csharp
using HarmonyLib;

[BepInPlugin("com.example.myplugin", "My Plugin", "1.0.0")]
public class MyPlugin : BaseUnityPlugin
{
    private void Awake()
    {
        var harmony = new Harmony("com.example.myplugin");
        harmony.PatchAll(); // Automatically patches all [HarmonyPatch] methods
    }
}
```

### 2. Prefix Patches (Run Before Original Method)
```csharp
[HarmonyPatch(typeof(PlayerController), "TakeDamage")]
class TakeDamage_Patch
{
    static bool Prefix(PlayerController __instance, int damage)
    {
        // __instance is the 'this' reference
        // Return false to skip original method
        // Return true to run original method

        if (damage > 100)
        {
            Plugin.Logger.LogInfo("Prevented lethal damage!");
            return false; // Skip original method
        }
        return true; // Run original method
    }
}
```

### 3. Postfix Patches (Run After Original Method)
```csharp
[HarmonyPatch(typeof(PlayerController), "GetHealth")]
class GetHealth_Patch
{
    static void Postfix(ref int __result)
    {
        // __result is the return value
        // You can modify it before it's returned

        __result *= 2; // Double the health
    }
}
```

### 4. Accessing Private Fields
```csharp
[HarmonyPatch(typeof(EnemyAI), "Update")]
class EnemyAI_Patch
{
    static void Prefix(EnemyAI __instance, ref float ___moveSpeed)
    {
        // ___fieldName accesses private fields (three underscores!)
        ___moveSpeed = 10f; // Modify enemy speed
    }
}
```

## Common Patterns

### Patching Methods with Overloads
```csharp
[HarmonyPatch(typeof(ItemManager), "AddItem", new Type[] { typeof(string), typeof(int) })]
class AddItem_Patch
{
    static void Prefix(string itemName, int count)
    {
        Plugin.Logger.LogInfo($"Adding {count}x {itemName}");
    }
}
```

### Patching Properties
```csharp
// Patch getter
[HarmonyPatch(typeof(Player), nameof(Player.MaxHealth), MethodType.Getter)]
class MaxHealth_Getter_Patch
{
    static void Postfix(ref int __result)
    {
        __result = 999; // Unlimited health
    }
}

// Patch setter
[HarmonyPatch(typeof(Player), nameof(Player.MaxHealth), MethodType.Setter)]
class MaxHealth_Setter_Patch
{
    static void Prefix(ref int value)
    {
        value = Math.Max(value, 100); // Minimum health
    }
}
```

### Conditional Patching
```csharp
static bool Prefix(PlayerController __instance)
{
    if (SomeCondition)
    {
        // Do your logic
        return false; // Skip original
    }
    return true; // Run original
}
```

## Parameter Injection

Harmony can inject special parameters:
- `__instance` - The instance object (for non-static methods)
- `__result` - The return value (Postfix only, use `ref`)
- `__state` - Pass data from Prefix to Postfix
- `___fieldName` - Access private field (three underscores!)
- `__args` - All arguments as object array
- Original method parameters by name

## Best Practices

1. **Always use unique Harmony IDs** (usually your plugin GUID)
2. **Use Prefix `return false`** to completely skip original method
3. **Use Postfix** to modify return values or run code after
4. **Keep patches simple** - complex logic should be in separate methods
5. **Log your patches** for debugging
6. **Unpatch on disable**: `harmony.UnpatchSelf()`
