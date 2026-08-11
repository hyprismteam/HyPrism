// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

import type { PageMapItem } from 'nextra'
import { getPageMap } from 'nextra/page-map'
import type { ReactNode } from 'react'
import { DocsShell } from '../_components/docs-shell'

function removeLocaleFromPageMap(pageMap: PageMapItem[], locale: string): PageMapItem[] {
  return pageMap.map(item => {
    if (!('route' in item)) {
      return item
    }

    const normalizedItem = {
      ...item,
      route: item.route.replace(new RegExp(`^/${locale}(?=/)`), '')
    }

    if ('children' in item) {
      return {
        ...normalizedItem,
        children: removeLocaleFromPageMap(item.children, locale)
      }
    }

    return normalizedItem
  })
}

function getDocsPageMap(pageMap: PageMapItem[], locale: string): PageMapItem[] {
  const normalizedPageMap = removeLocaleFromPageMap(pageMap, locale)
  const docsFolder = normalizedPageMap.find(
    item => 'children' in item && item.route === '/docs'
  )

  return docsFolder && 'children' in docsFolder
    ? docsFolder.children
    : normalizedPageMap
}

export default async function DocsLayout({ children }: Readonly<{ children: ReactNode }>) {
  const [englishPageMap, russianPageMap] = await Promise.all([
    getPageMap('/en'),
    getPageMap('/ru')
  ])

  return (
    <DocsShell
      pageMaps={{
        en: getDocsPageMap(englishPageMap, 'en'),
        ru: getDocsPageMap(russianPageMap, 'ru')
      }}
    >
      {children}
    </DocsShell>
  )
}
