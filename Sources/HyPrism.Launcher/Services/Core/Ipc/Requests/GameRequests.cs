// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.Services.Core.Ipc.Requests;

public record LaunchGameRequest(string? InstanceId = null, bool? LaunchAfterDownload = null);
public record GetVersionsRequest(string? Branch = null);
public record GetLogsRequest(int? Count = null);
