import { useState, useCallback, useRef, useMemo, useEffect } from 'react';
import { ipc, type ModInfo as CurseForgeModInfo } from '@/lib/ipc';
import type { InstalledVersionInfo, InstalledModInfo } from '@/types';
import { getInstanceInstalledMods, checkInstanceModUpdates, uninstallInstanceMod } from './useInstanceActions';

/**
 * Normalizes raw backend mod payload objects, handling PascalCase and camelCase property names
 * and converting numeric IDs from strings where needed.
 *
 * @param mods - Array of raw mod objects as returned by the IPC layer.
 * @returns A normalized array of {@link InstalledModInfo} objects.
 */
export function normalizeInstalledMods(mods: unknown[]): InstalledModInfo[] {
  return (mods || []).map((m: unknown) => {
    const mod = m as Record<string, unknown>;

    const curseForgeIdRaw = mod.curseForgeId || mod.CurseForgeId || (typeof mod.id === 'string' && (mod.id as string).startsWith('cf-') ? (mod.id as string).replace('cf-', '') : undefined);
    let curseForgeId: number | undefined;
    if (typeof curseForgeIdRaw === 'number' && Number.isFinite(curseForgeIdRaw)) {
      curseForgeId = curseForgeIdRaw;
    } else if (typeof curseForgeIdRaw === 'string' && curseForgeIdRaw.trim()) {
      const parsed = Number(curseForgeIdRaw.replace(/^cf-/i, ''));
      if (Number.isFinite(parsed)) curseForgeId = parsed;
    }

    const fileIdRaw = mod.fileId ?? mod.FileId;
    const latestFileIdRaw = mod.latestFileId ?? mod.LatestFileId;
    const fileId = typeof fileIdRaw === 'number' ? fileIdRaw : typeof fileIdRaw === 'string' ? Number(fileIdRaw) : undefined;
    const latestFileId = typeof latestFileIdRaw === 'number' ? latestFileIdRaw : typeof latestFileIdRaw === 'string' ? Number(latestFileIdRaw) : undefined;

    return {
      id: mod.id as string,
      name: mod.name as string || mod.Name as string || '',
      slug: mod.slug as string,
      version: mod.version as string || mod.Version as string || '',
      fileName: (mod.fileName as string) || (mod.FileName as string) || '',
      author: mod.author as string || mod.Author as string || '',
      description: mod.description as string || mod.Description as string || mod.summary as string || '',
      enabled: mod.enabled as boolean ?? true,
      iconUrl: mod.iconUrl as string || mod.IconUrl as string || mod.iconURL as string || '',
      curseForgeId,
      fileId: typeof fileId === 'number' && Number.isFinite(fileId) ? fileId : undefined,
      latestVersion: mod.latestVersion as string || mod.LatestVersion as string,
      latestFileId: typeof latestFileId === 'number' && Number.isFinite(latestFileId) ? latestFileId : undefined,
    } as InstalledModInfo;
  });
}

/**
 * Options accepted by the {@link useModManager} hook.
 */
export interface UseModManagerOptions {
  selectedInstance: InstalledVersionInfo | null;
  setMessage: (msg: { type: 'success' | 'error'; text: string } | null) => void;
  t: (key: string, params?: Record<string, unknown>) => string;
}

/**
 * Manages installed-mod state for the currently selected game instance,
 * including loading, filtering, selection, update-checking, and deletion.
 *
 * @param options - See {@link UseModManagerOptions}.
 * @returns The complete mod-manager state and handler bag.
 */
export function useModManager({ selectedInstance, setMessage, t }: UseModManagerOptions) {
  const [installedMods, setInstalledMods] = useState<InstalledModInfo[]>([]);
  const [isLoadingMods, setIsLoadingMods] = useState(false);
  const [modsWithUpdates, setModsWithUpdates] = useState<InstalledModInfo[]>([]);
  const [updateCount, setUpdateCount] = useState(0);
  const [modsSearchQuery, setModsSearchQuery] = useState('');
  const [selectedMods, setSelectedMods] = useState<Set<string>>(new Set());
  const [modDetailsCache, setModDetailsCache] = useState<Record<string, CurseForgeModInfo | null>>({});
  
  const modsLoadInFlightRef = useRef(0);
  const installedModsSignatureRef = useRef('');
  const updatesSignatureRef = useRef('');
  const modsLoadSeqRef = useRef(0);
  const selectedInstanceRef = useRef<InstalledVersionInfo | null>(null);
  const contentSelectionAnchorRef = useRef<number | null>(null);

  // Keep ref in sync
  useEffect(() => {
    selectedInstanceRef.current = selectedInstance;
  }, [selectedInstance]);

  // Clear selection on instance change
  useEffect(() => {
    setSelectedMods(new Set());
    contentSelectionAnchorRef.current = null;
  }, [selectedInstance?.id]);

  const buildModSignature = useCallback((mods: InstalledModInfo[]): string => {
    return (mods || [])
      .map((m) => {
        const parts = [
          m.id ?? '',
          m.enabled ? '1' : '0',
          m.name ?? '',
          m.author ?? '',
          m.version ?? '',
          m.fileName ?? '',
          m.slug ?? '',
          m.iconUrl ?? '',
          m.curseForgeId ?? '',
          m.fileId ?? '',
          m.latestVersion ?? '',
          m.latestFileId ?? '',
        ];
        return parts.join('\u0001');
      })
      .join('\u0002');
  }, []);

  const loadInstalledMods = useCallback(async (options?: { silent?: boolean }) => {
    const silent = !!options?.silent;

    if (!selectedInstance) {
      if (!silent) {
        setInstalledMods([]);
        installedModsSignatureRef.current = '';
        setModsWithUpdates([]);
        updatesSignatureRef.current = '';
        setUpdateCount(0);
      }
      return;
    }

    if (silent && modsLoadInFlightRef.current > 0) return;

    const currentInstance = selectedInstance;
    const requestSeq = ++modsLoadSeqRef.current;
    modsLoadInFlightRef.current += 1;
    if (!silent) setIsLoadingMods(true);

    try {
      const mods = await getInstanceInstalledMods(currentInstance.id);
      const normalized = normalizeInstalledMods(mods || []);

      if (selectedInstanceRef.current?.id === currentInstance.id && modsLoadSeqRef.current === requestSeq) {
        const nextSig = buildModSignature(normalized);
        if (!silent || nextSig !== installedModsSignatureRef.current) {
          setInstalledMods(normalized);
          installedModsSignatureRef.current = nextSig;
        }

        // Check updates in background
        void (async () => {
          try {
            const updates = await checkInstanceModUpdates(currentInstance.id);
            const normalizedUpdates = normalizeInstalledMods(updates || []);

            if (selectedInstanceRef.current?.id === currentInstance.id && modsLoadSeqRef.current === requestSeq) {
              const nextUpdatesSig = buildModSignature(normalizedUpdates);
              if (nextUpdatesSig !== updatesSignatureRef.current) {
                setModsWithUpdates(normalizedUpdates);
                setUpdateCount(normalizedUpdates.length);
                updatesSignatureRef.current = nextUpdatesSig;
              }
            }
          } catch {
            if (!silent && selectedInstanceRef.current?.id === currentInstance.id && modsLoadSeqRef.current === requestSeq) {
              setModsWithUpdates([]);
              updatesSignatureRef.current = '';
              setUpdateCount(0);
            }
          }
        })();
      }
    } catch (err) {
      console.error('Failed to load installed mods:', err);
      if (!silent && selectedInstanceRef.current?.id === currentInstance.id && modsLoadSeqRef.current === requestSeq) {
        setInstalledMods([]);
        installedModsSignatureRef.current = '';
        setModsWithUpdates([]);
        updatesSignatureRef.current = '';
        setUpdateCount(0);
      }
    } finally {
      if (!silent) setIsLoadingMods(false);
      modsLoadInFlightRef.current = Math.max(0, modsLoadInFlightRef.current - 1);
    }
  }, [buildModSignature, selectedInstance]);

  // Filter mods by search query
  const filteredMods = useMemo(() => {
    if (!modsSearchQuery.trim()) return installedMods;
    const query = modsSearchQuery.toLowerCase();
    return installedMods.filter(mod =>
      mod.name.toLowerCase().includes(query) ||
      mod.author?.toLowerCase().includes(query)
    );
  }, [installedMods, modsSearchQuery]);

  // Selection handlers
  const toggleModSelection = useCallback((modId: string, index: number) => {
    setSelectedMods((prev) => {
      const next = new Set(prev);
      if (next.has(modId)) {
        next.delete(modId);
      } else {
        next.add(modId);
      }
      return next;
    });
    contentSelectionAnchorRef.current = index;
  }, []);

  const handleShiftSelect = useCallback((index: number) => {
    if (filteredMods.length === 0) return;

    const anchor = contentSelectionAnchorRef.current ?? index;
    const start = Math.min(anchor, index);
    const end = Math.max(anchor, index);
    const ids = filteredMods.slice(start, end + 1).map((mod) => mod.id);

    setSelectedMods(new Set(ids));
  }, [filteredMods]);

  const selectOnlyMod = useCallback((modId: string, index: number) => {
    setSelectedMods(new Set([modId]));
    contentSelectionAnchorRef.current = index;
  }, []);

  const selectAllMods = useCallback(() => {
    const allIds = filteredMods.map((m) => m.id);
    const alreadyAllSelected = allIds.length > 0 && allIds.every((id) => selectedMods.has(id));
    setSelectedMods(alreadyAllSelected ? new Set() : new Set(allIds));
  }, [filteredMods, selectedMods]);

  const clearSelection = useCallback(() => {
    setSelectedMods(new Set());
    contentSelectionAnchorRef.current = null;
  }, []);

  // Mod deletion
  const handleDeleteMod = useCallback(async (mod: InstalledModInfo) => {
    if (!selectedInstance) return false;
    try {
      await uninstallInstanceMod(mod.id, selectedInstance.id);
      await loadInstalledMods();
      setMessage({ type: 'success', text: t('modManager.modDeleted') });
      setTimeout(() => setMessage(null), 3000);
      return true;
    } catch {
      setMessage({ type: 'error', text: t('modManager.deleteFailed') });
      return false;
    }
  }, [loadInstalledMods, selectedInstance, setMessage, t]);

  const handleBulkDeleteMods = useCallback(async (ids?: Iterable<string>) => {
    if (!selectedInstance) return false;

    const idsToDelete = Array.from(ids ?? selectedMods);
    if (idsToDelete.length === 0) return false;

    try {
      for (const modId of idsToDelete) {
        await uninstallInstanceMod(modId, selectedInstance.id);
      }
      setSelectedMods(new Set());
      await loadInstalledMods();
      setMessage({ type: 'success', text: t('modManager.modsDeleted') });
      setTimeout(() => setMessage(null), 3000);
      return true;
    } catch {
      setMessage({ type: 'error', text: t('modManager.deleteFailed') });
      return false;
    }
  }, [loadInstalledMods, selectedInstance, selectedMods, setMessage, t]);

  // Toggle mod enabled state
  const handleToggleMod = useCallback(async (mod: InstalledModInfo) => {
    if (!selectedInstance) return;
    try {
      const ok = await ipc.mods.toggle({
        modId: mod.id,
        instanceId: selectedInstance.id,
      });
      if (ok) {
        setInstalledMods(prev =>
          prev.map(m => (m.id === mod.id ? { ...m, enabled: !m.enabled } : m))
        );
      }
    } catch (err) {
      console.warn('[IPC] ToggleMod:', err);
    }
  }, [selectedInstance]);

  const handleBulkToggleMods = useCallback(async (desiredEnabled: boolean) => {
    if (!selectedInstance || selectedMods.size === 0) return;

    const selectedVisibleMods = filteredMods.filter((m) => selectedMods.has(m.id));
    if (selectedVisibleMods.length === 0) return;

    const modsNeedingChange = selectedVisibleMods.filter((m) => Boolean(m.enabled) !== desiredEnabled);
    if (modsNeedingChange.length === 0) {
      setMessage({
        type: 'success',
        text: desiredEnabled ? t('modManager.modsEnabled') : t('modManager.modsDisabled'),
      });
      setTimeout(() => setMessage(null), 2500);
      return;
    }

    try {
      for (const mod of modsNeedingChange) {
        await ipc.mods.toggle({
          modId: mod.id,
          instanceId: selectedInstance.id,
        });
      }

      const changedIds = new Set(modsNeedingChange.map((m) => m.id));
      setInstalledMods((prev) => prev.map((m) => (changedIds.has(m.id) ? { ...m, enabled: desiredEnabled } : m)));

      setMessage({
        type: 'success',
        text: desiredEnabled ? t('modManager.modsEnabled') : t('modManager.modsDisabled'),
      });
      setTimeout(() => setMessage(null), 3000);
    } catch {
      setMessage({ type: 'error', text: t('modManager.toggleFailed') });
      setTimeout(() => setMessage(null), 3000);
    }
  }, [filteredMods, selectedInstance, selectedMods, setMessage, t]);

  // Bulk update mods
  const bulkUpdateList = useMemo(() => {
    return (modsWithUpdates || []).filter((m) => typeof m.latestFileId === 'number' && Number.isFinite(m.latestFileId));
  }, [modsWithUpdates]);

  const handleBulkUpdateMods = useCallback(async (selectedIds: Set<string>) => {
    if (!selectedInstance || bulkUpdateList.length === 0) return false;

    const modsToUpdate = bulkUpdateList.filter((m) => selectedIds.has(m.id) && typeof m.latestFileId === 'number' && Number.isFinite(m.latestFileId));
    if (modsToUpdate.length === 0) return false;

    let failed = 0;
    for (const mod of modsToUpdate) {
      try {
        const cfId = mod.curseForgeId ? String(mod.curseForgeId) : 
          (typeof mod.id === 'string' && mod.id.startsWith('cf-') ? mod.id.replace('cf-', '') : mod.id);
        const fileId = String(mod.latestFileId);
        const result = await ipc.mods.install({
          modId: cfId,
          fileId,
          instanceId: selectedInstance.id,
        });
        const ok = typeof result === 'object' && result !== null ? (result as { success: boolean }).success : result;
        if (!ok) failed++;
      } catch {
        failed++;
      }
    }

    await loadInstalledMods();
    if (failed > 0) {
      setMessage({ type: 'error', text: `${t('modManager.toggleFailed')} (${failed}/${modsToUpdate.length})` });
      setTimeout(() => setMessage(null), 3500);
      return false;
    } else {
      setMessage({ type: 'success', text: t('modManager.updating') });
      setTimeout(() => setMessage(null), 2500);
      return true;
    }
  }, [bulkUpdateList, loadInstalledMods, selectedInstance, setMessage, t]);

  return {
    // State
    installedMods,
    setInstalledMods,
    isLoadingMods,
    modsWithUpdates,
    updateCount,
    modsSearchQuery,
    setModsSearchQuery,
    selectedMods,
    setSelectedMods,
    filteredMods,
    bulkUpdateList,
    modDetailsCache,
    setModDetailsCache,
    
    // Actions
    loadInstalledMods,
    handleDeleteMod,
    handleBulkDeleteMods,
    handleToggleMod,
    handleBulkToggleMods,
    handleBulkUpdateMods,
    
    // Selection
    toggleModSelection,
    handleShiftSelect,
    selectOnlyMod,
    selectAllMods,
    clearSelection,
    contentSelectionAnchorRef,
  };
}
