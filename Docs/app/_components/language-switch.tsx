// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

'use client'

import Link from 'next/link'
import { usePathname } from 'next/navigation'
import type { Locale } from '../_dictionaries/types'

type LanguageSwitchProps = Readonly<{
  activeLocale: Locale
  label: string
  languages: Record<Locale, string>
}>

const availableLocales: Locale[] = ['en', 'ru']

export function LanguageSwitch({ activeLocale, label, languages }: LanguageSwitchProps) {
  const pathname = usePathname()

  function getLocalizedPath(locale: Locale): string {
    const segments = pathname.split('/')
    segments[1] = locale
    return segments.join('/') || `/${locale}/docs/`
  }

  function rememberLocale(locale: Locale) {
    localStorage.setItem('hyprism-docs-locale', locale)
    document.cookie = `NEXT_LOCALE=${locale}; max-age=31536000; path=/; SameSite=Lax`
  }

  return (
    <nav className="hyprism-language-switch" aria-label={label}>
      {availableLocales.map(locale => (
        <Link
          key={locale}
          href={getLocalizedPath(locale)}
          hrefLang={locale}
          lang={locale}
          aria-current={locale === activeLocale ? 'page' : undefined}
          aria-label={languages[locale]}
          onClick={() => rememberLocale(locale)}
        >
          {locale.toUpperCase()}
        </Link>
      ))}
    </nav>
  )
}
