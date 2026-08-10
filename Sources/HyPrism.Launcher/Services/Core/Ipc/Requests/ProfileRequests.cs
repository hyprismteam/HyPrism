// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.Services.Core.Ipc.Requests;

public record CreateProfileRequest(string Name, string Uuid, bool? IsOfficial = null);
public record SwitchProfileRequest(string Id);
