// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

import { useMDXComponents as getThemeComponents } from 'nextra-theme-docs'
import type { MDXComponents } from 'mdx/types'

const themeComponents = getThemeComponents()

export function useMDXComponents(components: MDXComponents): MDXComponents {
  return {
    ...themeComponents,
    ...components
  }
}
