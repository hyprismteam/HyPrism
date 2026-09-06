// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

import Link from '@docusaurus/Link'
import { useLocation } from '@docusaurus/router'
import React, { useEffect, useMemo, useState } from 'react'
import { useDocsLocale } from '../../context/locale'
import { routeToUrl, useLocalizedDocsData } from '../../data'
import { dictionaries } from '../../i18n'

export default function SearchBar() {
  const { locale } = useDocsLocale()
  const { search } = useLocalizedDocsData()
  const dictionary = dictionaries[locale].search
  const location = useLocation()
  const [query, setQuery] = useState('')
  const normalizedQuery = query.trim().toLocaleLowerCase(locale)
  const results = useMemo(() => {
    if (normalizedQuery.length < 2) {
      return []
    }
    return search[locale]
      .filter(entry =>
        `${entry.title} ${entry.description} ${entry.text}`
          .toLocaleLowerCase(locale)
          .includes(normalizedQuery)
      )
      .slice(0, 8)
  }, [locale, normalizedQuery, search])

  useEffect(() => {
    setQuery('')
  }, [location.pathname])

  return (
    <div className="hyprism-search">
      <label>
        <span className="sr-only">{dictionary.label}</span>
        <input
          type="search"
          value={query}
          placeholder={dictionary.placeholder}
          aria-label={dictionary.label}
          onChange={event => setQuery(event.target.value)}
        />
      </label>
      {normalizedQuery.length >= 2 && (
        <div className="hyprism-search-results">
          {results.length > 0 ? (
            <ul>
              {results.map(result => (
                <li key={result.route}>
                  <Link to={routeToUrl(result.route)}>
                    <strong>{result.title}</strong>
                    {result.description && <span>{result.description}</span>}
                  </Link>
                </li>
              ))}
            </ul>
          ) : (
            <p>{dictionary.empty}</p>
          )}
        </div>
      )}
    </div>
  )
}
