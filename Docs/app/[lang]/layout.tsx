// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

import type { Metadata } from 'next'
import { notFound } from 'next/navigation'
import type { PageMapItem } from 'nextra'
import { Head, Search } from 'nextra/components'
import { getPageMap } from 'nextra/page-map'
import { GitHubIcon } from 'nextra/icons'
import { Footer, LastUpdated, Layout, Navbar } from 'nextra-theme-docs'
import type { ReactNode } from 'react'
import { LanguageSwitch } from '../_components/language-switch'
import { getDictionary } from '../_dictionaries/get-dictionary'
import { isLocale, locales } from '../_dictionaries/types'
import 'nextra-theme-docs/style.css'
import '../styles.css'

type LocaleLayoutProps = Readonly<{
  children: ReactNode
  params: Promise<{ lang: string }>
}>

function localizePageMap(pageMap: PageMapItem[], lang: string): PageMapItem[] {
  return pageMap
    .filter(item => !('route' in item) || item.route !== '/')
    .map(item => {
      if (!('route' in item)) {
        return item
      }

      const localizedItem = {
        ...item,
        route: `/${lang}${item.route}`
      }

      if ('children' in item) {
        return {
          ...localizedItem,
          children: localizePageMap(item.children, lang)
        }
      }

      return localizedItem
    })
}

export function generateStaticParams() {
  return locales.map(lang => ({ lang }))
}

export async function generateMetadata({ params }: LocaleLayoutProps): Promise<Metadata> {
  const { lang } = await params
  const dictionary = await getDictionary(lang)

  return {
    title: {
      default: dictionary.siteTitle,
      template: `%s | HyPrism`
    },
    description: dictionary.siteDescription
  }
}

export default async function LocaleLayout({ children, params }: LocaleLayoutProps) {
  const { lang } = await params
  if (!isLocale(lang)) {
    notFound()
  }

  const dictionary = await getDictionary(lang)
  const pageMap = localizePageMap(await getPageMap(`/${lang}`), lang)
  const navbar = (
    <Navbar
      logo={
        <span className="hyprism-logo">
          HyPrism <span>{dictionary.docsLabel}</span>
        </span>
      }
      logoLink={`/${lang}/docs/`}
      projectLink="https://github.com/hyprismteam/HyPrism"
      projectIcon={
        <GitHubIcon height="24" aria-label={dictionary.projectRepository} />
      }
    >
      <LanguageSwitch
        activeLocale={lang}
        label={dictionary.languageSwitcher}
        languages={dictionary.languages}
      />
    </Navbar>
  )
  const footer = <Footer>{dictionary.footer}</Footer>
  const search = (
    <Search
      placeholder={dictionary.search.placeholder}
      emptyResult={dictionary.search.emptyResult}
      errorText={dictionary.search.errorText}
      loading={dictionary.search.loading}
    />
  )

  return (
    <html lang={lang} dir="ltr" suppressHydrationWarning>
      <Head>
        <meta name="theme-color" content="#0d0f13" />
      </Head>
      <body>
        <Layout
          navbar={navbar}
          pageMap={pageMap}
          docsRepositoryBase="https://github.com/hyprismteam/HyPrism/tree/main/Docs"
          footer={footer}
          search={search}
          editLink={dictionary.editPage}
          feedback={{ content: dictionary.feedback, labels: 'documentation' }}
          lastUpdated={
            <LastUpdated locale={lang}>{dictionary.lastUpdated}</LastUpdated>
          }
          themeSwitch={dictionary.theme}
          toc={{
            title: dictionary.toc.title,
            backToTop: dictionary.toc.backToTop
          }}
          copyPageButton={false}
          sidebar={{ autoCollapse: true, defaultMenuCollapseLevel: 1 }}
        >
          {children}
        </Layout>
      </body>
    </html>
  )
}
