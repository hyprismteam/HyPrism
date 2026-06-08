'use strict';

/**
 * ElectronNET startup hook — VPN-compatible socket bridge
 *
 * This file is loaded by main.js via the ElectronNET.Core custom_main.js hook
 * mechanism before app.on('ready') fires, so the patch is in the Node.js module
 * cache when startSocketApiBridge() calls require('http').createServer()
 *
 * Security Model:
 * - Windows: Bind to 0.0.0.0 (all interfaces) for VPN compatibility
 * - Linux/macOS: Bind to 127.0.0.1 (localhost only)
 * - All platforms: Filter connections to only accept loopback addresses
 *
 * Environment Variables:
 * - HYPRISM_VPN_COMPAT=0: Disable VPN compatibility (force 127.0.0.1)
 * - HYPRISM_VPN_COMPAT=1: Force VPN compatibility mode (0.0.0.0)
 */
module.exports = {
    onStartup(_host) {
        const { app } = require('electron');
        app.setName('HyPrism');
        process.title = 'HyPrism';
        if (process.platform === 'win32') {
            app.setAppUserModelId('io.github.hyprismteam.HyPrism');
        }

        const http = require('http');
        const _origCreate = http.createServer;

        // Check environment variable for explicit override
        const vpnCompatEnv = process.env.HYPRISM_VPN_COMPAT;
        const isWindows = process.platform === 'win32';

        // Default: Windows uses 0.0.0.0, others use 127.0.0.1
        // User can override with HYPRISM_VPN_COMPAT=0 (disable) or =1 (force enable)
        const vpnCompatMode = vpnCompatEnv === '1' || (isWindows && vpnCompatEnv !== '0');

        http.createServer = function (...args) {
            const server = _origCreate.apply(http, args);
            const _origListen = server.listen.bind(server);

            server.listen = function (port, host, ...rest) {
                const originalHost = host;

                // Apply VPN compatibility mode for loopback hosts
                if (vpnCompatMode && (host === 'localhost' || host === '127.0.0.1' || host === '::1')) {
                    host = '0.0.0.0';
                    console.log(`[HyPrism] VPN compatibility: binding on ${host} (original: ${originalHost})`);
                }

                return _origListen.call(server, port, host, ...rest);
            };

            // Security: Filter incoming connections (always active)
            const _origOn = server.on.bind(server);
            server.on = function (event, listener) {
                if (event === 'connection') {
                    const originalListener = listener;
                    listener = function (socket) {
                        const remoteAddr = socket.remoteAddress;

                        // Only allow loopback connections
                        const isLoopback =
                            remoteAddr === '127.0.0.1' ||
                            remoteAddr === '::1' ||
                            remoteAddr === '::ffff:127.0.0.1' ||
                            remoteAddr?.startsWith('::ffff:7f00:');

                        if (!isLoopback) {
                            console.log(`[HyPrism] Blocked non-loopback connection from: ${remoteAddr}`);
                            socket.destroy();
                            return;
                        }

                        return originalListener.call(this, socket);
                    };
                }
                return _origOn.call(this, event, listener);
            };

            return server;
        };
    },
};
