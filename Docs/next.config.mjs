// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

import nextra from 'nextra'

const withNextra = nextra({
  contentDirBasePath: '/docs'
})

export default withNextra({
  output: 'export',
  trailingSlash: true,
  basePath: process.env.PAGES_BASE_PATH || '',
  images: {
    unoptimized: true
  }
})
