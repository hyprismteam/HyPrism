// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

import React, { createContext, useContext } from 'react'
import type { Locale } from '../i18n'

type LocaleContextValue = Readonly<{
  locale: Locale
  selectLocale: (locale: Locale) => void
}>

export const LocaleContext = createContext<LocaleContextValue>({
  locale: 'en',
  selectLocale: () => undefined
})

export function useDocsLocale(): LocaleContextValue {
  return useContext(LocaleContext)
}
