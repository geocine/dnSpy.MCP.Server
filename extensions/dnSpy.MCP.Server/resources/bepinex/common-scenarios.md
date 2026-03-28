# Common BepInEx Plugin Scenarios

## 1. Modifying Player Stats

```csharp
[HarmonyPatch(typeof(Player), "Start")]
class Player_Start_Patch
{
    static void Postfix(Player __instance)
    {
        // Increase player health
        __instance.maxHealth = 200;
        __instance.currentHealth = 200;

        // Increase movement speed
        __instance.moveSpeed = 10f;
    }
}
```

## 2. Unlocking All Items

```csharp
[HarmonyPatch(typeof(ItemDatabase), "IsItemUnlocked")]
class ItemDatabase_IsUnlocked_Patch
{
    static void Postfix(ref bool __result)
    {
        __result = true; // All items unlocked
    }
}
```

## 3. Adding Debug Commands

```csharp
private void Update()
{
    if (Input.GetKeyDown(KeyCode.F1))
    {
        GiveAllItems();
    }

    if (Input.GetKeyDown(KeyCode.F2))
    {
        TeleportPlayer(new Vector3(0, 0, 0));
    }
}

void GiveAllItems()
{
    var inventory = Player.instance.inventory;
    foreach (var item in ItemDatabase.allItems)
    {
        inventory.AddItem(item);
    }
}
```

## 4. Logging Game Information

```csharp
[HarmonyPatch(typeof(GameManager), "LoadLevel")]
class GameManager_LoadLevel_Patch
{
    static void Prefix(string levelName)
    {
        Plugin.Logger.LogInfo($"Loading level: {levelName}");
    }
}
```

## 5. Custom UI with Unity

```csharp
private void OnGUI()
{
    if (GUI.Button(new Rect(10, 10, 100, 30), "Click Me"))
    {
        Logger.LogInfo("Button clicked!");
    }

    GUI.Label(new Rect(10, 50, 200, 30), $"Health: {Player.instance.health}");
}
```

## 6. Preventing Method Execution

```csharp
[HarmonyPatch(typeof(Enemy), "Attack")]
class Enemy_Attack_Patch
{
    static bool Prefix()
    {
        // Return false to completely prevent enemy attacks
        return false;
    }
}
```

## 7. Modifying Method Arguments

```csharp
[HarmonyPatch(typeof(DamageHandler), "ApplyDamage")]
class DamageHandler_ApplyDamage_Patch
{
    static void Prefix(ref int damage)
    {
        // Reduce all damage by 50%
        damage = (int)(damage * 0.5f);
    }
}
```

## 8. Saving Custom Data

```csharp
private void SaveData()
{
    var dataPath = Path.Combine(Paths.ConfigPath, "myplugin_data.json");
    var data = new MyData { score = 100, level = 5 };
    File.WriteAllText(dataPath, JsonUtility.ToJson(data));
}

private MyData LoadData()
{
    var dataPath = Path.Combine(Paths.ConfigPath, "myplugin_data.json");
    if (File.Exists(dataPath))
    {
        return JsonUtility.FromJson<MyData>(File.ReadAllText(dataPath));
    }
    return new MyData();
}
```
