import { useState, useCallback, useRef, useEffect } from 'react';
import { ipc, type ModInfo, type ModCategory } from '@/lib/ipc';

/** Number of mods fetched per page in search results. */
const PAGE_SIZE = 20;

/**
 * Manages CurseForge mod search state including query, pagination, category
 * and sort-field selection, and load-more / reset helpers.
 *
 * @returns Search state values, setter callbacks, and action handlers.
 */
export function useModSearch() {
  const [searchQuery, setSearchQuery] = useState('');
  const [searchResults, setSearchResults] = useState<ModInfo[]>([]);
  const [categories, setCategories] = useState<ModCategory[]>([]);
  const [selectedCategory, setSelectedCategory] = useState(0);
  const [selectedSortField, setSelectedSortField] = useState(6); // Total downloads
  const [isSearching, setIsSearching] = useState(false);
  const [hasSearched, setHasSearched] = useState(false);
  const [currentPage, setCurrentPage] = useState(0);
  const [hasMore, setHasMore] = useState(true);
  const [isLoadingMore, setIsLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const searchTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Load categories on mount
  useEffect(() => {
    ipc.mods.categories().then(cats => setCategories(cats || [])).catch(() => {});
  }, []);

  const handleSearch = useCallback(async (
    page = 0,
    append = false,
    options?: { silent?: boolean }
  ) => {
    const silent = options?.silent === true;
    if (append) {
      setIsLoadingMore(true);
    } else if (!silent) {
      setIsSearching(true);
    }

    try {
      const cats = selectedCategory === 0 ? [] : [selectedCategory.toString()];

      const result = await ipc.mods.search({
        query: searchQuery,
        page,
        pageSize: PAGE_SIZE,
        categories: cats,
        sortField: selectedSortField,
        sortOrder: 1, // desc
      });

      const mods: ModInfo[] = result?.mods ?? [];

      if (append) {
        setSearchResults(prev => [...prev, ...mods]);
      } else {
        setSearchResults(mods);
      }

      setCurrentPage(page);
      setHasMore(mods.length >= PAGE_SIZE);
      setHasSearched(true);
      setError(null);
    } catch (err) {
      console.error('Search failed:', err);
      setError(err instanceof Error ? err.message : 'Search failed');
      if (!append) setSearchResults([]);
    } finally {
      if (!silent) setIsSearching(false);
      setIsLoadingMore(false);
    }
  }, [searchQuery, selectedCategory, selectedSortField]);

  const loadMore = useCallback(() => {
    if (!isLoadingMore && hasMore) {
      handleSearch(currentPage + 1, true);
    }
  }, [currentPage, handleSearch, hasMore, isLoadingMore]);

  const resetSearch = useCallback(() => {
    setSearchResults([]);
    setCurrentPage(0);
    setHasMore(true);
    setHasSearched(false);
    setError(null);
  }, []);

  // Cleanup timeout on unmount
  useEffect(() => {
    return () => {
      if (searchTimeoutRef.current) {
        clearTimeout(searchTimeoutRef.current);
      }
    };
  }, []);

  return {
    // State
    searchQuery,
    searchResults,
    categories,
    selectedCategory,
    selectedSortField,
    isSearching,
    hasSearched,
    currentPage,
    hasMore,
    isLoadingMore,
    error,
    // Setters
    setSearchQuery,
    setSelectedCategory,
    setSelectedSortField,
    setError,
    // Actions
    handleSearch,
    loadMore,
    resetSearch,
  };
}
