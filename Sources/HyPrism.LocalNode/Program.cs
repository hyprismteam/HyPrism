// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.LocalNode;

var options = LocalNodeOptions.Parse(args);
var log = new LocalNodeLog(options.DataDirectory, options.LogFilePath);

try
{
    using var certificate = LocalNodeCertificateStore.LoadOrCreate(options);
    using var processLifetime = new LocalNodeProcessLifetime(log, options.OwnerProcessId);
    var app = LocalNodeApplication.Build(
        options,
        certificate,
        log: log,
        processLifetime: processLifetime);
    processLifetime.Start(app.Lifetime);
    log.Info($"Local Node process {Environment.ProcessId} listening on {options.Issuer}");

    await app.RunAsync();
    log.Info("Local Node stopped");
    return 0;
}
catch (Exception exception)
{
    log.Error($"Local Node failed: {exception}");
    return 1;
}
