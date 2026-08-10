// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

/** JVM garbage collector mode. `"auto"` lets the launcher choose; `"g1"` forces G1GC. */
export type GcMode = 'auto' | 'g1';

/** Java runtime selection mode: use the launcher-bundled JRE or a user-provided one. */
export type RuntimeMode = 'bundled' | 'custom';
