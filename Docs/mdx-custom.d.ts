// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

declare module '*.mdx' {
  import type { $NextraMetadata, Heading } from 'nextra'

  export const metadata: $NextraMetadata
  export const sourceCode: string
  export const toc: Heading[]
}
