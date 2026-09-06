// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

import Link from '@docusaurus/Link'
import React from 'react'
import { useDocsLocale } from '../context/locale'
import { dictionaries } from '../i18n'

export default function DocsNavbarLink({ mobile = false }: Readonly<{ mobile?: boolean }>) {
  const { locale } = useDocsLocale()
  return (
    <Link className={mobile ? 'menu__link' : 'navbar__item navbar__link'} to="/docs/">
      {dictionaries[locale].docsLabel}
    </Link>
  )
}
