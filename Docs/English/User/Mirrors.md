<!--
Copyright (C) 2026 HyPrism Launcher
SPDX-License-Identifier: GPL-3.0-only
-->

# Mirrors Guide

HyPrism uses a **data-driven mirror system** for downloading game files. Instead of hardcoding download URLs, the launcher reads mirror definitions from `.mirror.json` files in the `Mirrors/` directory. This guide explains how the system works, how to add mirrors, and how to configure them.

---

## Table of Contents

- [Overview](#overview)
- [How mirrors work](#how-mirrors-work)
- [Adding mirrors](#adding-mirrors)
  - [Method 1: Add via Settings UI (Recommended)](#method-1-add-via-settings-ui-recommended)
  - [Method 2: Manual JSON file](#method-2-manual-json-file)
- [Data directory location](#data-directory-location)
- [Mirrors directory structure](#mirrors-directory-structure)
- [Mirror schema reference](#mirror-schema-reference)
  - [Common fields](#common-fields)
  - [Source type: pattern](#source-type-pattern)
  - [Source type: json-index](#source-type-json-index)
  - [Speed test config](#speed-test-config)
  - [Cache config](#cache-config)
- [URL placeholders reference](#url-placeholders-reference)
- [Version discovery methods](#version-discovery-methods)
  - [json-api](#method-json-api)
  - [html-autoindex](#method-html-autoindex)
  - [static-list](#method-static-list)
- [Understanding diffBasedBranches](#understanding-diffbasedbranches)
- [Mirror examples (annotated)](#mirror-examples-annotated)
  - [HTML Autoindex style (pattern + html-autoindex)](#html-autoindex-style-pattern--html-autoindex)
  - [JSON API style (pattern + json-api)](#json-api-style-pattern--json-api)
  - [JSON Index style (json-index)](#json-index-style-json-index)
- [Tutorials](#tutorials)
  - [Tutorial 1: pattern mirror with a JSON API](#tutorial-1-pattern-mirror-with-a-json-api)
  - [Tutorial 2: pattern mirror with HTML directory listing](#tutorial-2-pattern-mirror-with-html-directory-listing)
  - [Tutorial 3: json-index mirror](#tutorial-3-json-index-mirror)
  - [Tutorial 4: simple static mirror](#tutorial-4-simple-static-mirror)
- [Disabling, re-enabling, and removing mirrors](#disabling-re-enabling-and-removing-mirrors)
- [Troubleshooting](#troubleshooting)
- [FAQ](#faq)

---

## Overview

HyPrism always tries the **official Hytale servers first** (if you have an official account). If the official download is unavailable or slow, the launcher automatically tests available mirrors and uses the best reachable one based on priority and speed.

Mirrors are community-hosted servers that replicate Hytale game files. They are useful when:

- You don't have an official Hytale account
- Official servers are down or unreachable in your region
- You want faster download speeds from a geographically closer server
- You want to self-host files for your community

> **Note:** No mirrors are pre-configured by default. You must add mirrors manually through Settings or by placing `.mirror.json` files in the Mirrors folder.

## How mirrors work

1. On startup, the launcher reads all `*.mirror.json` files from the `Mirrors/` directory and creates download sources from them.
2. Mirrors with `"enabled": false` are skipped.
3. Sources are **sorted by priority** (lower number = higher priority). The official Hytale source has priority `0`, mirrors typically start at `100`.
4. When a download is needed, the launcher picks the best available source automatically.

## Adding mirrors

### Method 1: Add via Settings UI (Recommended)

1. Open **Settings → Downloads**.
2. Click the **"Add Mirror"** button.
3. Enter the mirror's base URL (e.g., `https://mirror.example.com/hytale/patches`).
4. The launcher will **automatically detect** the mirror configuration (JSON API, HTML autoindex, or directory structure).
5. If detection succeeds, the mirror is added immediately and ready to use.

The auto-detection system tries multiple strategies:
- **JSON Index API** - Single endpoint returning full file index
- **JSON API** - Version list endpoint
- **HTML Autoindex** - Apache/Nginx directory listing
- **Directory Structure** - Pattern-based URL with OS/arch/branch subdirectories

### Method 2: Manual JSON file

1. Navigate to your data directory → `Mirrors/` folder.
2. Create a new file, for example `my-mirror.mirror.json`.
3. Paste a mirror template (see below) and fill in your server details.
4. Restart the launcher.

## Data directory location

| Platform | Path |
|----------|------|
| **Windows** | `%APPDATA%\Local\HyPrism\` |
| **Linux** | `~/.local/share/HyPrism/` |
| **macOS** | `~/Library/Application Support/HyPrism/` |

You can also open this directory from **Settings → Data → Open Launcher Folder** inside the launcher.

## Mirrors directory structure

```
<data-dir>/
└── Mirrors/
    └── my-custom.mirror.json      ← your custom mirror
```

File naming convention: `<mirror-id>.mirror.json`. The file name doesn't affect functionality — the launcher uses the `id` field inside the JSON — but keeping them matched makes management easier.

> **Tip:** When you add a mirror via the Settings UI, the launcher automatically creates the `.mirror.json` file for you.

---

## Mirror schema reference

### Common fields

These fields apply to every mirror regardless of source type.

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `schemaVersion` | int | Yes | `1` | Schema version. Always set to `1`. |
| `id` | string | **Yes** | — | Unique identifier. Used internally for caching and logging. Must be unique across all mirrors. |
| `name` | string | No | `""` | Human-readable display name. |
| `description` | string | No | `null` | Optional description text. |
| `priority` | int | No | `100` | Ordering priority. Lower number = higher priority. Official source is `0`. Mirrors should use `≥ 100`. |
| `enabled` | bool | No | `true` | Set to `false` to disable this mirror without deleting the file. |
| `sourceType` | string | **Yes** | `"pattern"` | Must be `"pattern"` or `"json-index"`. Determines which config block is used. |
| `pattern` | object | Conditional | `null` | Required when `sourceType` is `"pattern"`. See [Pattern config](#source-type-pattern). |
| `jsonIndex` | object | Conditional | `null` | Required when `sourceType` is `"json-index"`. See [JSON index config](#source-type-json-index). |
| `speedTest` | object | No | `{}` | Speed test configuration. See [Speed test config](#speed-test-config). |
| `cache` | object | No | `{}` | Cache TTL configuration. See [Cache config](#cache-config). |

### Source type: pattern

**Source type `"pattern"`** is for mirrors where you know the URL structure. The launcher builds download URLs from templates by substituting placeholders, and discovers available versions through a separate endpoint.

#### Pattern fields

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `fullBuildUrl` | string | Yes | `"{base}/{os}/{arch}/{branch}/0/{version}.pwr"` | URL template for full game builds. See [URL placeholders](#url-placeholders-reference). |
| `diffPatchUrl` | string | No | `null` | URL template for differential patches (version-to-version). If omitted, only full builds are available. |
| `signatureUrl` | string | No | `null` | URL template for `.sig` signature files. Used for verification. |
| `baseUrl` | string | Yes | `""` | Base URL substituted into the `{base}` placeholder. |
| `versionDiscovery` | object | Yes | — | Configures how to find which versions are available. See [Version discovery methods](#version-discovery-methods). |
| `osMapping` | object | No | `null` | Overrides internal OS names in URLs. Example: `{"darwin": "macos"}`. |
| `branchMapping` | object | No | `null` | Overrides internal branch names in URLs. Example: `{"pre-release": "prerelease"}`. |
| `diffBasedBranches` | string[] | No | `[]` | Branches where **only** diff patches exist (no full builds). See [Understanding diffBasedBranches](#understanding-diffbasedbranches). |

#### How URL templates are resolved

Given this configuration:
```json
{
  "fullBuildUrl": "{base}/{os}/{arch}/{branch}/0/{version}.pwr",
  "diffPatchUrl": "{base}/{os}/{arch}/{branch}/{from}/{to}.pwr",
  "baseUrl": "https://my-server.example.com/hytale/patches"
}
```

For a full build of version 8 on Linux x64 release:
```
https://my-server.example.com/hytale/patches/linux/x64/release/0/8.pwr
```

For a diff patch from version 7 to 8:
```
https://my-server.example.com/hytale/patches/linux/x64/release/7/8.pwr
```

#### Branch mapping example

If your server uses `prerelease` instead of `pre-release` in URLs:
```json
{
  "branchMapping": {
    "pre-release": "prerelease"
  }
}
```

The URL `{base}/{os}/{arch}/{branch}/0/{version}.pwr` with branch `pre-release` will resolve to:
```
https://example.com/linux/x64/prerelease/0/8.pwr
```
instead of:
```
https://example.com/linux/x64/pre-release/0/8.pwr
```

#### OS mapping example

If your server uses `macos` instead of `darwin`:
```json
{
  "osMapping": {
    "darwin": "macos"
  }
}
```

### Source type: json-index

**Source type `"json-index"`** is for mirrors that expose a single API endpoint returning a complete file index with download URLs. The launcher fetches the index once and extracts all version information and download URLs from it.

#### JSON index fields

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `apiUrl` | string | Yes | `""` | URL of the API endpoint returning the full file index. |
| `rootPath` | string | No | `"hytale"` | Root property name in the JSON response to look under. |
| `structure` | string | No | `"flat"` | How files are organized: `"flat"` or `"grouped"`. See below. |
| `platformMapping` | object | No | `null` | Maps internal OS names to JSON platform keys. Example: `{"darwin": "mac"}`. |
| `fileNamePattern` | object | No | — | Patterns for parsing file names. See below. |
| `diffBasedBranches` | string[] | No | `[]` | Branches where **only** diff patches exist (no full builds). See [Understanding diffBasedBranches](#understanding-diffbasedbranches). |

#### File name patterns

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `fileNamePattern.full` | string | `"v{version}-{os}-{arch}.pwr"` | Pattern to match/parse full build file names. |
| `fileNamePattern.diff` | string | `"v{from}~{to}-{os}-{arch}.pwr"` | Pattern to match/parse diff patch file names. |

#### Structure: flat vs. grouped

**`"flat"`** — files are organized as `branch.platform → { filename: url }`:
```json
{
  "hytale": {
    "release.linux.x64": {
      "v8-linux-x64.pwr": "https://example.com/v8-linux-x64.pwr"
    }
  }
}
```

**`"grouped"`** — files are organized as `branch.platform → group → { filename: url }`, where groups are typically `"base"` (full builds) and `"patch"` (diff patches):
```json
{
  "hytale": {
    "release.linux.x64": {
      "base": {
        "v8-linux-x64.pwr": "https://example.com/base/v8-linux-x64.pwr"
      },
      "patch": {
        "v7~8-linux-x64.pwr": "https://example.com/patch/v7~8-linux-x64.pwr"
      }
    }
  }
}
```

### Speed test config

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `speedTest.pingUrl` | string | `null` | URL for a quick availability check (HTTP HEAD request). Set to any URL on your server. |
| `speedTest.pingTimeoutSeconds` | int | `5` | Timeout in seconds for the ping check. |
| `speedTest.speedTestSizeBytes` | int | `10485760` (10 MB) | Amount of data to download for the speed test. |

If `pingUrl` is not set, the launcher will skip speed testing for this mirror.

### Cache config

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `cache.indexTtlMinutes` | int | `30` | How long to cache the version index before refreshing (minutes). |
| `cache.speedTestTtlMinutes` | int | `60` | How long to cache the speed test result (minutes). |

---

## URL placeholders reference

These placeholders are available in `fullBuildUrl`, `diffPatchUrl`, `signatureUrl`, and `versionDiscovery.url` templates:

| Placeholder | Description | Example values |
|-------------|-------------|----------------|
| `{base}` | Replaced with `baseUrl` | `https://my-server.example.com/hytale` |
| `{os}` | Operating system | `linux`, `windows`, `darwin` |
| `{arch}` | CPU architecture | `x64`, `arm64` |
| `{branch}` | Game branch (after mapping) | `release`, `pre-release` |
| `{version}` | Target version number | `1`, `2`, ..., `8` |
| `{from}` | Source version for diff patches | `7` |
| `{to}` | Target version for diff patches | `8` |

> **Note:** If `osMapping` or `branchMapping` are defined, the mapped values are used instead of the internal names. For example, with `{"darwin": "mac"}` in `osMapping`, `{os}` on macOS will be `mac`, not `darwin`.

---

## Version discovery methods

For `sourceType: "pattern"`, the launcher needs to know which versions are available. This is configured via the `versionDiscovery` block.

### Method: json-api

Fetches a JSON endpoint and extracts version numbers.

```json
{
  "versionDiscovery": {
    "method": "json-api",
    "url": "{base}/versions?branch={branch}&os={os}&arch={arch}",
    "jsonPath": "items[].version"
  }
}
```

**`jsonPath`** supports three formats:

| Format | Expected JSON | Example |
|--------|---------------|---------|
| `"$root"` | Root is an array of numbers | `[1, 2, 3, 4, 5]` |
| `"versions"` | Property contains array of numbers | `{"versions": [1, 2, 3]}` |
| `"items[].version"` | Array of objects with a version field | `{"items": [{"version": 1}, {"version": 2}]}` |

**Example** — JSON API method:
```json
{
  "method": "json-api",
  "url": "{base}/launcher/patches/{branch}/versions?os_name={os}&arch={arch}",
  "jsonPath": "items[].version"
}
```

A typical API at `https://example.com/launcher/patches/release/versions?os_name=linux&arch=x64` returns:
```json
{
  "items": [
    { "version": 1, "size": 1234567 },
    { "version": 2, "size": 2345678 },
    ...
  ]
}
```

### Method: html-autoindex

Parses an HTML directory listing (like nginx autoindex or Apache directory listing) to extract file names and version numbers.

```json
{
  "versionDiscovery": {
    "method": "html-autoindex",
    "url": "{base}/{os}/{arch}/{branch}/0/",
    "htmlPattern": "<a\\s+href=\"(\\d+)\\.pwr\">\\d+\\.pwr</a>\\s+\\S+\\s+\\S+\\s+(\\d+)",
    "minFileSizeBytes": 1048576
  }
}
```

| Field | Description |
|-------|-------------|
| `htmlPattern` | Regular expression applied to the HTML. Capture group 1 = version number. Capture group 2 (optional) = file size in bytes. |
| `minFileSizeBytes` | If the regex captures a file size (group 2), files smaller than this are ignored. Useful to filter out incomplete uploads. Default: `0` (no filtering). |

**Example** — nginx autoindex. A typical HTML directory listing at `https://example.com/hytale/patches/linux/x64/release/0/` looks like:

```html
<pre>
<a href="1.pwr">1.pwr</a>       2025-01-15 12:34    123456789
<a href="2.pwr">2.pwr</a>       2025-01-20 15:00    234567890
...
</pre>
```

The regex `<a\s+href="(\d+)\.pwr">\d+\.pwr</a>\s+\S+\s+\S+\s+(\d+)` captures:
- Group 1: `1`, `2`, etc. (version number)
- Group 2: `123456789`, etc. (file size)

Files smaller than 1 MB (`1048576` bytes) are ignored.

### Method: static-list

Hardcoded list of version numbers. No network request needed for version discovery.

```json
{
  "versionDiscovery": {
    "method": "static-list",
    "staticVersions": [1, 2, 3, 4, 5, 6, 7, 8]
  }
}
```

Use this when:
- Your mirror has a fixed set of versions
- Your server doesn't provide a version listing API
- You want the simplest possible configuration

> **Downside:** You need to manually update the list when new versions appear.

---

## Understanding diffBasedBranches

The `diffBasedBranches` field tells the launcher which branches have **only** differential patches and **no** full builds available.

### What are full builds vs. diff patches?

- **Full build** — A complete game package for a specific version (e.g., version 8). Download once to install.
- **Diff patch** — An incremental update from one version to another (e.g., version 7 → 8). Much smaller than full builds, but requires the previous version already installed.

### How `diffBasedBranches` affects behavior

| `diffBasedBranches` | Full builds available? | Diff patches available? | First install | Updates |
|---------------------|----------------------|------------------------|---------------|---------|
| Branch **not** in list | ✅ Yes | ✅ Yes (if `diffPatchUrl` is set) | Downloads full build | Uses diff if available, falls back to full |
| Branch **in** list | ❌ No | ✅ Yes | Applies chain of diffs from version 0 | Uses diff patches |

### Example

```json
{
  "diffBasedBranches": ["pre-release"]
}
```

This means:
- **release** branch: Has full builds. Users can download any version directly. Diff patches are also available for updates.
- **pre-release** branch: Only has diff patches. To install version 22, the launcher downloads patches `0→1`, `1→2`, ..., `21→22` and applies them sequentially.

### When to use

- Set to `[]` (empty) if your mirror stores full builds for all branches. Most mirrors should use this.
- Add a branch name if your mirror **genuinely** has no full builds for that branch — only version-to-version diffs.
- Even if `diffBasedBranches` is empty, the launcher will **still use diff patches** for updates when `diffPatchUrl` is configured. The field only affects whether **full builds** are available.

---

## Mirror examples (annotated)

Below are example mirror configurations demonstrating different setups. These are illustrative templates — you can use them as a starting point when configuring your own mirrors.

### HTML Autoindex style (pattern + html-autoindex)

This example shows a mirror that hosts files on a web server with nginx/Apache autoindex enabled. The launcher parses the HTML directory listing to discover versions.

```json
{
  "schemaVersion": 1,
  "id": "my-html-autoindex-mirror",
  "name": "My HTML Mirror",
  "description": "Mirror using HTML directory listing for version discovery",
  "priority": 100,
  "enabled": true,
  "sourceType": "pattern",
  "pattern": {
    "fullBuildUrl": "{base}/{os}/{arch}/{branch}/0/{version}.pwr",
    "diffPatchUrl": "{base}/{os}/{arch}/{branch}/{from}/{to}.pwr",
    "signatureUrl": "{base}/{os}/{arch}/{branch}/0/{version}.pwr.sig",
    "baseUrl": "https://my-server.example.com/hytale/patches",
    "versionDiscovery": {
      "method": "html-autoindex",
      "url": "{base}/{os}/{arch}/{branch}/0/",
      "htmlPattern": "<a\\s+href=\"(\\d+)\\.pwr\">\\d+\\.pwr</a>\\s+\\S+\\s+\\S+\\s+(\\d+)",
      "minFileSizeBytes": 1048576
    },
    "diffBasedBranches": []
  },
  "speedTest": {
    "pingUrl": "https://my-server.example.com/hytale/patches"
  },
  "cache": {
    "indexTtlMinutes": 30,
    "speedTestTtlMinutes": 60
  }
}
```

**Key points:**
- **File structure on server:** `patches/{os}/{arch}/{branch}/0/{version}.pwr` for full builds, `patches/{os}/{arch}/{branch}/{from}/{to}.pwr` for diffs.
- **Version discovery:** Parses the HTML at `.../release/0/` to find `*.pwr` files and extract version numbers.
- **`diffBasedBranches: []`** — Both release and pre-release have full builds available.
- **`signatureUrl`** — Provides `.pwr.sig` files for verifying downloads.
- **`diffPatchUrl`** is set — diff patches are available for all branches (faster updates).

**Resolved URLs (Linux x64, release, version 8):**
| Type | URL |
|------|-----|
| Full build | `https://my-server.example.com/hytale/patches/linux/x64/release/0/8.pwr` |
| Diff 7→8 | `https://my-server.example.com/hytale/patches/linux/x64/release/7/8.pwr` |
| Signature | `https://my-server.example.com/hytale/patches/linux/x64/release/0/8.pwr.sig` |
| Version list | `https://my-server.example.com/hytale/patches/linux/x64/release/0/` |

### JSON API style (pattern + json-api)

This example shows a mirror that provides a JSON API for version discovery. Note the use of `branchMapping` for URL customization.

```json
{
  "schemaVersion": 1,
  "id": "my-json-api-mirror",
  "name": "My JSON API Mirror",
  "description": "Mirror with JSON API for version discovery",
  "priority": 101,
  "enabled": true,
  "sourceType": "pattern",
  "pattern": {
    "fullBuildUrl": "{base}/launcher/patches/{os}/{arch}/{branch}/0/{version}.pwr",
    "diffPatchUrl": "{base}/launcher/patches/{os}/{arch}/{branch}/{from}/{to}.pwr",
    "baseUrl": "https://my-server.example.com",
    "versionDiscovery": {
      "method": "json-api",
      "url": "{base}/launcher/patches/{branch}/versions?os_name={os}&arch={arch}",
      "jsonPath": "items[].version"
    },
    "branchMapping": {
      "pre-release": "prerelease"
    },
    "diffBasedBranches": []
  },
  "speedTest": {
    "pingUrl": "https://my-server.example.com/health"
  },
  "cache": {
    "indexTtlMinutes": 30,
    "speedTestTtlMinutes": 60
  }
}
```

**Key points:**
- **`branchMapping`** — Translates `pre-release` to `prerelease` in URLs.
- **Version discovery:** `json-api` method. The API returns `{ "items": [{ "version": 1 }, ...] }`, and `jsonPath: "items[].version"` extracts the numbers.
- **No `signatureUrl`** — This mirror doesn't provide signature files.
- **`diffPatchUrl`** is set — diff patches are available.

**Resolved URLs (Linux x64, pre-release branch, version 22):**
| Type | URL |
|------|-----|
| Full build | `https://my-server.example.com/launcher/patches/linux/x64/prerelease/0/22.pwr` |
| Diff 21→22 | `https://my-server.example.com/launcher/patches/linux/x64/prerelease/21/22.pwr` |
| Version list | `https://my-server.example.com/launcher/patches/prerelease/versions?os_name=linux&arch=x64` |

> Note how `{branch}` resolved to `prerelease` (not `pre-release`) because of the `branchMapping`.

### JSON Index style (json-index)

This example shows a mirror that provides a single API returning a full file index with direct download URLs. This is the simplest setup for mirror operators since the launcher just needs one endpoint.

```json
{
  "schemaVersion": 1,
  "id": "my-json-index-mirror",
  "name": "My JSON Index Mirror",
  "description": "Mirror using JSON index API for file discovery",
  "priority": 102,
  "enabled": true,
  "sourceType": "json-index",
  "jsonIndex": {
    "apiUrl": "https://my-server.example.com/api/files",
    "rootPath": "hytale",
    "structure": "grouped",
    "platformMapping": {
      "darwin": "mac"
    },
    "fileNamePattern": {
      "full": "v{version}-{os}-{arch}.pwr",
      "diff": "v{from}~{to}-{os}-{arch}.pwr"
    },
    "diffBasedBranches": ["pre-release"]
  },
  "speedTest": {
    "pingUrl": "https://my-server.example.com/api/files"
  },
  "cache": {
    "indexTtlMinutes": 30,
    "speedTestTtlMinutes": 60
  }
}
```

**Key points:**
- **`sourceType: "json-index"`** — No URL templates needed. The API returns full download URLs.
- **`structure: "grouped"`** — Files are split into `"base"` (full builds) and `"patch"` (diffs) groups.
- **`platformMapping: {"darwin": "mac"}`** — macOS is stored as `mac` in the API.
- **`fileNamePattern`** — Tells the launcher how to parse file names like `v8-linux-x64.pwr` (full) and `v7~8-linux-x64.pwr` (diff).
- **`diffBasedBranches: ["pre-release"]`** — The pre-release branch has only diff patches (no full builds). The release branch has both full builds and diffs.

**Example API response (simplified):**
```json
{
  "hytale": {
    "release.linux.x64": {
      "base": {
        "v1-linux-x64.pwr": "https://my-server.example.com/.../v1-linux-x64.pwr",
        "v2-linux-x64.pwr": "https://my-server.example.com/.../v2-linux-x64.pwr",
        ...
        "v8-linux-x64.pwr": "https://my-server.example.com/.../v8-linux-x64.pwr"
      },
      "patch": {
        "v1~2-linux-x64.pwr": "https://my-server.example.com/.../v1~2-linux-x64.pwr",
        "v2~3-linux-x64.pwr": "https://my-server.example.com/.../v2~3-linux-x64.pwr",
        ...
        "v7~8-linux-x64.pwr": "https://my-server.example.com/.../v7~8-linux-x64.pwr"
      }
    },
    "pre-release.linux.x64": {
      "patch": {
        "v0~1-linux-x64.pwr": "https://my-server.example.com/.../v0~1-linux-x64.pwr",
        "v1~2-linux-x64.pwr": "https://my-server.example.com/.../v1~2-linux-x64.pwr",
        ...
        "v21~22-linux-x64.pwr": "https://my-server.example.com/.../v21~22-linux-x64.pwr"
      }
    }
  }
}
```

---

## Tutorials

### Tutorial 1: pattern mirror with a JSON API

**Scenario:** You host game files at `https://files.myguild.com/hytale/` and have a simple API at `/api/versions` that returns version numbers.

**Step 1:** Determine your URL structure.
- Full builds: `https://files.myguild.com/hytale/{os}/{arch}/{branch}/full/{version}.pwr`
- You don't have diff patches.

**Step 2:** Determine your API response format.
- `GET https://files.myguild.com/hytale/api/versions?branch=release` returns:
  ```json
  {"versions": [1, 2, 3, 4, 5, 6, 7, 8]}
  ```

**Step 3:** Create `myguild.mirror.json`:

```json
{
  "schemaVersion": 1,
  "id": "myguild",
  "name": "My Guild Mirror",
  "description": "Private mirror for our guild",
  "priority": 105,
  "enabled": true,
  "sourceType": "pattern",
  "pattern": {
    "fullBuildUrl": "{base}/{os}/{arch}/{branch}/full/{version}.pwr",
    "baseUrl": "https://files.myguild.com/hytale",
    "versionDiscovery": {
      "method": "json-api",
      "url": "{base}/api/versions?branch={branch}",
      "jsonPath": "versions"
    }
  },
  "speedTest": {
    "pingUrl": "https://files.myguild.com/hytale/api/versions"
  }
}
```

**Step 4:** Place the file in `<data-dir>/Mirrors/` and restart the launcher.

### Tutorial 2: pattern mirror with HTML directory listing

**Scenario:** You host files on a web server with nginx autoindex or Apache directory listing.

**Step 1:** Your directory structure on the server:
```
/var/www/hytale/
├── linux/x64/release/
│   ├── 1.pwr
│   ├── 2.pwr
│   └── ...
└── windows/x64/release/
    ├── 1.pwr
    └── ...
```

**Step 2:** Accessing `https://mirror.example.com/hytale/linux/x64/release/` shows an HTML directory listing with `<a>` tags for each file.

**Step 3:** Create `my-nginx.mirror.json`:

```json
{
  "schemaVersion": 1,
  "id": "my-nginx",
  "name": "My Nginx Mirror",
  "priority": 120,
  "enabled": true,
  "sourceType": "pattern",
  "pattern": {
    "fullBuildUrl": "{base}/{os}/{arch}/{branch}/{version}.pwr",
    "baseUrl": "https://mirror.example.com/hytale",
    "versionDiscovery": {
      "method": "html-autoindex",
      "url": "{base}/{os}/{arch}/{branch}/",
      "htmlPattern": "<a\\s+href=\"(\\d+)\\.pwr\">"
    }
  }
}
```

> **Tip:** The `htmlPattern` regex needs only one capture group (the version number). The file size group is optional and is used for filtering small/incomplete files.

### Tutorial 3: json-index mirror

**Scenario:** Your server provides a single API endpoint that returns a flat JSON index of all files.

**Step 1:** Your API at `https://api.mymirror.com/index` returns:
```json
{
  "game": {
    "release.linux.x64": {
      "v1-linux-x64.pwr": "https://cdn.mymirror.com/release/v1-linux-x64.pwr",
      "v2-linux-x64.pwr": "https://cdn.mymirror.com/release/v2-linux-x64.pwr"
    },
    "release.windows.x64": {
      "v1-windows-x64.pwr": "https://cdn.mymirror.com/release/v1-windows-x64.pwr"
    }
  }
}
```

**Step 2:** Create `mymirror.mirror.json`:

```json
{
  "schemaVersion": 1,
  "id": "mymirror",
  "name": "My CDN Mirror",
  "priority": 108,
  "enabled": true,
  "sourceType": "json-index",
  "jsonIndex": {
    "apiUrl": "https://api.mymirror.com/index",
    "rootPath": "game",
    "structure": "flat",
    "fileNamePattern": {
      "full": "v{version}-{os}-{arch}.pwr"
    }
  },
  "speedTest": {
    "pingUrl": "https://cdn.mymirror.com"
  }
}
```

**Key details:**
- `rootPath` is `"game"` because the files are nested under the `"game"` key in the response.
- `structure` is `"flat"` because there are no `"base"`/`"patch"` groups.
- No `diff` pattern is defined in `fileNamePattern` because this mirror only has full builds.

### Tutorial 4: simple static mirror

**Scenario:** You have a basic file server with known versions. No API, no directory listing.

Create `static.mirror.json`:

```json
{
  "schemaVersion": 1,
  "id": "my-static",
  "name": "Static File Server",
  "priority": 150,
  "enabled": true,
  "sourceType": "pattern",
  "pattern": {
    "fullBuildUrl": "{base}/game-v{version}-{os}-{arch}.pwr",
    "baseUrl": "https://files.example.com",
    "versionDiscovery": {
      "method": "static-list",
      "staticVersions": [5, 6, 7, 8]
    }
  }
}
```

This will resolve to:
```
https://files.example.com/game-v8-linux-x64.pwr
```

> **Remember:** With `static-list`, you need to manually update the `staticVersions` array when new versions are released.

---

## Disabling, re-enabling, and removing mirrors

| Action | How to |
|--------|--------|
| **Disable temporarily** | Open the `.mirror.json` file and set `"enabled": false`. Restart the launcher. |
| **Re-enable** | Set `"enabled": true`. Restart the launcher. |
| **Remove permanently** | Delete the `.mirror.json` file from the `Mirrors/` folder. Restart the launcher. |
| **Reset all to defaults** | Delete the entire `Mirrors/` folder. The launcher will regenerate defaults on next start. |

---

## Troubleshooting

### Mirror not appearing in the launcher

1. Check that the file is in the correct `Mirrors/` directory.
2. Check that the file extension is exactly `.mirror.json` (not `.json` alone).
3. Check that the JSON is valid (use a JSON validator).
4. Check the launcher logs (**Settings → Logs**) for error messages from `MirrorLoader`.
5. Make sure `"enabled"` is set to `true`.
6. Make sure `"id"` is not empty.

### Mirror loads but downloads fail

1. Check that the `baseUrl` or `apiUrl` is reachable from your network.
2. For pattern mirrors, manually construct a URL using the template and open it in a browser to test.
3. Verify that `osMapping` and `branchMapping` match your server's URL structure.
4. Check the launcher logs for HTTP error codes.

### Version list is empty

1. For `json-api`: check that the `url` returns valid JSON and that `jsonPath` correctly points to the version array.
2. For `html-autoindex`: check that `htmlPattern` matches the HTML structure of your server's directory listing.
3. For `static-list`: check that `staticVersions` is a non-empty array.
4. Check `minFileSizeBytes` — if set too high, it may filter out all files.

### Diff patches not working

1. Make sure `diffPatchUrl` is set (for pattern mirrors) or `fileNamePattern.diff` is set (for json-index mirrors).
2. Verify that diff files actually exist on the server.
3. Check that the `{from}` and `{to}` placeholders in the URL template match how files are named on the server.

---

## FAQ

**Q: Can I use mirrors without the official Hytale source?**
A: The launcher always tries the official source first (priority 0). Mirrors are used as fallback. You cannot disable the official source.

**Q: Do I need to restart the launcher after editing a mirror file?**
A: Yes. Mirror files are read once on startup.

**Q: What happens if two mirrors have the same `id`?**
A: Both will be loaded, which may cause unexpected behavior. Always use unique IDs.

**Q: What happens if two mirrors have the same `priority`?**
A: They will both be loaded. The order between them is determined by file system enumeration order, which is not guaranteed. Use distinct priorities for predictable ordering.

**Q: Can I have mirrors for only one branch?**
A: Yes. If your mirror only hosts release files, the launcher will simply not find versions for pre-release from your mirror and will fall back to other sources.

**Q: What OS names does the launcher use internally?**
A: `linux`, `windows`, `darwin` (for macOS). Use `osMapping` if your server uses different names.

**Q: What architecture names does the launcher use?**
A: `x64`, `arm64`. These are used as-is unless overridden.

**Q: What branch names does the launcher use?**
A: `release`, `pre-release`. Use `branchMapping` if your server uses different names (e.g., `prerelease`).

**Q: Can I change the default mirrors?**
A: Yes. After the first launch, you can edit the generated `.mirror.json` files freely. The launcher won't overwrite them.
