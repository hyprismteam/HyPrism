// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

'use client'

import Link from 'next/link'
import { useRouter } from 'next/navigation'
import { useEffect } from 'react'
import type { Locale } from '../_dictionaries/types'

function getPreferredLocale(): Locale {
  const savedLocale = localStorage.getItem('hyprism-docs-locale')
  if (savedLocale === 'en' || savedLocale === 'ru') {
    return savedLocale
  }

  return navigator.language.toLowerCase().startsWith('ru') ? 'ru' : 'en'
}

export function LocaleRedirect() {
  const router = useRouter()

  useEffect(() => {
    router.replace(`/${getPreferredLocale()}/docs/`)
  }, [router])

  return (
    <main className="hyprism-locale-entry">
      <div>
        <span className="hyprism-logo">
          HyPrism <span>Docs</span>
        </span>
        <h1>Choose a language · Выберите язык</h1>
        <nav aria-label="Documentation language · Язык документации">
          <Link href="/en/docs/" hrefLang="en" lang="en">
            English
          </Link>
          <Link href="/ru/docs/" hrefLang="ru" lang="ru">
            Русский
          </Link>
        </nav>
      </div>
    </main>
  )
}
