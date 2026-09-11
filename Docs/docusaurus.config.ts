// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

import type { Config } from '@docusaurus/types'
import type * as Preset from '@docusaurus/preset-classic'
import { themes as prismThemes } from 'prism-react-renderer'
import path from 'node:path'
import repositoryLinks from './plugins/remark-repository-links'

const configuredBasePath = process.env.PAGES_BASE_PATH || '/HyPrism'
const baseUrl = `/${configuredBasePath.replace(/^\/+|\/+$/g, '')}/`

const localeBootstrapScript = `try {
  const savedLocale = window.localStorage.getItem('hyprism-docs-locale')
  const locale = savedLocale === 'en' || savedLocale === 'ru'
    ? savedLocale
    : window.navigator.language.toLowerCase().startsWith('ru') ? 'ru' : 'en'
  document.documentElement.dataset.docsLocale = locale
  document.documentElement.dataset.docsReady = locale === 'en' ? 'true' : 'false'
  document.documentElement.lang = locale
} catch {}`

const config: Config = {
  title: 'HyPrism Documentation',
  tagline: 'User and developer documentation for HyPrism Launcher',
  url: 'https://hyprismteam.github.io',
  baseUrl,
  organizationName: 'hyprismteam',
  projectName: 'HyPrism',
  trailingSlash: true,
  onBrokenLinks: 'throw',
  // Public routes render one of two MDX modules at runtime, so Docusaurus cannot statically match their TOC anchors
  onBrokenAnchors: 'ignore',
  markdown: {
    hooks: {
      onBrokenMarkdownLinks: 'throw'
    }
  },
  future: {
    v4: true
  },
  i18n: {
    defaultLocale: 'en',
    locales: ['en']
  },
  headTags: [
    {
      tagName: 'script',
      attributes: {},
      innerHTML: localeBootstrapScript
    },
    {
      tagName: 'meta',
      attributes: {
        name: 'theme-color',
        content: '#0d0f13'
      }
    }
  ],
  presets: [
    [
      'classic',
      {
        docs: false,
        blog: false,
        pages: false,
        sitemap: false,
        theme: {
          customCss: './src/css/custom.css'
        }
      } satisfies Preset.Options
    ]
  ],
  plugins: [
    [
      '@docusaurus/plugin-content-docs',
      {
        id: 'source-en',
        path: 'content/en',
        routeBasePath: '__source/en',
        sidebarPath: false,
        beforeDefaultRemarkPlugins: [[repositoryLinks, { repositoryRoot: path.resolve(__dirname, '..') }]],
        showLastUpdateAuthor: false,
        showLastUpdateTime: false
      }
    ],
    [
      '@docusaurus/plugin-content-docs',
      {
        id: 'source-ru',
        path: 'content/ru',
        routeBasePath: '__source/ru',
        sidebarPath: false,
        beforeDefaultRemarkPlugins: [[repositoryLinks, { repositoryRoot: path.resolve(__dirname, '..') }]],
        showLastUpdateAuthor: false,
        showLastUpdateTime: false
      }
    ],
    './plugins/localized-docs/index.ts'
  ],
  themeConfig: {
    colorMode: {
      defaultMode: 'dark',
      respectPrefersColorScheme: true
    },
    navbar: {
      title: 'HyPrism',
      items: [
        {
          type: 'html',
          value: '<span>Docs</span>',
          position: 'left',
          className: 'hyprism-docs-link-slot'
        },
        {
          type: 'search',
          position: 'right'
        },
        {
          type: 'html',
          value: '<span>EN / RU</span>',
          position: 'right',
          className: 'hyprism-language-switch-slot'
        },
        {
          href: 'https://github.com/hyprismteam/HyPrism',
          label: 'GitHub',
          position: 'right'
        }
      ]
    },
    footer: {
      style: 'dark',
      copyright: 'HyPrism Launcher documentation · GPL-3.0-only'
    },
    prism: {
      theme: prismThemes.github,
      darkTheme: prismThemes.dracula,
      additionalLanguages: ['csharp', 'powershell', 'bash', 'json', 'diff']
    }
  } satisfies Preset.ThemeConfig
}

export default config
