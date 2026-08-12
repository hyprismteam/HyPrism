// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

import Layout from '@theme/Layout'
import TOC from '@theme/TOC'
import React, { type ComponentType, useEffect } from 'react'
import { useDocsLocale } from '../context/locale'
import { routeToUrl, useLocalizedDocsData, type NavigationItem } from '../data'
import { dictionaries, type Locale } from '../i18n'
import DocsSidebar from './DocsSidebar'
import Link from '@docusaurus/Link'

type TocItem = Readonly<{
  value: string
  id: string
  level: number
}>

type MdxContent = ComponentType & Readonly<{
  contentTitle?: string
  frontMatter: Readonly<{
    description?: string
    title?: string
  }>
  metadata: Readonly<{
    description?: string
    title?: string
  }>
  toc: TocItem[]
}>

type LocalizedDocPageProps = Readonly<{
  en: MdxContent
  ru: MdxContent
  pageKey: string
}>

type FlatLink = Readonly<{
  label: string
  route: string
}>

function flattenNavigation(items: NavigationItem[]): FlatLink[] {
  return items.flatMap(item =>
    item.type === 'link'
      ? [{ label: item.label, route: item.route }]
      : flattenNavigation(item.items)
  )
}

function PageNavigation({ pageKey, locale }: Readonly<{
  pageKey: string
  locale: Locale
}>) {
  const { navigation } = useLocalizedDocsData()
  const dictionary = dictionaries[locale]
  const links = flattenNavigation(navigation[locale])
  const index = links.findIndex(link => link.route === pageKey)
  const previous = index > 0 ? links[index - 1] : undefined
  const next = index >= 0 ? links[index + 1] : undefined

  return (
    <nav className="pagination-nav" aria-label="Docs pages navigation">
      {previous ? (
        <Link className="pagination-nav__link pagination-nav__link--prev" to={routeToUrl(previous.route)}>
          <div className="pagination-nav__sublabel">{dictionary.previous}</div>
          <div className="pagination-nav__label">{previous.label}</div>
        </Link>
      ) : <span />}
      {next && (
        <Link className="pagination-nav__link pagination-nav__link--next" to={routeToUrl(next.route)}>
          <div className="pagination-nav__sublabel">{dictionary.next}</div>
          <div className="pagination-nav__label">{next.label}</div>
        </Link>
      )}
    </nav>
  )
}

export default function LocalizedDocPage({ en, ru, pageKey }: LocalizedDocPageProps) {
  const { locale } = useDocsLocale()
  const dictionary = dictionaries[locale]
  const Content = locale === 'ru' ? ru : en
  const title = Content.metadata.title || Content.frontMatter.title || Content.contentTitle || 'HyPrism'
  const description = Content.metadata.description || Content.frontMatter.description
  const sourcePath = pageKey ? `${pageKey}.mdx` : 'index.mdx'
  const editUrl = `https://github.com/hyprismteam/HyPrism/edit/main/Docs/content/${locale}/${sourcePath}`

  useEffect(() => {
    document.title = `${title} | HyPrism`
  }, [title])

  return (
    <Layout title={title} description={description}>
      <div className="hyprism-docs-shell">
        <div className="hyprism-docs-layout">
          <DocsSidebar />
          <main className="hyprism-doc-main">
            <article className="theme-doc-markdown markdown">
              <Content />
            </article>
            <a className="hyprism-edit-link" href={editUrl}>
              {dictionary.editPage}
            </a>
            <PageNavigation pageKey={pageKey} locale={locale} />
          </main>
          {Content.toc.length > 0 && (
            <aside className="hyprism-doc-toc">
              <strong>{dictionary.toc}</strong>
              <TOC toc={Content.toc} minHeadingLevel={2} maxHeadingLevel={3} />
            </aside>
          )}
        </div>
      </div>
    </Layout>
  )
}
