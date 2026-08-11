// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

import type { Metadata } from 'next'
import { Head } from 'nextra/components'
import type { ReactNode } from 'react'
import EnglishDictionary from './_dictionaries/en'
import 'nextra-theme-docs/style.css'
import './styles.css'

const localeBootstrapScript = `try {
  const savedLocale = window.localStorage.getItem('hyprism-docs-locale')
  const locale = savedLocale === 'en' || savedLocale === 'ru'
    ? savedLocale
    : window.navigator.language.toLowerCase().startsWith('ru') ? 'ru' : 'en'
  document.documentElement.dataset.docsLocale = locale
  document.documentElement.dataset.docsReady = locale === 'en' ? 'true' : 'false'
  document.documentElement.lang = locale
} catch {}`

export const metadata: Metadata = {
  title: {
    default: EnglishDictionary.siteTitle,
    template: '%s | HyPrism'
  },
  description: EnglishDictionary.siteDescription
}

export default function RootLayout({ children }: Readonly<{ children: ReactNode }>) {
  return (
    <html
      lang="en"
      dir="ltr"
      data-docs-locale="en"
      data-docs-ready="false"
      suppressHydrationWarning
    >
      <Head>
        <script dangerouslySetInnerHTML={{ __html: localeBootstrapScript }} />
        <meta name="theme-color" content="#0d0f13" />
      </Head>
      <body>{children}</body>
    </html>
  )
}
