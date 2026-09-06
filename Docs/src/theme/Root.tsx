// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

import React, { type ReactNode, useCallback, useEffect, useState } from 'react'
import { LocaleContext } from '../context/locale'
import type { Locale } from '../i18n'

function preferredLocale(): Locale {
  const storedLocale = window.localStorage.getItem('hyprism-docs-locale')
  if (storedLocale === 'en' || storedLocale === 'ru') {
    return storedLocale
  }
  return window.navigator.language.toLowerCase().startsWith('ru') ? 'ru' : 'en'
}

export default function Root({ children }: Readonly<{ children: ReactNode }>) {
  const [locale, setLocale] = useState<Locale>('en')

  useEffect(() => {
    setLocale(preferredLocale())
  }, [])

  useEffect(() => {
    if (document.documentElement.dataset.docsLocale === locale) {
      document.documentElement.lang = locale
      document.documentElement.dataset.docsReady = 'true'
    }
  }, [locale])

  const selectLocale = useCallback((nextLocale: Locale) => {
    setLocale(currentLocale => {
      if (currentLocale === nextLocale) {
        return currentLocale
      }
      document.documentElement.dataset.docsReady = 'false'
      document.documentElement.dataset.docsLocale = nextLocale
      document.documentElement.lang = nextLocale
      window.localStorage.setItem('hyprism-docs-locale', nextLocale)
      return nextLocale
    })
  }, [])

  return (
    <LocaleContext.Provider value={{ locale, selectLocale }}>
      {children}
    </LocaleContext.Provider>
  )
}
