// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

import type EnglishDictionary from './en'

export const locales = ['en', 'ru'] as const

export type Locale = (typeof locales)[number]
export type Dictionary = typeof EnglishDictionary

export function isLocale(value: string): value is Locale {
  return locales.some(locale => locale === value)
}
