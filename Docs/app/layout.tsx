// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

import type { Metadata } from 'next'
import { Head } from 'nextra/components'
import { getPageMap } from 'nextra/page-map'
import { Footer, Layout, Navbar } from 'nextra-theme-docs'
import 'nextra-theme-docs/style.css'
import './styles.css'

export const metadata: Metadata = {
  title: {
    default: 'HyPrism Documentation',
    template: '%s | HyPrism'
  },
  description: 'User and developer documentation for HyPrism Launcher'
}

const navbar = (
  <Navbar
    logo={<span className="hyprism-logo">HyPrism <span>Docs</span></span>}
    logoLink="/docs/"
    projectLink="https://github.com/hyprismteam/HyPrism"
  />
)

const footer = (
  <Footer>
    HyPrism Launcher documentation, GPL-3.0-only
  </Footer>
)

export default async function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en" dir="ltr" suppressHydrationWarning>
      <Head>
        <meta name="theme-color" content="#0d0f13" />
      </Head>
      <body>
        <Layout
          navbar={navbar}
          pageMap={await getPageMap()}
          docsRepositoryBase="https://github.com/hyprismteam/HyPrism/tree/main/Docs/content"
          footer={footer}
          sidebar={{ autoCollapse: true, defaultMenuCollapseLevel: 1 }}
        >
          {children}
        </Layout>
      </body>
    </html>
  )
}
