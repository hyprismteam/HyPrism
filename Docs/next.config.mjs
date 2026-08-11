// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

import nextra from 'nextra'

const withNextra = nextra({
  contentDirBasePath: '/docs'
})

export default withNextra({
  agentRules: false,
  output: 'export',
  trailingSlash: true,
  basePath: process.env.PAGES_BASE_PATH || (process.env.NODE_ENV === 'development' ? '/HyPrism' : ''),
  i18n: {
    locales: ['en', 'ru'],
    defaultLocale: 'en'
  },
  images: {
    unoptimized: true
  }
})
