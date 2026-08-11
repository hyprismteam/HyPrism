// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

import type { PageMapItem } from 'nextra'
import { generateStaticParamsFor, importPage } from 'nextra/pages'
import { getPageMap } from 'nextra/page-map'
import { DocsShell } from '../../_components/docs-shell'
import { useMDXComponents as getMDXComponents } from '../../../mdx-components'

type PageProps = Readonly<{
  params: Promise<{
    mdxPath?: string[]
  }>
}>

const generateLocalizedStaticParams = generateStaticParamsFor('mdxPath')

export async function generateStaticParams() {
  const localizedParams = await generateLocalizedStaticParams()
  const paths = new Map<string, { mdxPath: string[] }>()

  for (const params of localizedParams) {
    const mdxPath = params.mdxPath as string[]
    paths.set(JSON.stringify(mdxPath), { mdxPath })
  }

  return [...paths.values()]
}

export async function generateMetadata({ params }: PageProps) {
  const { mdxPath } = await params
  const { metadata } = await importPage(mdxPath, 'en')
  return metadata
}

function getDocsPageMap(pageMap: PageMapItem[]): PageMapItem[] {
  const docsFolder = pageMap.find(
    item => 'children' in item && item.route === '/docs'
  )

  if (!docsFolder || !('children' in docsFolder)) {
    return []
  }

  return docsFolder.children
}

function getPageTitle(metadata: Record<string, unknown>): string {
  return typeof metadata.title === 'string' ? metadata.title : 'HyPrism'
}

const Wrapper = getMDXComponents({}).wrapper!

export default async function Page(props: PageProps) {
  const params = await props.params
  const [englishPage, russianPage, englishPageMap, russianPageMap] = await Promise.all([
    importPage(params.mdxPath, 'en'),
    importPage(params.mdxPath, 'ru'),
    getPageMap('/en'),
    getPageMap('/ru')
  ])
  const EnglishContent = englishPage.default
  const RussianContent = russianPage.default

  return (
    <DocsShell
      pages={{
        en: {
          title: getPageTitle(englishPage.metadata),
          pageMap: getDocsPageMap(englishPageMap),
          content: (
            <Wrapper
              toc={englishPage.toc}
              metadata={englishPage.metadata}
              sourceCode={englishPage.sourceCode}
            >
              <EnglishContent {...props} params={params} />
            </Wrapper>
          )
        },
        ru: {
          title: getPageTitle(russianPage.metadata),
          pageMap: getDocsPageMap(russianPageMap),
          content: (
            <Wrapper
              toc={russianPage.toc}
              metadata={russianPage.metadata}
              sourceCode={russianPage.sourceCode}
            >
              <RussianContent {...props} params={params} />
            </Wrapper>
          )
        }
      }}
    />
  )
}
