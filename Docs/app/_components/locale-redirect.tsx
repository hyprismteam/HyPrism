// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

'use client'

import { useRouter } from 'next/navigation'
import { useEffect } from 'react'

export function LocaleRedirect() {
  const router = useRouter()

  useEffect(() => {
    router.replace('/docs/')
  }, [router])

  return (
    <main className="hyprism-locale-entry">
      <div>
        <span className="hyprism-logo">
          HyPrism <span>Docs</span>
        </span>
        <h1>Opening documentation · Открываем документацию</h1>
      </div>
    </main>
  )
}
