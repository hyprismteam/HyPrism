// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

import { usePluginData } from '@docusaurus/useGlobalData'
import type { Locale } from './i18n'

export type NavigationItem =
  | Readonly<{
      type: 'link'
      label: string
      route: string
      position: number
    }>
  | Readonly<{
      type: 'category'
      label: string
      position: number
      items: NavigationItem[]
    }>

export type SearchEntry = Readonly<{
  description: string
  route: string
  text: string
  title: string
}>

type LocalizedDocsData = Readonly<{
  navigation: Record<Locale, NavigationItem[]>
  search: Record<Locale, SearchEntry[]>
}>

export function useLocalizedDocsData(): LocalizedDocsData {
  return usePluginData('hyprism-localized-docs') as LocalizedDocsData
}

export function routeToUrl(route: string): string {
  return route ? `/docs/${route}/` : '/docs/'
}
