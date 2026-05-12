# ChairOSC

VRChat OSC → ESPHome bridge for the recliner3 chair-massage motors.

Listens for `/avatar/parameters/ChairOSC/v1/{zone}` floats on localhost:9001,
computes per-zone motion velocity, and POSTs intensity 0..1 to ESPHome's REST
API on the recliner3 ESP. Runs as a Windows tray app — open from the tray icon
to configure settings, test motors directly, and see live OSC events.

## Avatar parameters expected

| OSC path | Type | Notes |
|---|---|---|
| `/avatar/parameters/ChairOSC/v1/back`   | float 0..1 | Upper back proximity |
| `/avatar/parameters/ChairOSC/v1/lumbar` | float 0..1 | Lower back |
| `/avatar/parameters/ChairOSC/v1/lthigh` | float 0..1 | Left thigh |
| `/avatar/parameters/ChairOSC/v1/rthigh` | float 0..1 | Right thigh |
| `/avatar/parameters/ChairOSC/v1/lleg`   | float 0..1 | Left leg |
| `/avatar/parameters/ChairOSC/v1/rleg`   | float 0..1 | Right leg |
| `/avatar/parameters/ChairOSC/v1/heat`   | bool       | Heat on/off |

## Velocity logic

```
intensity = max(BaseIntensity, VelocityScale × |dProximity/dt|)   if proximity > TouchThreshold
intensity = 0                                                     otherwise
intensity = clamp(intensity * Multiplier, 0, MaxIntensity)
```

`lthigh+rthigh` and `lleg+rleg` are MAX-aggregated to a single hardware zone
each (chair has 4 motors, not 6). Aggregation is configurable in `config.json`.

## Build

Requires .NET 8 SDK and Visual Studio 2022+ (or `dotnet build` from CLI).

```
dotnet build -c Release
```

Output: `ChairOSC/bin/Release/net8.0-windows/ChairOSC.exe`.

## Config

Stored at `%APPDATA%\ChairOSC\config.json`. Edit through the Settings window
(open via tray double-click) and click **Apply & Save**. Hardware-zone mapping
(which OSC zone maps to which motor 1..4) lives in the JSON — change once you
know which physical body region each motor vibrates and the mapping is fixed.

Default placeholder mapping:

| OSC zone | Hardware zone |
|---|---|
| back   | 1 |
| lumbar | 2 |
| lthigh | 3 |
| rthigh | 3 |
| lleg   | 4 |
| rleg   | 4 |

## ESPHome side

Expects `recliner3` firmware ≥ v2.10 with these entities:

- `number.recliner3_massage_zone_{1..4}_intensity` (float 0..1)
- `switch.recliner3_massage_heat` (on/off)

REST endpoints used:

```
POST http://{esp_host}/number/recliner3_massage_zone_1_intensity/set?value=0.5
POST http://{esp_host}/switch/recliner3_massage_heat/turn_on
POST http://{esp_host}/switch/recliner3_massage_heat/turn_off
```

ESPHome must have `web_server: version: 3` enabled.
