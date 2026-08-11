// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

'use client'

import { useEffect } from 'react'
import { useMDXComponents as getMDXComponents } from '../../mdx-components'
import { useDocsLocale } from './locale-context'
import { localizedPages, type MdxPageKey } from './mdx-registry.generated'

type LocalizedMdxPageProps = Readonly<{
  pageKey: MdxPageKey
}>

const Wrapper = getMDXComponents({}).wrapper!

export function LocalizedMdxPage({ pageKey }: LocalizedMdxPageProps) {
  const locale = useDocsLocale()
  const page = localizedPages[locale][pageKey]
  const Content = page.default

  useEffect(() => {
    document.title = `${page.metadata.title} | HyPrism`
  }, [page.metadata.title])

  return (
    <Wrapper toc={page.toc} metadata={page.metadata} sourceCode={page.sourceCode}>
      <Content />
    </Wrapper>
  )
}
