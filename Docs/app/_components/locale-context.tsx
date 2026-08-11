// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

'use client'

import { createContext, useContext } from 'react'
import type { Locale } from '../_dictionaries/types'

export const localeChangeEvent = 'hyprism-docs-locale-change'
export const DocsLocaleContext = createContext<Locale>('en')

export function useDocsLocale() {
  return useContext(DocsLocaleContext)
}
