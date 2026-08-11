// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

'use client'

import type { Locale } from '../_dictionaries/types'
import { localeChangeEvent } from './locale-context'

type LanguageSwitchProps = Readonly<{
  activeLocale: Locale
  label: string
  languages: Record<Locale, string>
}>

const availableLocales: Locale[] = ['en', 'ru']

export function LanguageSwitch({
  activeLocale,
  label,
  languages
}: LanguageSwitchProps) {
  function selectLocale(locale: Locale) {
    if (locale === activeLocale) {
      return
    }

    window.localStorage.setItem('hyprism-docs-locale', locale)
    document.documentElement.dataset.docsReady = 'false'
    document.documentElement.dataset.docsLocale = locale
    document.documentElement.lang = locale
    window.dispatchEvent(new CustomEvent(localeChangeEvent, { detail: locale }))
  }

  return (
    <nav className="hyprism-language-switch" aria-label={label}>
      {availableLocales.map(locale => (
        <button
          key={locale}
          type="button"
          aria-current={locale === activeLocale ? 'page' : undefined}
          aria-pressed={locale === activeLocale}
          aria-label={languages[locale]}
          onClick={() => selectLocale(locale)}
        >
          {locale.toUpperCase()}
        </button>
      ))}
    </nav>
  )
}
