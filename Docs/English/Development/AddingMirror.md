<!--
Copyright (C) 2026 HyPrism Launcher
SPDX-License-Identifier: GPL-3.0-only
-->

# Adding a Built-in Mirror

This guide is for contributors who want to add a new community mirror to HyPrism's **default set** — the mirrors auto-generated on first launch via `MirrorLoaderService.GetDefaultMirrors()`.

> **For mirror schema reference, URL placeholders, version discovery methods, and `diffBasedBranches` semantics**, see the [User Mirrors Guide](../User/Mirrors.md). This page only covers the contributor workflow.

---

## Prerequisites

Before adding a mirror to the defaults, ensure:

1. The mirror is **publicly available** and reliable (reasonable uptime).
2. The mirror operator has **agreed** to be included in HyPrism.
3. You've **verified** that the mirror serves valid `.pwr` files matching official Hytale patches.
4. You've determined which **source type** fits the mirror: `pattern` or `json-index` (see [User Mirrors Guide — Source types](../User/Mirrors.md#source-type-pattern)).

## Architecture overview

```
MirrorLoaderService.cs          ← Generates defaults + loads *.mirror.json
    ↓ creates
JsonMirrorSource.cs             ← Universal IVersionSource for all mirrors
    ↑ implements
IVersionSource.cs               ← Unified interface (official + mirrors)
    ↑ used by
VersionService.cs               ← Orchestrates all sources, caches results
```

**Key files:**

| File | Purpose |
|------|---------|
| `Sources/HyPrism.Core/Game/Sources/MirrorLoaderService.cs` | Default mirror definitions (`GetDefaultMirrors()`), loading logic |
| `Sources/HyPrism.Core/Game/Sources/JsonMirrorSource.cs` | Handles both `pattern` and `json-index` sources |
| `Sources/HyPrism.Core/Game/Sources/IVersionSource.cs` | Interface + supporting types |
| `Models/MirrorMeta.cs` | Data model for `.mirror.json` schema |

You do **not** need to create a new C# class per mirror. All mirrors use `JsonMirrorSource`.

## Step-by-step guide

### 1. Gather mirror information

Determine:

| Question | Example |
|----------|---------|
| Mirror ID (unique, lowercase, no spaces) | `mymirror` |
| Display name | `MyMirror` |
| URL structure for full builds | `https://cdn.example.com/{os}/{arch}/{branch}/0/{version}.pwr` |
| URL structure for diff patches (if available) | `https://cdn.example.com/{os}/{arch}/{branch}/{from}/{to}.pwr` |
| How to discover available versions | JSON API / HTML autoindex / known static list |
| Does it use non-standard branch/OS names in URLs? | `prerelease` instead of `pre-release`, `mac` instead of `darwin` |
| Which branches have only diffs (no full builds)? | `[]` or `["pre-release"]` |
| A URL to ping for availability checks | `https://cdn.example.com/health` |

### 2. Add the `MirrorMeta` entry

Open `Sources/HyPrism.Core/Game/Sources/MirrorLoaderService.cs` and add a new entry to the list returned by `GetDefaultMirrors()`.

**For a pattern-based mirror:**

```csharp
// MyMirror — json-api based
new() {
    SchemaVersion = 1,
    Id = "mymirror",
    Name = "MyMirror",
    Description = "Community mirror hosted by MyMirror (cdn.example.com)",
    Priority = 103,  // Next available after existing mirrors
    Enabled = true,
    SourceType = "pattern",
    Pattern = new MirrorPatternConfig
    {
        FullBuildUrl = "{base}/{os}/{arch}/{branch}/0/{version}.pwr",
        DiffPatchUrl = "{base}/{os}/{arch}/{branch}/{from}/{to}.pwr",
        BaseUrl = "https://cdn.example.com/hytale",
        VersionDiscovery = new VersionDiscoveryConfig
        {
            Method = "json-api",
            Url = "{base}/api/versions?branch={branch}&os={os}&arch={arch}",
            JsonPath = "versions"
        },
        DiffBasedBranches = new List<string>()
    },
    SpeedTest = new MirrorSpeedTestConfig
    {
        PingUrl = "https://cdn.example.com/health"
    },
    Cache = new MirrorCacheConfig
    {
        IndexTtlMinutes = 30,
        SpeedTestTtlMinutes = 60
    }
}
```

**For a json-index-based mirror:**

```csharp
// MyIndexMirror — single API index
new() {
    SchemaVersion = 1,
    Id = "myindexmirror",
    Name = "MyIndexMirror",
    Description = "Community mirror hosted by MyIndexMirror (api.example.com)",
    Priority = 104,
    Enabled = true,
    SourceType = "json-index",
    JsonIndex = new MirrorJsonIndexConfig
    {
        ApiUrl = "https://api.example.com/index",
        RootPath = "hytale",
        Structure = "grouped",  // or "flat"
        PlatformMapping = new Dictionary<string, string>
        {
            ["darwin"] = "mac"  // only if the API uses non-standard names
        },
        FileNamePattern = new FileNamePatternConfig
        {
            Full = "v{version}-{os}-{arch}.pwr",
            Diff = "v{from}~{to}-{os}-{arch}.pwr"
        },
        DiffBasedBranches = new List<string>()
    },
    SpeedTest = new MirrorSpeedTestConfig
    {
        PingUrl = "https://api.example.com/index"
    },
    Cache = new MirrorCacheConfig
    {
        IndexTtlMinutes = 30,
        SpeedTestTtlMinutes = 60
    }
}
```

### 3. Choose the right priority

Existing default priorities:

| Mirror Type | Priority |
|-------------|----------|
| HTML Autoindex style | 100 |
| JSON API style | 101 |
| JSON Index style | 102 |

Use the next available integer (e.g., `103`). Lower = higher priority. Official Hytale is always `0`.

### 4. Configure mappings (if needed)

If the mirror uses non-standard names in URLs or API keys:

```csharp
// Branch name differs from internal "pre-release"
BranchMapping = new Dictionary<string, string>
{
    ["pre-release"] = "prerelease"
},

// OS name differs from internal "darwin"
OsMapping = new Dictionary<string, string>
{
    ["darwin"] = "macos"
},

// Platform key differs in json-index API
PlatformMapping = new Dictionary<string, string>
{
    ["darwin"] = "mac"
}
```

### 5. Build and test

```bash
# Must compile without errors
dotnet build

# Launch and verify your mirror appears in logs
dotnet run
# Look for: "Loaded mirror: MyMirror (mymirror) [priority=103, type=pattern]"
```

**Manual testing checklist:**

- [ ] Launcher starts without errors
- [ ] Mirror appears in `MirrorLoader` log output
- [ ] `<data-dir>/Mirrors/mymirror.mirror.json` is generated on fresh launch (delete `Mirrors/` folder first to test)
- [ ] Version list loads for `release` branch
- [ ] Version list loads for `pre-release` branch
- [ ] Full build download URL resolves correctly (open in browser)
- [ ] Diff patch URL resolves correctly (if `diffPatchUrl` is configured)
- [ ] Generated JSON file is valid and uses UTF-8 (no `\uXXXX` escapes for readable characters)

### 6. Update documentation

Update the following files:

| File | What to add |
|------|-------------|
| `Docs/English/User/Mirrors.md` | Add annotated example in "Built-in mirror examples" section |
| `Docs/Russian/User/Mirrors.md` | Same in Russian |

You don't need to update the schema reference — it's generic and covers all mirrors.

### 7. Submit a Pull Request

PR checklist:

- [ ] `MirrorLoaderService.cs` — new entry in `GetDefaultMirrors()`
- [ ] `dotnet build` passes with 0 errors, 0 warnings
- [ ] Tested with a fresh `Mirrors/` folder (deleted and regenerated)
- [ ] Mirror verified on at least one platform (Linux, Windows, or macOS)
- [ ] English user docs updated (`Docs/English/User/Mirrors.md`)
- [ ] Russian user docs updated (`Docs/Russian/User/Mirrors.md`)
- [ ] Mirror operator's permission acknowledged in PR description

## Common pitfalls

### Forgetting `DiffBasedBranches`

If you leave this unset (`null`), it defaults to an empty list, which is usually correct. Only add branches here if the mirror **genuinely has no full builds** for that branch.

See [User Mirrors Guide — diffBasedBranches](../User/Mirrors.md#understanding-diffbasedbranches) for details.

### Using the wrong `jsonPath` format

The `jsonPath` field in `VersionDiscoveryConfig` supports exactly three formats:

| jsonPath | API response format |
|----------|-------------------|
| `"$root"` | `[1, 2, 3]` |
| `"versions"` | `{"versions": [1, 2, 3]}` |
| `"items[].version"` | `{"items": [{"version": 1}, ...]}` |

Other JSON path expressions are **not supported**. If the mirror's API doesn't match any of these, you'll need to extend `JsonMirrorSource.ParseVersionsFromJson()`.

### HTML autoindex regex

The regex in `htmlPattern` must be a valid .NET regex. Test it against the actual HTML response. Common issues:
- Double-escaped quotes in C# strings (use `@""` verbatim strings)
- Different HTML structure between nginx, Apache, and other web servers

### Unicode in generated JSON

`MirrorLoaderService` uses `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` to avoid `\uXXXX` escapes in generated files. Don't change this — it keeps generated `.mirror.json` files human-readable.

## Extending the mirror system

If a new mirror requires capabilities not covered by the existing schema (e.g., authentication, custom protocol), you may need to:

1. Add new fields to `MirrorMeta` / `MirrorPatternConfig` / `MirrorJsonIndexConfig` in `Models/MirrorMeta.cs`
2. Handle them in `JsonMirrorSource.cs`
3. Bump `SchemaVersion` if the change is breaking
4. Update the user docs schema reference
5. Ensure backward compatibility — old `.mirror.json` files without new fields must still work (use nullable types with defaults)
