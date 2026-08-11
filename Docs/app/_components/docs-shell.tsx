// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

'use client'

import type { PageMapItem } from 'nextra'
import { Search } from 'nextra/components'
import { GitHubIcon } from 'nextra/icons'
import { Footer, LastUpdated, Layout, Navbar } from 'nextra-theme-docs'
import type { ReactNode } from 'react'
import { useEffect, useState } from 'react'
import EnglishDictionary from '../_dictionaries/en'
import RussianDictionary from '../_dictionaries/ru'
import type { Dictionary, Locale } from '../_dictionaries/types'
import { LanguageSwitch } from './language-switch'

type LocalizedPage = Readonly<{
  content: ReactNode
  pageMap: PageMapItem[]
  title: string
}>

type DocsShellProps = Readonly<{
  pages: Record<Locale, LocalizedPage>
}>

const dictionaries: Record<Locale, Dictionary> = {
  en: EnglishDictionary,
  ru: RussianDictionary
}

function getPreferredLocale(): Locale {
  const savedLocale = window.localStorage.getItem('hyprism-docs-locale')
  if (savedLocale === 'en' || savedLocale === 'ru') {
    return savedLocale
  }

  return window.navigator.language.toLowerCase().startsWith('ru') ? 'ru' : 'en'
}

export function DocsShell({ pages }: DocsShellProps) {
  const [locale, setLocale] = useState<Locale>('en')
  const dictionary = dictionaries[locale]
  const page = pages[locale]

  useEffect(() => {
    setLocale(getPreferredLocale())
  }, [])

  useEffect(() => {
    document.documentElement.lang = locale
    document.title = `${page.title} | HyPrism`
  }, [locale, page.title])

  function selectLocale(nextLocale: Locale) {
    window.localStorage.setItem('hyprism-docs-locale', nextLocale)
    setLocale(nextLocale)
  }

  const navbar = (
    <Navbar
      logo={
        <span className="hyprism-logo">
          HyPrism <span>{dictionary.docsLabel}</span>
        </span>
      }
      logoLink="/docs/"
      projectLink="https://github.com/hyprismteam/HyPrism"
      projectIcon={
        <GitHubIcon height="24" aria-label={dictionary.projectRepository} />
      }
    >
      <LanguageSwitch
        activeLocale={locale}
        label={dictionary.languageSwitcher}
        languages={dictionary.languages}
        onChange={selectLocale}
      />
    </Navbar>
  )

  const search = (
    <Search
      placeholder={dictionary.search.placeholder}
      emptyResult={dictionary.search.emptyResult}
      errorText={dictionary.search.errorText}
      loading={dictionary.search.loading}
    />
  )

  return (
    <Layout
      key={locale}
      navbar={navbar}
      pageMap={page.pageMap}
      docsRepositoryBase="https://github.com/hyprismteam/HyPrism/tree/main/Docs"
      footer={<Footer>{dictionary.footer}</Footer>}
      search={search}
      editLink={dictionary.editPage}
      feedback={{ content: dictionary.feedback, labels: 'documentation' }}
      lastUpdated={<LastUpdated locale={locale}>{dictionary.lastUpdated}</LastUpdated>}
      themeSwitch={dictionary.theme}
      toc={{
        title: dictionary.toc.title,
        backToTop: dictionary.toc.backToTop
      }}
      copyPageButton={false}
      sidebar={{ autoCollapse: true, defaultMenuCollapseLevel: 1 }}
    >
      {page.content}
    </Layout>
  )
}
