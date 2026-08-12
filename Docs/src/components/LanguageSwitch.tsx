// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

import React from 'react'
import { useDocsLocale } from '../context/locale'
import { dictionaries, locales } from '../i18n'

export default function LanguageSwitch() {
  const { locale: activeLocale, selectLocale } = useDocsLocale()
  const dictionary = dictionaries[activeLocale]

  return (
    <nav className="hyprism-language-switch" aria-label={dictionary.languageSwitcher}>
      {locales.map(locale => (
        <button
          key={locale}
          type="button"
          aria-current={locale === activeLocale ? 'page' : undefined}
          aria-pressed={locale === activeLocale}
          aria-label={dictionary.languages[locale]}
          onClick={() => selectLocale(locale)}
        >
          {locale.toUpperCase()}
        </button>
      ))}
    </nav>
  )
}
