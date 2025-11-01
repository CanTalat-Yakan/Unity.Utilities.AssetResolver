# Unity Essentials

This module is part of the Unity Essentials ecosystem and follows the same lightweight, editor-first approach.
Unity Essentials is a lightweight, modular set of editor utilities and helpers that streamline Unity development. It focuses on clean, dependency-free tools that work well together.

All utilities are under the `UnityEssentials` namespace.

```csharp
using UnityEssentials;
```

## Installation

Install the Unity Essentials entry package via Unity's Package Manager, then install modules from the Tools menu.

- Add the entry package (via Git URL)
    - Window → Package Manager
    - "+" → "Add package from git URL…"
    - Paste: `https://github.com/CanTalat-Yakan/UnityEssentials.git`

- Install or update Unity Essentials packages
    - Tools → Install & Update UnityEssentials
    - Install all or select individual modules; run again anytime to update

---

# Resource Loader

> Quick overview: Typed, cached `Resources.Load` helpers for runtime, plus an editor‑only prefab spawner that finds and unpacks prefabs by name. Simple APIs, safe logging, and optional caching/clear.

A tiny utility that makes loading assets from any `Resources/` folder easier and safer. Use `LoadResource<T>(path)` to fetch and optionally cache assets; use `InstantiatePrefab(resourcePath, name, parent)` to spawn prefabs from `Resources`. In the Editor, `ResourceLoaderEditor.InstantiatePrefab(prefabName, name)` searches your project by name, instantiates, parents under the current selection, unpacks the prefab, and registers undo.

![screenshot](Documentation/Screenshot.png)

## Features
- Runtime resource loading
  - `LoadResource<T>(resourcePath, cache = true)` – typed load from `Resources` with optional caching and clear warnings
  - Logs a warning and returns null when the path is empty or the asset can’t be found
  - Overwrites cache on subsequent successful typed loads (safe if the first cached type mismatched)
- Runtime prefab instantiation
  - `InstantiatePrefab(resourcePath, instantiatedName, parent)` – loads a prefab from `Resources` and spawns it with optional name and parent
- Editor prefab spawner (Editor‑only)
  - `ResourceLoaderEditor.InstantiatePrefab(prefabName, instantiatedName)` – finds a prefab by name via `AssetDatabase`, instantiates it, parents under active selection, unpacks completely, registers undo, and selects the instance
- Cache management
  - In‑memory cache keyed by `resourcePath`; `ClearCache()` wipes it

## Requirements
- Unity Editor 6000.0+
- Runtime usage
  - Assets you want to load at runtime must live under a `Resources/` folder
  - `resourcePath` must be relative to a `Resources` folder and omit the file extension (e.g., `UI/MyDoc`)
- Editor usage
  - Editor‑only APIs rely on `UnityEditor` (not available in player builds)
  - Prefab search is by name; the first match is used if multiple exist

Tip: For large assets, consider calling `Resources.UnloadUnusedAssets()` after releasing references to free memory; the loader itself doesn’t unload.

## Usage
- Load a USS or UXML/asset from Resources
```csharp
var style = ResourceLoader.LoadResource<StyleSheet>("UI/Styles/Main");
```

- Instantiate a prefab from Resources
```csharp
var hud = ResourceLoader.InstantiatePrefab("Prefabs/HUD", "HUD", parentTransform);
```

- Clear the cache
```csharp
ResourceLoader.ClearCache();
```

- Editor: Spawn a prefab by name (searches the project)
```csharp
#if UNITY_EDITOR
var splash = ResourceLoaderEditor.InstantiatePrefab("UnityEssentials_Prefab_SplashScreen", "Splash Screen");
#endif
```

## How It Works
- Runtime loader
  - Keeps a `Dictionary<string, Object>` cache keyed by `resourcePath`
  - On `LoadResource<T>`: returns cached `T` when possible; otherwise calls `Resources.Load<T>` and caches the result if `cacheResource` is true
  - If the cached object type mismatches `T`, a warning is logged and a fresh load is attempted; the cache is overwritten on success
- Runtime prefab instantiate
  - Loads a `GameObject` via `Resources.Load<GameObject>(resourcePath)`, instantiates it (optionally sets name and parent), and returns the instance
- Editor prefab instantiate
  - Finds prefabs with `AssetDatabase.FindAssets(<name> + " t:Prefab")`, loads the first path, `PrefabUtility.InstantiatePrefab`, optionally parents under the current selection, unpacks completely, registers undo, selects the instance, and returns it

## Notes and Limitations
- Resource paths
  - Use folder paths relative to a `Resources` root and omit the extension: `Resources/UI/MyDoc.uxml` → `UI/MyDoc`
- Name collisions (Editor)
  - The editor spawner picks the first prefab whose name matches; prefer unique prefab names to avoid ambiguity
- No addressables integration
  - This module targets the built‑in `Resources` workflow; it does not integrate with Addressables
- Caching lifecycle
  - Cache lives for the app domain lifetime or until `ClearCache()`; refreshing domain/scripts clears it
- Null checks and logs
  - Methods log warnings/errors and return null on invalid inputs or missing assets; always null‑check results

## Files in This Package
- Runtime
  - `Runtime/ResourceLoader.cs` – `LoadResource<T>`, `InstantiatePrefab(resourcePath, name, parent)`, `ClearCache()`
  - `Runtime/UnityEssentials.ResourceLoader.asmdef`
- Editor
  - `Editor/ResourceLoaderEditor.cs` – `InstantiatePrefab(name, instantiatedName)` (Editor‑only project‑wide search + unpack + undo)
  - `Editor/UnityEssentials.ResourceLoader.Editor.asmdef`
- `package.json` – Package manifest metadata

## Tags
unity, resources, loader, cache, prefab, instantiate, editor, assetdatabase, resource path, runtime
