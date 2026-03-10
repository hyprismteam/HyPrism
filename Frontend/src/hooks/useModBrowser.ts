import { useState, useEffect, useCallback, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { useAccentColor } from '../contexts/AccentColorContext';
import { ipc, type ModInfo, type ModCategory, type ModFileInfo } from '@/lib/ipc';

// #region Helpers

/**
 * Formats a download count into a compact human-readable string.
 * Values ≥ 1 000 000 are shown as `"X.XM"`, values ≥ 1 000 as `"X.XK"`,
 * and smaller values are returned as-is.
 * @param count - The raw download count.
 * @returns A compact string such as `"1.2M"`, `"45.6K"`, or `"999"`.
 */
export const formatDownloads = (count: number): string => {
  if (count >= 1_000_000) return `${(count / 1_000_000).toFixed(1)}M`;
  if (count >= 1_000) return `${(count / 1_000).toFixed(1)}K`;
  return count.toString();
};

/**
 * Maps a CurseForge release type number to a localized label string.
 * @param type - CurseForge release type: `1` = Release, `2` = Beta, `3` = Alpha.
 * @param t - i18next translation function used to resolve the label key.
 * @returns The localized release type label, or the "unknown" label for unrecognized values.
 */
export const getReleaseTypeLabel = (type: number, t: (key: string) => string) => {
  switch (type) {
    case 1: return t('modManager.releaseType.release');
    case 2: return t('modManager.releaseType.beta');
    case 3: return t('modManager.releaseType.alpha');
    default: return t('modManager.releaseType.unknown');
  }
};

/**
 * Reads a `File` object and returns its contents as a Base64-encoded string.
 * Used when Electron's native file path is unavailable and the file must be
 * transferred to the backend via IPC as a Base64 payload.
 * @param file - The browser `File` object to encode.
 * @returns A promise that resolves with the Base64-encoded file contents.
 */
export const readFileAsBase64 = (file: File): Promise<string> => {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => {
      const ab = reader.result as ArrayBuffer;
      const bytes = new Uint8Array(ab);
      let binary = '';
      for (let i = 0; i < bytes.byteLength; i++) binary += String.fromCharCode(bytes[i]);
      resolve(btoa(binary));
    };
    reader.onerror = () => reject(reader.error);
    reader.readAsArrayBuffer(file);
  });
};

// #endregion

// #region Types

/**
 * Represents a single mod download task in the download queue.
 */
export type DownloadJob = {
  id: string;
  name: string;
  status: 'pending' | 'running' | 'success' | 'error';
  attempts: number;
  error?: string;
};

/**
 * A CurseForge sort option consisting of an API sort field ID and a display name.
 */
export type SortOption = { id: number; name: string };

/**
 * Configuration options for the {@link useModBrowser} hook.
 */
export interface UseModBrowserOptions {
  currentInstanceId?: string;
  installedModIds?: Set<string>;
  installedFileIds?: Set<string>;
  onModsInstalled?: () => void;
  onBack?: () => void;
  refreshSignal?: number;
}

// #endregion

/**
 * Manages mod browsing, searching, downloading, and drag-and-drop import for
 * a specific game instance.
 *
 * @param options - Options including the target instance ID, installed-mod sets,
 *   and lifecycle callbacks.
 * @returns The complete mod-browser state and handler bag.
 */
export const useModBrowser = (options: UseModBrowserOptions) => {
  const {
    currentInstanceId,
    installedModIds,
    installedFileIds,
    onModsInstalled,
    refreshSignal,
  } = options;

  const { t } = useTranslation();
  const { accentColor, accentTextColor } = useAccentColor();

  // Normalize ID helper
  const normalizeId = useCallback((value: string | number | null | undefined) => String(value ?? ''), []);

  // #region Search State
  const [searchQuery, setSearchQuery] = useState('');
  const [searchResults, setSearchResults] = useState<ModInfo[]>([]);
  const [categories, setCategories] = useState<ModCategory[]>([]);
  const [selectedCategory, setSelectedCategory] = useState(0);
  const [selectedSortField, setSelectedSortField] = useState(6);
  const [isSearching, setIsSearching] = useState(false);
  const [hasSearched, setHasSearched] = useState(false);
  const [currentPage, setCurrentPage] = useState(0);
  const [hasMore, setHasMore] = useState(true);
  const [isLoadingMore, setIsLoadingMore] = useState(false);
  const [isCategoryDropdownOpen, setIsCategoryDropdownOpen] = useState(false);
  const [isSortDropdownOpen, setIsSortDropdownOpen] = useState(false);
  const [selectedMods, setSelectedMods] = useState<Set<string>>(new Set());
  const [error, setError] = useState<string | null>(null);
  const browseSelectionAnchorRef = useRef<number | null>(null);

  // #endregion

  // #region Settings
  const [showAlphaMods, setShowAlphaMods] = useState(false);

  // #endregion

  // #region Mod Files Cache
  const [modFilesCache, setModFilesCache] = useState<Map<string, ModFileInfo[]>>(new Map());
  const [selectedVersions, setSelectedVersions] = useState<Map<string, string>>(new Map());

  // #endregion

  // #region Detail Panel
  const [selectedMod, setSelectedMod] = useState<ModInfo | null>(null);
  const [selectedModFiles, setSelectedModFiles] = useState<ModFileInfo[]>([]);
  const [isLoadingModFiles, setIsLoadingModFiles] = useState(false);
  const [detailSelectedFileId, setDetailSelectedFileId] = useState<string | undefined>();
  const [activeScreenshot, setActiveScreenshot] = useState(0);
  const [lightboxIndex, setLightboxIndex] = useState<number | null>(null);

  // #endregion

  // #region Download
  const [isDownloading, setIsDownloading] = useState(false);
  const [downloadProgress, setDownloadProgress] = useState<{ current: number; total: number; currentMod: string } | null>(null);
  const [downloadJobs, setDownloadJobs] = useState<DownloadJob[]>([]);

  // #endregion

  // #region Import
  const [isDragging, setIsDragging] = useState(false);
  const [isImporting, setIsImporting] = useState(false);
  const [importProgress, setImportProgress] = useState<string | null>(null);

  // #endregion

  // #region Refs
  const searchTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const dragDepthRef = useRef(0);
  const scrollContainerRef = useRef<HTMLDivElement>(null);
  const categoryDropdownRef = useRef<HTMLDivElement>(null);
  const sortDropdownRef = useRef<HTMLDivElement>(null);

  // #endregion

  // #region Sort Options
  const sortOptions: SortOption[] = [
    { id: 1, name: t('modManager.sortRelevancy') },
    { id: 2, name: t('modManager.sortPopularity') },
    { id: 3, name: t('modManager.sortLatestUpdate') },
    { id: 11, name: t('modManager.sortCreationDate') },
    { id: 6, name: t('modManager.sortTotalDownloads') },
  ];

  // #endregion

  // #region Data Loading
  useEffect(() => {
    ipc.mods.categories().then(cats => setCategories(cats || [])).catch(() => {});
    ipc.settings.get().then(s => setShowAlphaMods(s.showAlphaMods ?? false)).catch(() => {});
  }, []);

  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (categoryDropdownRef.current && !categoryDropdownRef.current.contains(e.target as Node))
        setIsCategoryDropdownOpen(false);
      if (sortDropdownRef.current && !sortDropdownRef.current.contains(e.target as Node))
        setIsSortDropdownOpen(false);
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, []);

  // Ensure drag overlay is never stuck if user leaves the window / ends drag elsewhere
  useEffect(() => {
    const clearDrag = () => {
      dragDepthRef.current = 0;
      setIsDragging(false);
    };

    const onWindowDrop = () => clearDrag();
    const onWindowDragEnd = () => clearDrag();
    const onWindowDragLeave = (e: DragEvent) => {
      if ((e as unknown as { relatedTarget?: EventTarget | null }).relatedTarget == null) {
        clearDrag();
      }
    };

    window.addEventListener('drop', onWindowDrop);
    window.addEventListener('dragend', onWindowDragEnd);
    window.addEventListener('dragleave', onWindowDragLeave);
    return () => {
      window.removeEventListener('drop', onWindowDrop);
      window.removeEventListener('dragend', onWindowDragEnd);
      window.removeEventListener('dragleave', onWindowDragLeave);
    };
  }, []);

  // #endregion

  // #region Search
  const handleSearch = useCallback(async (page = 0, append = false, opts?: { silent?: boolean }) => {
    const silent = opts?.silent === true;
    if (append) {
      setIsLoadingMore(true);
    } else if (!silent) {
      setIsSearching(true);
    }

    try {
      const pageSize = 20;
      const cats = selectedCategory === 0 ? [] : [selectedCategory.toString()];

      const result = await ipc.mods.search({
        query: searchQuery,
        page,
        pageSize,
        categories: cats,
        sortField: selectedSortField,
        sortOrder: 1,
      });

      const mods: ModInfo[] = result?.mods ?? [];

      if (append) {
        setSearchResults(prev => [...prev, ...mods]);
      } else {
        setSearchResults(mods);
      }
      setHasMore(mods.length >= pageSize);
      setCurrentPage(page);
    } catch (err: unknown) {
      const e = err as Error;
      setError(e.message || t('modManager.searchFailed'));
      if (!append && !silent) setSearchResults([]);
    }

    if (!silent) setIsSearching(false);
    setIsLoadingMore(false);
    setHasSearched(true);
  }, [searchQuery, selectedCategory, selectedSortField, t]);

  // External refresh trigger
  useEffect(() => {
    if (refreshSignal == null) return;
    handleSearch(0, false, { silent: true });
  }, [refreshSignal, handleSearch]);

  // Debounced search on query/filter changes
  useEffect(() => {
    if (searchTimeoutRef.current) clearTimeout(searchTimeoutRef.current);
    searchTimeoutRef.current = setTimeout(() => handleSearch(0, false), 300);
    return () => { if (searchTimeoutRef.current) clearTimeout(searchTimeoutRef.current); };
  }, [searchQuery, selectedCategory, selectedSortField, handleSearch]);

  // Infinite scroll handler
  const handleScroll = useCallback((e: React.UIEvent<HTMLDivElement>) => {
    const target = e.target as HTMLDivElement;
    const scrollBottom = target.scrollHeight - target.scrollTop - target.clientHeight;
    if (scrollBottom < 200 && !isLoadingMore && !isSearching && hasMore) {
      handleSearch(currentPage + 1, true);
    }
  }, [isLoadingMore, isSearching, hasMore, currentPage, handleSearch]);

  // #endregion

  // #region Mod Files
  const loadModFiles = useCallback(async (modId: string): Promise<ModFileInfo[]> => {
    if (modFilesCache.has(modId)) return modFilesCache.get(modId) || [];

    try {
      const result = await ipc.mods.files({ modId, pageSize: 50 });
      let files = result?.files ?? [];
      if (!showAlphaMods) {
        files = files.filter(f => f.releaseType !== 3);
      }
      files.sort((a, b) => new Date(b.fileDate).getTime() - new Date(a.fileDate).getTime());
      setModFilesCache(prev => new Map(prev).set(modId, files));
      if (files.length > 0 && !selectedVersions.has(modId)) {
        setSelectedVersions(prev => new Map(prev).set(modId, files[0].id));
      }
      return files;
    } catch {
      return [];
    }
  }, [modFilesCache, showAlphaMods, selectedVersions]);

  const handleModClick = useCallback(async (mod: ModInfo) => {
    setSelectedMod(mod);
    setActiveScreenshot(0);
    setIsLoadingModFiles(true);

    const modId = normalizeId(mod.id);
    if (modId) {
      const files = await loadModFiles(modId);
      const selectedFileId = selectedVersions.get(modId) || files[0]?.id;
      setSelectedModFiles(files);
      setDetailSelectedFileId(selectedFileId);
      if (selectedFileId) setSelectedVersions(prev => new Map(prev).set(modId, selectedFileId));
    } else {
      setSelectedModFiles([]);
      setDetailSelectedFileId(undefined);
    }
    setIsLoadingModFiles(false);
  }, [normalizeId, loadModFiles, selectedVersions]);

  const getCurseForgeUrl = useCallback((mod: ModInfo): string => {
    if (mod.slug) {
      return `https://www.curseforge.com/hytale/mods/${mod.slug}`;
    }
    return `https://www.curseforge.com/hytale/mods/search?search=${encodeURIComponent(mod.name || String(mod.id))}`;
  }, []);

  const handleOpenModPage = useCallback((e: React.MouseEvent, mod: ModInfo) => {
    e.preventDefault();
    e.stopPropagation();
    ipc.browser.open(getCurseForgeUrl(mod));
  }, [getCurseForgeUrl]);

  const toggleModSelection = useCallback((mod: ModInfo, index: number) => {
    const modId = normalizeId(mod.id);
    let shouldPrefetch = false;
    setSelectedMods((prev) => {
      const next = new Set(prev);
      if (next.has(modId)) {
        next.delete(modId);
      } else {
        next.add(modId);
        shouldPrefetch = true;
      }
      return next;
    });
    browseSelectionAnchorRef.current = index;
    if (shouldPrefetch) {
      void loadModFiles(modId);
    }
  }, [normalizeId, loadModFiles]);

  const handleBrowseShiftLeftClick = useCallback((e: React.MouseEvent, index: number) => {
    if (!e.shiftKey) return;
    e.preventDefault();
    if (searchResults.length === 0) return;

    const anchor = browseSelectionAnchorRef.current ?? index;
    const start = Math.min(anchor, index);
    const end = Math.max(anchor, index);
    const ids = searchResults.slice(start, end + 1).map((mod) => normalizeId(mod.id));

    setSelectedMods(new Set(ids));
    ids.forEach((id) => { void loadModFiles(id); });
  }, [searchResults, normalizeId, loadModFiles]);

  // #endregion

  // #region Download Queue
  const runDownloadQueue = useCallback(async (items: Array<{ id: string; name: string; fileId: string }>) => {
    const maxRetries = 3;
    setIsDownloading(true);
    setDownloadProgress({ current: 0, total: items.length, currentMod: '' });
    setDownloadJobs(items.map(i => ({ id: i.id, name: i.name, status: 'pending' as const, attempts: 0 })));

    let completed = 0;
    for (const item of items) {
      for (let attempt = 1; attempt <= maxRetries; attempt++) {
        setDownloadJobs(prev => prev.map(j => j.id === item.id ? { ...j, status: 'running', attempts: attempt } : j));
        try {
          const result = await ipc.mods.install({ modId: item.id, fileId: item.fileId, instanceId: currentInstanceId });
          const ok = typeof result === 'object' && result !== null ? (result as { success: boolean }).success : result;
          const errorMsg = typeof result === 'object' && result !== null ? (result as { error?: string }).error : undefined;
          if (!ok) throw new Error(errorMsg || t('modManager.backendRefused'));
          setDownloadJobs(prev => prev.map(j => j.id === item.id ? { ...j, status: 'success' } : j));
          break;
        } catch (err: unknown) {
          const e = err as Error;
          const isLast = attempt === maxRetries;
          setDownloadJobs(prev => prev.map(j => j.id === item.id ? { ...j, status: isLast ? 'error' : 'pending', error: e?.message } : j));
          if (!isLast) await new Promise(r => setTimeout(r, 500 * attempt));
        }
      }
      completed++;
      setDownloadProgress({ current: completed, total: items.length, currentMod: item.name });
    }
  }, [currentInstanceId, t]);

  const handleDownloadSelected = useCallback(async () => {
    if (selectedMods.size === 0) return;

    const selectedIds = Array.from(selectedMods);
    const modsById = new Map(searchResults.map((m) => [normalizeId(m.id), m]));

    const items: Array<{ id: string; name: string; fileId: string }> = [];
    for (const modId of selectedIds) {
      const mod = modsById.get(modId);
      let fileId = selectedVersions.get(modId);
      if (!fileId) {
        const files = await loadModFiles(modId);
        fileId = files?.[0]?.id;
      }
      if (fileId) {
        const name = mod?.name || `Mod ${modId}`;
        items.push({ id: modId, name, fileId });
      }
    }

    if (items.length === 0) { setError(t('modManager.noDownloadableFiles')); return; }

    try { await runDownloadQueue(items); }
    catch (err: unknown) { setError((err as Error)?.message || t('modManager.downloadFailed')); }

    setIsDownloading(false);
    setDownloadProgress(null);
    setDownloadJobs([]);
    setSelectedMods(new Set());
    onModsInstalled?.();
  }, [selectedMods, searchResults, selectedVersions, normalizeId, loadModFiles, runDownloadQueue, t, onModsInstalled]);

  const handleInstallSingleMod = useCallback(async (modId: string, fileId: string, name: string) => {
    try {
      await runDownloadQueue([{ id: modId, name, fileId }]);
    } catch { /* handled in queue */ }
    setIsDownloading(false);
    setDownloadProgress(null);
    setDownloadJobs([]);
    onModsInstalled?.();
  }, [runDownloadQueue, onModsInstalled]);

  // #endregion

  // #region Drag And Drop
  const handleDragEnter = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    dragDepthRef.current += 1;
    setIsDragging(true);
  }, []);

  const handleDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragging(true);
  }, []);

  const handleDragLeave = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    dragDepthRef.current = Math.max(0, dragDepthRef.current - 1);
    if (dragDepthRef.current === 0) {
      setIsDragging(false);
    }
  }, []);

  const handleDrop = useCallback(async (e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    dragDepthRef.current = 0;
    setIsDragging(false);
    const allFiles = Array.from(e.dataTransfer.files);
    // Filter to only .jar files
    const files = allFiles.filter((file) => {
      const name = file.name?.toLowerCase() || '';
      return name.endsWith('.jar') || name.endsWith('.jar.disabled');
    });
    if (files.length === 0) return;
    setIsImporting(true);
    let successCount = 0;
    try {
      for (const file of files) {
        setImportProgress(t('modManager.installingMod').replace('{{name}}', file.name));
        const electronFile = file as unknown as { path?: string };
        if (electronFile.path) {
          const ok = await ipc.mods.installLocal({ sourcePath: electronFile.path, instanceId: currentInstanceId });
          if (ok) successCount++;
        } else {
          const base64 = await readFileAsBase64(file);
          const ok = await ipc.mods.installBase64({ fileName: file.name, base64Content: base64, instanceId: currentInstanceId });
          if (ok) successCount++;
        }
      }
      if (successCount > 0) {
        setImportProgress(t('modManager.installedCount').replace('{{count}}', successCount.toString()));
        setTimeout(() => setImportProgress(null), 3000);
        onModsInstalled?.();
      }
    } catch {
      setError(t('modManager.importFailed'));
    } finally {
      setIsImporting(false);
    }
  }, [currentInstanceId, onModsInstalled, t]);

  // #endregion

  // #region Helpers
  const getCategoryName = useCallback((id: number) => {
    const cat = categories.find(c => c.id === id);
    if (!cat) return t('modManager.allMods');
    const key = `modManager.category.${cat.name.replace(/[\s\\/]+/g, '_').toLowerCase()}`;
    const translated = t(key);
    return translated !== key ? translated : cat.name;
  }, [categories, t]);

  const getSortName = useCallback((id: number) => sortOptions.find(s => s.id === id)?.name ?? '', [sortOptions]);

  // #endregion

  return {
    // Context
    t,
    accentColor,
    accentTextColor,
    normalizeId,

    // Search & Results
    searchQuery,
    setSearchQuery,
    searchResults,
    categories,
    selectedCategory,
    setSelectedCategory,
    selectedSortField,
    setSelectedSortField,
    isSearching,
    hasSearched,
    hasMore,
    isLoadingMore,
    sortOptions,

    // Detail Panel
    selectedMod,
    setSelectedMod,
    selectedModFiles,
    isLoadingModFiles,
    selectedVersions,
    setSelectedVersions,
    activeScreenshot,
    setActiveScreenshot,
    lightboxIndex,
    setLightboxIndex,
    detailSelectedFileId,
    setDetailSelectedFileId,

    // Batch Selection & Download
    selectedMods,
    setSelectedMods,
    isDownloading,
    downloadProgress,
    downloadJobs,

    // Drag & Drop
    isDragging,
    isImporting,
    importProgress,

    // Dropdowns
    isCategoryDropdownOpen,
    setIsCategoryDropdownOpen,
    isSortDropdownOpen,
    setIsSortDropdownOpen,
    categoryDropdownRef,
    sortDropdownRef,

    // Error
    error,
    setError,

    // Refs
    scrollContainerRef,
    browseSelectionAnchorRef,

    // Options (pass-through)
    installedModIds,
    installedFileIds,

    // Handlers
    handleSearch,
    handleScroll,
    handleModClick,
    toggleModSelection,
    handleBrowseShiftLeftClick,
    handleDownloadSelected,
    handleInstallSingleMod,
    handleOpenModPage,
    handleDragEnter,
    handleDragOver,
    handleDragLeave,
    handleDrop,

    // Utilities
    getCategoryName,
    getSortName,
  };
};

/** Full return type of the {@link useModBrowser} hook. */
export type UseModBrowserReturn = ReturnType<typeof useModBrowser>;
