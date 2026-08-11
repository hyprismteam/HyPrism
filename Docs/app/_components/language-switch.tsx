// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

'use client'

import type { Locale } from '../_dictionaries/types'

type LanguageSwitchProps = Readonly<{
  activeLocale: Locale
  label: string
  languages: Record<Locale, string>
  onChange: (locale: Locale) => void
}>

const availableLocales: Locale[] = ['en', 'ru']

export function LanguageSwitch({
  activeLocale,
  label,
  languages,
  onChange
}: LanguageSwitchProps) {
  return (
    <nav className="hyprism-language-switch" aria-label={label}>
      {availableLocales.map(locale => (
        <button
          key={locale}
          type="button"
          aria-current={locale === activeLocale ? 'page' : undefined}
          aria-pressed={locale === activeLocale}
          aria-label={languages[locale]}
          onClick={() => onChange(locale)}
        >
          {locale.toUpperCase()}
        </button>
      ))}
    </nav>
  )
}
