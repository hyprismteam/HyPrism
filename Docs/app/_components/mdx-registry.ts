// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

import * as EnArchitectureCore from '../../content/en/architecture/core.mdx'
import * as EnArchitectureDataAndCache from '../../content/en/architecture/data-and-cache.mdx'
import * as EnArchitectureDesktop from '../../content/en/architecture/desktop.mdx'
import * as EnArchitectureOverview from '../../content/en/architecture/overview.mdx'
import * as EnDevelopmentBuilding from '../../content/en/development/building.mdx'
import * as EnDevelopmentCodingStyle from '../../content/en/development/coding-style.mdx'
import * as EnDevelopmentContributing from '../../content/en/development/contributing.mdx'
import * as EnDevelopmentDocumentation from '../../content/en/development/documentation.mdx'
import * as EnDevelopmentLocalization from '../../content/en/development/localization.mdx'
import * as EnDevelopmentTesting from '../../content/en/development/testing.mdx'
import * as EnGettingStartedFirstRun from '../../content/en/getting-started/first-run.mdx'
import * as EnGettingStartedInstallation from '../../content/en/getting-started/installation.mdx'
import * as EnIndex from '../../content/en/index.mdx'
import * as EnMigration from '../../content/en/migration.mdx'
import * as EnReferenceConfiguration from '../../content/en/reference/configuration.mdx'
import * as EnReferenceMirrors from '../../content/en/reference/mirrors.mdx'
import * as EnReferenceServices from '../../content/en/reference/services.mdx'
import * as EnUserGuideDashboard from '../../content/en/user-guide/dashboard.mdx'
import * as EnUserGuideInstancesProfilesMods from '../../content/en/user-guide/instances-profiles-mods.mdx'
import * as EnUserGuideNews from '../../content/en/user-guide/news.mdx'
import * as EnUserGuideSettings from '../../content/en/user-guide/settings.mdx'
import * as RuArchitectureCore from '../../content/ru/architecture/core.mdx'
import * as RuArchitectureDataAndCache from '../../content/ru/architecture/data-and-cache.mdx'
import * as RuArchitectureDesktop from '../../content/ru/architecture/desktop.mdx'
import * as RuArchitectureOverview from '../../content/ru/architecture/overview.mdx'
import * as RuDevelopmentBuilding from '../../content/ru/development/building.mdx'
import * as RuDevelopmentCodingStyle from '../../content/ru/development/coding-style.mdx'
import * as RuDevelopmentContributing from '../../content/ru/development/contributing.mdx'
import * as RuDevelopmentDocumentation from '../../content/ru/development/documentation.mdx'
import * as RuDevelopmentLocalization from '../../content/ru/development/localization.mdx'
import * as RuDevelopmentTesting from '../../content/ru/development/testing.mdx'
import * as RuGettingStartedFirstRun from '../../content/ru/getting-started/first-run.mdx'
import * as RuGettingStartedInstallation from '../../content/ru/getting-started/installation.mdx'
import * as RuIndex from '../../content/ru/index.mdx'
import * as RuMigration from '../../content/ru/migration.mdx'
import * as RuReferenceConfiguration from '../../content/ru/reference/configuration.mdx'
import * as RuReferenceMirrors from '../../content/ru/reference/mirrors.mdx'
import * as RuReferenceServices from '../../content/ru/reference/services.mdx'
import * as RuUserGuideDashboard from '../../content/ru/user-guide/dashboard.mdx'
import * as RuUserGuideInstancesProfilesMods from '../../content/ru/user-guide/instances-profiles-mods.mdx'
import * as RuUserGuideNews from '../../content/ru/user-guide/news.mdx'
import * as RuUserGuideSettings from '../../content/ru/user-guide/settings.mdx'
import type { Locale } from '../_dictionaries/types'

const englishPages = {
  '': EnIndex,
  'architecture/core': EnArchitectureCore,
  'architecture/data-and-cache': EnArchitectureDataAndCache,
  'architecture/desktop': EnArchitectureDesktop,
  'architecture/overview': EnArchitectureOverview,
  'development/building': EnDevelopmentBuilding,
  'development/coding-style': EnDevelopmentCodingStyle,
  'development/contributing': EnDevelopmentContributing,
  'development/documentation': EnDevelopmentDocumentation,
  'development/localization': EnDevelopmentLocalization,
  'development/testing': EnDevelopmentTesting,
  'getting-started/first-run': EnGettingStartedFirstRun,
  'getting-started/installation': EnGettingStartedInstallation,
  migration: EnMigration,
  'reference/configuration': EnReferenceConfiguration,
  'reference/mirrors': EnReferenceMirrors,
  'reference/services': EnReferenceServices,
  'user-guide/dashboard': EnUserGuideDashboard,
  'user-guide/instances-profiles-mods': EnUserGuideInstancesProfilesMods,
  'user-guide/news': EnUserGuideNews,
  'user-guide/settings': EnUserGuideSettings
}

export type MdxPageKey = keyof typeof englishPages

export const localizedPages = {
  en: englishPages,
  ru: {
    '': RuIndex,
    'architecture/core': RuArchitectureCore,
    'architecture/data-and-cache': RuArchitectureDataAndCache,
    'architecture/desktop': RuArchitectureDesktop,
    'architecture/overview': RuArchitectureOverview,
    'development/building': RuDevelopmentBuilding,
    'development/coding-style': RuDevelopmentCodingStyle,
    'development/contributing': RuDevelopmentContributing,
    'development/documentation': RuDevelopmentDocumentation,
    'development/localization': RuDevelopmentLocalization,
    'development/testing': RuDevelopmentTesting,
    'getting-started/first-run': RuGettingStartedFirstRun,
    'getting-started/installation': RuGettingStartedInstallation,
    migration: RuMigration,
    'reference/configuration': RuReferenceConfiguration,
    'reference/mirrors': RuReferenceMirrors,
    'reference/services': RuReferenceServices,
    'user-guide/dashboard': RuUserGuideDashboard,
    'user-guide/instances-profiles-mods': RuUserGuideInstancesProfilesMods,
    'user-guide/news': RuUserGuideNews,
    'user-guide/settings': RuUserGuideSettings
  }
} satisfies Record<Locale, Record<MdxPageKey, typeof EnIndex>>
