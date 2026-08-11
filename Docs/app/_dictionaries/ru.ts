// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

import type { Dictionary } from './types'

const dictionary = {
  siteTitle: 'Документация HyPrism',
  siteDescription: 'Документация для пользователей и разработчиков HyPrism Launcher',
  docsLabel: 'Документация',
  footer: 'Документация HyPrism Launcher, GPL-3.0-only',
  projectRepository: 'Репозиторий проекта',
  languageSwitcher: 'Выбрать язык документации',
  languages: {
    en: 'English',
    ru: 'Русский'
  },
  search: {
    placeholder: 'Поиск по документации…',
    emptyResult: 'Ничего не найдено',
    errorText: 'Не удалось загрузить поисковый индекс',
    loading: 'Загрузка…'
  },
  toc: {
    title: 'На этой странице',
    backToTop: 'Наверх'
  },
  editPage: 'Редактировать страницу на GitHub',
  feedback: 'Есть вопрос или предложение?',
  lastUpdated: 'Последнее обновление',
  theme: {
    dark: 'Тёмная',
    light: 'Светлая',
    system: 'Системная'
  }
} satisfies Dictionary

export default dictionary
