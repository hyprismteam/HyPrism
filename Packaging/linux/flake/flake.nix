# Copyright (C) 2026 HyPrism Launcher
# SPDX-License-Identifier: GPL-3.0-only

{
  description = "HyPrism, a native Hytale launcher";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs =
    { self, nixpkgs }:
    let
      systems = [ "x86_64-linux" ];
      forAllSystems = nixpkgs.lib.genAttrs systems;
      versionSource = builtins.replaceStrings [ "\n" "\r" " " "\t" ] [ "" "" "" "" ] (
        builtins.readFile (../../.. + "/Sources/HyPrism.Desktop/HyPrism.Desktop.csproj")
      );
      versionMatch = builtins.match ".*<Version>([0-9A-Za-z.+-]+)</Version>.*" versionSource;
      version =
        if versionMatch == null then
          throw "Unable to read the HyPrism version from HyPrism.Desktop.csproj"
        else
          builtins.elemAt versionMatch 0;
    in
    {
      packages = forAllSystems (
        system:
        let
          pkgs = import nixpkgs { inherit system; };
          inherit (pkgs) lib;
          runtimeLibraries = with pkgs; [
            expat
            fontconfig
            freetype
            glib
            icu
            libglvnd
            libice
            libsm
            libx11
            libxcomposite
            libxcursor
            libxext
            libxi
            libxkbcommon
            libxrandr
            libxtst
            openssl
            wayland
            zlib
          ];
          hyprism = pkgs.buildDotnetModule {
            pname = "hyprism";
            inherit version;
            src = ../../..;

            projectFile = "Sources/HyPrism.Desktop/HyPrism.Desktop.csproj";
            nugetDeps = ./nix/deps.json;
            runtimeId = "linux-x64";
            dotnet-sdk = pkgs.dotnetCorePackages.sdk_10_0;
            dotnet-runtime = pkgs.dotnetCorePackages.runtime_10_0;
            executables = [ "HyPrism.Desktop" ];

            nativeBuildInputs = [ pkgs.autoPatchelfHook ];
            buildInputs = runtimeLibraries;
            runtimeDeps = runtimeLibraries;

            postInstall = ''
              install -d "$out/bin"
              ln -s HyPrism.Desktop "$out/bin/hyprism"
              install -Dm644 Packaging/linux/io.github.hyprismteam.HyPrism.desktop \
                "$out/share/applications/io.github.hyprismteam.HyPrism.desktop"
              install -Dm644 Sources/HyPrism.Desktop/Assets/Images/appicon_512.png \
                "$out/share/icons/hicolor/512x512/apps/io.github.hyprismteam.HyPrism.png"
            '';

            meta = {
              homepage = "https://github.com/hyprismteam/HyPrism";
              description = "Native Avalonia launcher for Hytale";
              license = lib.licenses.gpl3Only;
              mainProgram = "hyprism";
              platforms = [ "x86_64-linux" ];
            };
          };
        in
        {
          default = hyprism;
          inherit hyprism;
        }
      );

      apps = forAllSystems (
        system:
        let
          package = self.packages.${system}.hyprism;
        in
        {
          default = {
            type = "app";
            program = "${package}/bin/hyprism";
          };
          hyprism = {
            type = "app";
            program = "${package}/bin/hyprism";
          };
        }
      );

      checks = forAllSystems (system: {
        hyprism = self.packages.${system}.hyprism;
      });

      formatter = forAllSystems (system: nixpkgs.legacyPackages.${system}.nixfmt-rfc-style);
    };
}
