// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

import { ipc } from '@/lib/ipc';

/**
 * Open a URL in the system browser via the backend IPC bridge.
 *
 * Replaces the pattern: `const BrowserOpenURL = (url: string) => ipc.browser.open(url);`
 * that was duplicated across SettingsModal, InlineModBrowser, and other files.
 * @param url - The URL to open in the default system browser.
 * @returns void
 */
export const openUrl = (url: string): void => {
  ipc.browser.open(url);
};
