// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

export const locales = ['en', 'ru'] as const

export type Locale = (typeof locales)[number]

export const dictionaries = {
  en: {
    docsLabel: 'Docs',
    editPage: 'Edit this page on GitHub',
    languageSwitcher: 'Choose documentation language',
    languages: {
      en: 'English',
      ru: 'Russian'
    },
    menu: 'Documentation navigation',
    next: 'Next',
    previous: 'Previous',
    search: {
      label: 'Search documentation',
      placeholder: 'Search docs',
      empty: 'No results found'
    },
    toc: 'On this page'
  },
  ru: {
    docsLabel: 'Документация',
    editPage: 'Редактировать страницу на GitHub',
    languageSwitcher: 'Выбрать язык документации',
    languages: {
      en: 'Английский',
      ru: 'Русский'
    },
    menu: 'Навигация по документации',
    next: 'Далее',
    previous: 'Назад',
    search: {
      label: 'Поиск по документации',
      placeholder: 'Поиск',
      empty: 'Ничего не найдено'
    },
    toc: 'На этой странице'
  }
} as const
