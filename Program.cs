// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.Dev;

internal static class DevLauncher
{
    [STAThread]
    public static void Main(string[] args) => Desktop.Program.Main(args);
}
