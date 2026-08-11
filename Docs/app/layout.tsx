// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

import type { Metadata } from 'next'
import { Head } from 'nextra/components'
import type { ReactNode } from 'react'
import EnglishDictionary from './_dictionaries/en'
import 'nextra-theme-docs/style.css'
import './styles.css'

export const metadata: Metadata = {
  title: {
    default: EnglishDictionary.siteTitle,
    template: '%s | HyPrism'
  },
  description: EnglishDictionary.siteDescription
}

export default function RootLayout({ children }: Readonly<{ children: ReactNode }>) {
  return (
    <html lang="en" dir="ltr" suppressHydrationWarning>
      <Head>
        <meta name="theme-color" content="#0d0f13" />
      </Head>
      <body>{children}</body>
    </html>
  )
}
