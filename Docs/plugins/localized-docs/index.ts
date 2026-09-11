// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

import type { LoadContext, Plugin } from '@docusaurus/types'
import matter from 'gray-matter'
import { promises as fs } from 'node:fs'
import path from 'node:path'

const locales = ['en', 'ru'] as const

type Locale = (typeof locales)[number]

type Page = Readonly<{
  description: string
  position: number
  route: string
  source: string
  title: string
}>

type NavigationItem =
  | Readonly<{
      type: 'link'
      label: string
      route: string
      position: number
    }>
  | Readonly<{
      type: 'category'
      label: string
      icon?: string
      position: number
      items: NavigationItem[]
    }>

type LocalizedDocsContent = Readonly<{
  navigation: Record<Locale, NavigationItem[]>
  pages: Record<Locale, Record<string, Page>>
  search: Record<Locale, SearchEntry[]>
}>

type SearchEntry = Readonly<{
  description: string
  route: string
  text: string
  title: string
}>

function routeFor(filePath: string, contentPath: string): string {
  const relative = path.relative(contentPath, filePath).replace(/\.mdx?$/, '')
  const segments = relative.split(path.sep)
  if (segments.at(-1) === 'index') {
    segments.pop()
  }
  return segments.join('/')
}

function asPosition(value: unknown): number {
  return typeof value === 'number' ? value : Number.MAX_SAFE_INTEGER
}

function pageTitle(frontMatter: Record<string, unknown>, filePath: string): string {
  if (typeof frontMatter.title === 'string') {
    return frontMatter.title
  }
  return path.basename(filePath).replace(/\.mdx?$/, '').replaceAll('-', ' ')
}

function plainText(markdown: string): string {
  return markdown
    .replace(/```[\s\S]*?```/g, ' ')
    .replace(/`([^`]+)`/g, '$1')
    .replace(/!\[[^\]]*\]\([^)]*\)/g, ' ')
    .replace(/\[([^\]]+)\]\([^)]*\)/g, '$1')
    .replace(/<[^>]+>/g, ' ')
    .replace(/[#>*_|~-]/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
}

async function readNavigationMetadata(directory: string): Promise<{
  labels: Map<string, string>
  icons: Map<string, string>
}> {
  const metadataPath = path.join(directory, '_meta.ts')
  const result: { labels: Map<string, string>; icons: Map<string, string> } = {
    labels: new Map(),
    icons: new Map()
  }
  try {
    const source = await fs.readFile(metadataPath, 'utf8')
    const entryPattern = /^\s*(?:'([^']+)'|([A-Za-z0-9_-]+)):\s*'([^']+)'/gm
    for (const match of source.matchAll(entryPattern)) {
      const key = match[1] || match[2]
      if (key !== 'icon') result.labels.set(key, match[3])
    }
    const iconPattern = /^\s*icon:\s*'([^']+)'/gm
    for (const match of source.matchAll(iconPattern)) {
      result.icons.set('icon', match[1])
    }
    return result
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code === 'ENOENT') {
      return result
    }
    throw error
  }
}

async function collectFiles(directory: string): Promise<string[]> {
  const entries = await fs.readdir(directory, { withFileTypes: true })
  const nested = await Promise.all(
    entries.map(async entry => {
      const entryPath = path.join(directory, entry.name)
      if (entry.isDirectory()) {
        return collectFiles(entryPath)
      }
      return /\.mdx?$/.test(entry.name) ? [entryPath] : []
    })
  )
  return nested.flat().sort()
}

async function collectPages(contentPath: string): Promise<{
  pages: Record<string, Page>
  search: SearchEntry[]
}> {
  const pages: Record<string, Page> = {}
  const search: SearchEntry[] = []

  for (const source of await collectFiles(contentPath)) {
    const raw = await fs.readFile(source, 'utf8')
    const parsed = matter(raw)
    const route = routeFor(source, contentPath)
    const title = pageTitle(parsed.data, source)
    const description = typeof parsed.data.description === 'string'
      ? parsed.data.description
      : ''

    if (pages[route]) {
      throw new Error(`Documentation route ${JSON.stringify(route)} is declared twice`)
    }

    pages[route] = {
      description,
      position: asPosition(parsed.data.sidebar_position),
      route,
      source,
      title
    }
    search.push({
      description,
      route,
      text: plainText(parsed.content),
      title
    })
  }

  return { pages, search }
}

function sortNavigation(items: NavigationItem[]): NavigationItem[] {
  return items.sort((left, right) =>
    left.position - right.position || left.label.localeCompare(right.label)
  )
}

async function buildNavigation(
  directory: string,
  contentPath: string,
  pages: Record<string, Page>
): Promise<NavigationItem[]> {
  const entries = await fs.readdir(directory, { withFileTypes: true })
  const metadata = await readNavigationMetadata(directory)
  const items: NavigationItem[] = []

  for (const entry of entries) {
    const entryPath = path.join(directory, entry.name)
    if (entry.isDirectory()) {
      const nestedItems = await buildNavigation(entryPath, contentPath, pages)
      const childMetadata = await readNavigationMetadata(entryPath)
      if (nestedItems.length > 0) {
        items.push({
          type: 'category',
          label: metadata.labels.get(entry.name) || entry.name.replaceAll('-', ' '),
          icon: childMetadata.icons.get('icon'),
          position: metadata.labels.has(entry.name)
            ? [...metadata.labels.keys()].indexOf(entry.name)
            : Number.MAX_SAFE_INTEGER,
          items: nestedItems
        })
      }
      continue
    }

    if (!/\.mdx?$/.test(entry.name)) {
      continue
    }

    const route = routeFor(entryPath, contentPath)
    const page = pages[route]
    const name = entry.name.replace(/\.mdx?$/, '')
    items.push({
      type: 'link',
      label: metadata.labels.get(name) || page.title,
      route,
      position: metadata.labels.has(name)
        ? [...metadata.labels.keys()].indexOf(name)
        : page.position
    })
  }

  return sortNavigation(items)
}

function assertParity(pages: Record<Locale, Record<string, Page>>): void {
  const englishRoutes = new Set(Object.keys(pages.en))
  const russianRoutes = new Set(Object.keys(pages.ru))
  const missingRussian = [...englishRoutes].filter(route => !pages.ru[route])
  const missingEnglish = [...russianRoutes].filter(route => !pages.en[route])

  if (missingRussian.length > 0 || missingEnglish.length > 0) {
    const errors = [
      ...missingRussian.map(route => `Russian page is missing for route ${JSON.stringify(route)}`),
      ...missingEnglish.map(route => `English page is missing for route ${JSON.stringify(route)}`)
    ]
    throw new Error(errors.join('\n'))
  }
}

function publicRoute(route: string): string {
  return route ? `/docs/${route}/` : '/docs/'
}

export default async function localizedDocsPlugin(
  context: LoadContext
): Promise<Plugin<LocalizedDocsContent>> {
  const contentRoot = path.join(context.siteDir, 'content')

  return {
    name: 'hyprism-localized-docs',
    getPathsToWatch() {
      return locales.map(locale => path.join(contentRoot, locale, '**', '*.{md,mdx,ts}'))
    },
    async loadContent() {
      const collected = await Promise.all(
        locales.map(async locale => {
          const contentPath = path.join(contentRoot, locale)
          const result = await collectPages(contentPath)
          return {
            locale,
            ...result,
            navigation: await buildNavigation(contentPath, contentPath, result.pages)
          }
        })
      )
      const pages = Object.fromEntries(
        collected.map(result => [result.locale, result.pages])
      ) as Record<Locale, Record<string, Page>>
      assertParity(pages)

      return {
        pages,
        navigation: Object.fromEntries(
          collected.map(result => [result.locale, result.navigation])
        ) as Record<Locale, NavigationItem[]>,
        search: Object.fromEntries(
          collected.map(result => [result.locale, result.search])
        ) as Record<Locale, SearchEntry[]>
      }
    },
    async contentLoaded({ content, actions }) {
      actions.setGlobalData({
        navigation: content.navigation,
        search: content.search
      })

      for (const route of Object.keys(content.pages.en)) {
        actions.addRoute({
          path: `${context.baseUrl.replace(/\/$/, '')}${publicRoute(route)}`,
          exact: true,
          component: '@site/src/components/LocalizedDocPage',
          modules: {
            en: content.pages.en[route].source,
            ru: content.pages.ru[route].source
          },
          props: {
            pageKey: route
          }
        })
      }

      actions.addRoute({
        path: context.baseUrl,
        exact: true,
        component: '@site/src/components/HomeRedirect'
      })
    },
    async postBuild({ outDir }) {
      await fs.rm(path.join(outDir, '__source'), { recursive: true, force: true })
    }
  }
}
