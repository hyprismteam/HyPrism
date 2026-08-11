// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

import type { Dictionary, Locale } from './types'
import { isLocale } from './types'

const dictionaries: Record<Locale, () => Promise<{ default: Dictionary }>> = {
  en: () => import('./en'),
  ru: () => import('./ru')
}

export async function getDictionary(locale: string): Promise<Dictionary> {
  const selectedLocale = isLocale(locale) ? locale : 'en'
  const { default: dictionary } = await dictionaries[selectedLocale]()
  return dictionary
}
