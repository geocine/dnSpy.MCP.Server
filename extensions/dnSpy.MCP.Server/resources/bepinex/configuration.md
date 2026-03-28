# BepInEx Configuration System

## Creating Configuration

```csharp
using BepInEx.Configuration;

[BepInPlugin("com.example.myplugin", "My Plugin", "1.0.0")]
public class MyPlugin : BaseUnityPlugin
{
    // Configuration entries
    private ConfigEntry<bool> enableFeature;
    private ConfigEntry<int> maxValue;
    private ConfigEntry<float> multiplier;
    private ConfigEntry<string> playerName;

    private void Awake()
    {
        // Bind configuration entries
        enableFeature = Config.Bind("General", "EnableFeature", true,
            "Enable or disable the main feature");

        maxValue = Config.Bind("General", "MaxValue", 100,
            new ConfigDescription("Maximum allowed value",
            new AcceptableValueRange<int>(1, 1000)));

        multiplier = Config.Bind("Gameplay", "DamageMultiplier", 1.5f,
            "Damage multiplier for all attacks");

        playerName = Config.Bind("Player", "Name", "DefaultName",
            "Player display name");

        // Use configuration
        if (enableFeature.Value)
        {
            Logger.LogInfo($"Feature enabled! Max value: {maxValue.Value}");
        }
    }
}
```

## ConfigEntry Usage

### Accessing Values
```csharp
// Read value
int currentValue = maxValue.Value;

// Set value (triggers save)
maxValue.Value = 50;
```

### Acceptable Values
```csharp
// Range constraint
new AcceptableValueRange<int>(1, 100)

// List constraint
new AcceptableValueList<string>("Option1", "Option2", "Option3")
```

### Change Events
```csharp
enableFeature.SettingChanged += (sender, args) =>
{
    Logger.LogInfo($"Setting changed to: {enableFeature.Value}");
};
```

## Configuration File

Config files are saved to: `BepInEx/config/com.example.myplugin.cfg`

Example generated file:
```ini
[General]
## Enable or disable the main feature
# Setting type: Boolean
# Default value: true
EnableFeature = true

## Maximum allowed value
# Setting type: Int32
# Default value: 100
# Acceptable value range: From 1 to 1000
MaxValue = 100
```
