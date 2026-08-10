<!--
Copyright (C) 2026 HyPrism Launcher
SPDX-License-Identifier: GPL-3.0-only
-->

This README explains the *generated* Flathub manifest — the manifest is created at CI/runtime and should not be edited or checked in to the repository. Use `Properties/linux/flatpak/io.github.hyprismteam.HyPrism.yml` as the source of truth.

The difference between this manifest and the one located in `Properties/linux/flatpak` is that we cannot build Electron on the Flathub repository; we build here and publish a `flathub-bundle.tar.gz` release artifact which the Flathub manifest references.

## Dotnet extension handling

The helper script that regenerates the manifest (`Scripts/update-flathub-manifest.sh`) also calls `flatpak-dotnet-generator.py` to create a NuGet sources file. The Python tool expects a *version number* (e.g. `10`), but the manifest stores the full extension ID (`org.freedesktop.Sdk.Extension.dotnet10`). A bug in older versions of the script caused the entire ID to be passed through, producing an invalid flatpak invocation like:

```
flatpak run org.freedesktop.Sdk.Extension.dotnetorg.freedesktop.Sdk.Extension.dotnet10//25.08
```

which triggers an error such as:

```
error: app/org.freedesktop.Sdk.Extension.dotnetorg.freedesktop.Sdk.Extension.dotnet10/x86_64/24.08 not installed
```

The updated scripts now strip the prefix and normalize the argument; if you ever see this message while running the generator, make sure `--dotnet` is just the numeric version or update the script accordingly.

You can also manually install the required extension on your machine with:

```sh
flatpak install flathub org.freedesktop.Sdk.Extension.dotnet10//25.08
```

which will satisfy the runtime dependency when testing the manifest locally.

## NuGet sources and runtime packages

The `update-flathub-manifest.sh` helper populates both `nuget-sources.json` and
`generated-sources.json` when regenerating the Flathub manifest.  `nuget-sources.json`
contains the offline NuGet feed configuration for the .NET build, while
`generated-sources.json` tracks the offline Node sources needed by the
`HyPrism-node` module.

The script restores the project inside a Flatpak container and writes the
offline feed configuration accordingly.  Builds inside the Flathub sandbox
restrict `dotnet restore` to this offline feed.  The upstream tool does not
include the Microsoft runtime packages (e.g. `Microsoft.NETCore.App.Runtime.linux-x64`) because
those are provided by the SDK itself, but the offline build still needs them.

To avoid `NU1101` errors, the script now detects the installed runtime version and
appends entries for the three runtime packages (including crossgen) to the
sources file.  You should never need to edit `nuget-sources.json` manually;
regenerating the manifest with the script will handle everything automatically.
