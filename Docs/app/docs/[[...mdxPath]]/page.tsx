// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

import { generateStaticParamsFor, importPage } from 'nextra/pages'
import { LocalizedMdxPage } from '../../_components/localized-mdx-page'
import type { MdxPageKey } from '../../_components/mdx-registry.generated'

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

export default async function Page({ params }: PageProps) {
  const { mdxPath = [] } = await params
  return <LocalizedMdxPage pageKey={mdxPath.join('/') as MdxPageKey} />
}
