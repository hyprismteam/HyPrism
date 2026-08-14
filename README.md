<!--
Copyright (C) 2026 HyPrism Launcher
SPDX-License-Identifier: GPL-3.0-only
-->

<img src="https://raw.githubusercontent.com/hyprismteam/HyPrism/refs/heads/main/Sources/HyPrism.Desktop/Assets/Images/logo.png" alt="HyPrism Logo" height="128" />

  *A multiplatform Hytale launcher with mod manager and more!*

  [![Downloads](https://img.shields.io/github/downloads/hyprismteam/HyPrism/total?style=flat&logo=github&label=Downloads&color=2d3748&logoWidth=20)](https://github.com/hyprismteam/HyPrism/releases)
  [![CI](https://img.shields.io/github/actions/workflow/status/hyprismteam/HyPrism/ci.yml?branch=main&style=flat&label=CI&logo=github&logoWidth=20)](https://github.com/hyprismteam/HyPrism/actions/workflows/ci.yml)
  [![Website](https://img.shields.io/badge/Website-hyprism-207e5c?style=flat&logo=google-chrome&logoColor=white&logoWidth=20)](https://hyprismteam.github.io/hyprism-site/)
  [![GitLab](https://img.shields.io/badge/GitLab-yyyumeniku-FC6D26?style=flat&logo=gitlab&logoColor=white&logoWidth=20)](https://gitlab.com/yyyumeniku/HyPrism)
  [![Discord](https://img.shields.io/badge/Discord-Join-5865F2?style=flat&logo=discord&logoColor=white&logoWidth=20)](https://discord.com/invite/ekZqTtynjp)
  [![Buy Me a Coffee](https://img.shields.io/badge/Buy%20Me%20a%20Coffee-Support-FFDD00?style=flat&logo=buy-me-a-coffee&logoColor=black&logoWidth=20)](https://buymeacoffee.com/yyyumeniku)

> Disclaimer: HyPrism has no any connection to [PrismLauncher](https://github.com/PrismLauncher/PrismLauncher). HyPrism is an application that is being developed **INDEPENDENTLY** of the PrismLauncher project or its team. Thank you for your understanding

## Installation

Downloads are available in [Releases](https://github.com/hyprismteam/HyPrism/releases)

> We are also here! https://gitlab.com/yyyumeniku/HyPrism
> 
> And here: https://git.sanhost.net/HyprismTeam/HyPrism

## Build Instructions

**Requirements:**

- .NET 10.0 SDK
- Node.js 22 and pnpm when working on the documentation

**Build:**

```bash
# Clone the repository
git clone https://github.com/hyprismteam/HyPrism.git
cd HyPrism

# Build the application and tests
dotnet build HyPrism.sln

# Run the launcher
dotnet run --project Sources/HyPrism.Desktop/HyPrism.Desktop.csproj
```

HyPrism uses a .NET 10 Core library and a native Avalonia 12 desktop application

## Docs

The English and Russian documentation is available on the [HyPrism documentation site](https://hyprismteam.github.io/HyPrism/docs/). Its Docusaurus sources live in [`Docs/content`](Docs/content)

## Credits & Contributors

Special thanks to **Sanasol** for maintaining and creating the [auth server](https://github.com/sanasol/hytale-auth-server)

<a href="https://github.com/hyprismteam/HyPrism/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=hyprismteam/HyPrism" alt="Contributors" />
</a>

## Donate

We support the launcher **solely with our free time** and **community feedback**. Financial support will help us continue active development in the world of Hytale!

- BuyMeACoffe [(Click)](https://buymeacoffee.com/yyyumeniku)
- DonationAlerts [(Click)](https://www.donationalerts.com/r/danielfreak)

## Legal Notice & Licenses

HyPrism is licensed under the **GNU General Public License v3.0 (GPL-3.0)**
- Full license text: [LICENSE](LICENSE)
- License texts used by project assets: [Licenses](Licenses)
- Machine-readable licensing and CI compliance: [REUSE.toml](REUSE.toml)

### Unofficial Product
**HyPrism** is an unofficial, open-source launcher for Hytale. This project is **not** affiliated with, endorsed by, sponsored by, or approved by **Hypixel Studios**, **Riot Games**, or any of their affiliates

### Intellectual Property
"Hytale", "Hypixel Studios", and strictly related logos and assets are trademarks or registered trademarks of **Hypixel Studios Inc**. All rights reserved by their respective owners. usage of this software is subject to the terms of the [Hytale End-User License Agreement (EULA)](https://hytale.com/eula)

### Game Files & Distribution

**HyPrism** acts solely as a client-side tool to facilitate the downloading and launching process. It does not host, modify, or distribute original game files directly

1. **Official Authentication (Default):** By default, HyPrism implements the standard Hytale authentication protocol. Users with a valid license can download unmodified game images directly from official servers, identical to the official launcher process
2. **Custom Third-Party Mirrors (User-Configured):** HyPrism does not include, provide, or pre-configure any third-party download mirrors. The software merely provides a technical capability for users to manually add custom mirror URLs at their own discretion

The developers of HyPrism constitute no control over the content hosted on any user-added external mirrors. The responsibility for choosing a download source and ensuring compliance with the [Hytale EULA](https://hytale.com/eula) and applicable copyright laws rests entirely with the end-user

### Third-Party Components

HyPrism depends on third-party libraries distributed under licenses including MIT, Apache-2.0, BSD-style licenses, and CC-BY-4.0

<div align="center">
  <sub>Made with ❤️ by the HyPrism Community</sub>
</div>
