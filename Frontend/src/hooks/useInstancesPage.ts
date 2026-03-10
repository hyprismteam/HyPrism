import { useState, useEffect, useCallback, useMemo, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { ipc, InstalledInstance, type ModInfo as CurseForgeModInfo, type SaveInfo } from '@/lib/ipc';
import type { InstalledVersionInfo, InstanceTab, InstalledModInfo } from '@/types';
import { GameBranch } from '@/constants/enums';
import { 
  useInstanceActions, 
  useModManager,
  getCustomInstanceDir,
  getInstanceSaves,
  getInstanceIcon as getInstanceIconIpc,
} from '@/hooks';

// #region Types

/**
 * Options accepted by the {@link useInstancesPage} hook.
 */
export interface UseInstancesPageOptions {
  onInstanceDeleted?: () => void;
  onInstanceSelected?: () => void;
  isGameRunning?: boolean;
  runningInstanceId?: string;
  onStopGame?: () => void;
  activeTab?: InstanceTab;
  onTabChange?: (tab: InstanceTab) => void;
  isDownloading?: boolean;
  downloadingInstanceId?: string;
  onLaunchInstance?: (instanceId: string) => void;
}

/**
 * A transient status message displayed to the user.
 */
export interface MessageState {
  type: 'success' | 'error';
  text: string;
}

// #endregion

// #region Helpers

/**
 * Converts a raw {@link InstalledInstance} from the IPC layer into the
 * UI-friendly {@link InstalledVersionInfo} shape.
 *
 * @param inst - The raw installed instance data.
 * @returns A normalized {@link InstalledVersionInfo} object.
 */
export const toVersionInfo = (inst: InstalledInstance): InstalledVersionInfo => ({
  id: inst.id,
  branch: inst.branch,
  version: inst.version,
  path: inst.path,
  sizeBytes: inst.totalSize,
  isLatest: false,
  isLatestInstance: inst.version === 0,
  iconPath: undefined,
  validationStatus: inst.validationStatus,
  validationDetails: inst.validationDetails,
  customName: inst.customName,
});

// #endregion

// #region Main Hook

/**
 * Manages all state and business logic for the Instances page, including
 * instance listing, mod management, saves, icon loading, and download tracking.
 *
 * @param options - Configuration options including lifecycle callbacks and
 *   controlled state for game running / download status.
 * @returns The complete instances-page state and handler bag.
 */
export function useInstancesPage(options: UseInstancesPageOptions) {
  const {
    onInstanceDeleted,
    onInstanceSelected,
    isGameRunning = false,
    runningInstanceId,
    onStopGame,
    activeTab: controlledTab,
    onTabChange,
    isDownloading = false,
    downloadingInstanceId,
    onLaunchInstance,
  } = options;

  const { t } = useTranslation();

  // #region Core State
  
  const [instances, setInstances] = useState<InstalledVersionInfo[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [instanceDir, setInstanceDir] = useState('');
  const [selectedInstance, setSelectedInstance] = useState<InstalledVersionInfo | null>(null);
  const selectedInstanceRef = useRef<InstalledVersionInfo | null>(null);
  const [message, setMessage] = useState<MessageState | null>(null);

  // #endregion

  // #region Tab State
  
  const [localTab, setLocalTab] = useState<InstanceTab>(controlledTab ?? 'content');
  const activeTab = controlledTab ?? localTab;
  
  const setActiveTab = useCallback((tab: InstanceTab) => {
    onTabChange?.(tab);
    setLocalTab(tab);
  }, [onTabChange]);
  
  const tabs: InstanceTab[] = ['content', 'browse', 'worlds'];

  // #endregion

  // #region Modal States
  
  const [instanceToDelete, setInstanceToDelete] = useState<InstalledVersionInfo | null>(null);
  const [modToDelete, setModToDelete] = useState<InstalledModInfo | null>(null);
  const [showBulkUpdateConfirm, setShowBulkUpdateConfirm] = useState(false);
  const [showBulkDeleteConfirm, setShowBulkDeleteConfirm] = useState(false);
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [showEditModal, setShowEditModal] = useState(false);
  const [exportingInstance, setExportingInstance] = useState<string | null>(null);
  const [isImporting, setIsImporting] = useState(false);
  const [isDeletingMod, setIsDeletingMod] = useState(false);
  const [isBulkTogglingMods] = useState(false);
  const [isUpdatingMods, setIsUpdatingMods] = useState(false);

  // #endregion

  // #region Instance Menu State
  
  const [showInstanceMenu, setShowInstanceMenu] = useState(false);
  const instanceMenuRef = useRef<HTMLDivElement>(null);
  const [inlineMenuInstanceId, setInlineMenuInstanceId] = useState<string | null>(null);
  const inlineMenuRef = useRef<HTMLDivElement>(null);

  // #endregion

  // #region Saves State
  
  const [saves, setSaves] = useState<SaveInfo[]>([]);
  const [isLoadingSaves, setIsLoadingSaves] = useState(false);

  // #endregion

  // #region Instance Icons
  
  const [instanceIcons, setInstanceIcons] = useState<Record<string, string>>({});
  const iconLoadSeqRef = useRef(0);

  // #endregion

  // #region Browse Tab State
  
  const [browseRefreshSignal, setBrowseRefreshSignal] = useState(0);
  const hasOpenedBrowseRef = useRef(false);

  // #endregion

  // #region Instance Actions Hook
  
  const instanceActions = useInstanceActions(setMessage, loadInstances, t);

  // #endregion

  // #region Mod Manager Hook
  
  const modManager = useModManager({
    selectedInstance,
    setMessage,
    t,
  });

  // #endregion

  // #region Ref Sync
  
  useEffect(() => {
    selectedInstanceRef.current = selectedInstance;
  }, [selectedInstance]);

  // #endregion

  // #region Load Instances
  
  // eslint-disable-next-line react-hooks/exhaustive-deps
  function loadInstances() {
    return loadInstancesAsync();
  }

  async function loadInstancesAsync() {
    setIsLoading(true);
    try {
      const [data, selected] = await Promise.all([
        ipc.game.instances(),
        ipc.instance.getSelected()
      ]);
      const instanceList = (data || []).map(toVersionInfo);
      setInstances(instanceList);

      const currentSelected = selectedInstanceRef.current;
      if (selected && instanceList.length > 0) {
        const found = instanceList.find(inst => inst.id === selected.id);
        if (found) {
          setSelectedInstance(found);
        } else if (!currentSelected) {
          setSelectedInstance(instanceList[0]);
        }
      } else if (instanceList.length > 0 && !currentSelected) {
        setSelectedInstance(instanceList[0]);
      }
    } catch (err) {
      console.error('Failed to load instances:', err);
    }
    setIsLoading(false);
  }

  useEffect(() => {
    loadInstances();
    getCustomInstanceDir().then(dir => dir && setInstanceDir(dir)).catch(() => {});
  }, []);

  // Reload when download finishes
  const prevDownloadingRef = useRef(isDownloading);
  useEffect(() => {
    if (prevDownloadingRef.current && !isDownloading) {
      loadInstances();
    }
    prevDownloadingRef.current = isDownloading;
  }, [isDownloading]);

  // #endregion

  // #region Load Mods Effect
  
  useEffect(() => {
    modManager.loadInstalledMods();
  }, [selectedInstance?.id]);

  // Auto-refresh mods
  useEffect(() => {
    if (!selectedInstance) return;
    if (activeTab !== 'content') return;
    if (selectedInstance.validationStatus !== 'Valid') return;

    const intervalId = window.setInterval(() => {
      if (showBulkUpdateConfirm || showBulkDeleteConfirm) return;
      if (modToDelete || instanceToDelete) return;
      void modManager.loadInstalledMods({ silent: true });
    }, 4000);

    return () => window.clearInterval(intervalId);
  }, [activeTab, instanceToDelete, modToDelete, selectedInstance, showBulkDeleteConfirm, showBulkUpdateConfirm, modManager]);

  // #endregion

  // #region Load Saves
  
  const loadSaves = useCallback(async () => {
    if (!selectedInstance) {
      setSaves([]);
      return;
    }
    setIsLoadingSaves(true);
    try {
      const savesData = await getInstanceSaves(selectedInstance.id);
      setSaves(savesData || []);
    } catch (err) {
      console.error('Failed to load saves:', err);
      setSaves([]);
    }
    setIsLoadingSaves(false);
  }, [selectedInstance]);

  useEffect(() => {
    if (activeTab === 'worlds') {
      loadSaves();
    }
  }, [loadSaves, activeTab]);

  // Clear selection on tab/instance change
  useEffect(() => {
    modManager.clearSelection();
  }, [activeTab, selectedInstance?.id]);

  // #endregion

  // #region Load Instance Icons
  
  const loadAllInstanceIcons = useCallback(async () => {
    const requestSeq = ++iconLoadSeqRef.current;
    if (instances.length === 0) {
      setInstanceIcons({});
      return;
    }

    try {
      const nextIcons: Record<string, string> = {};
      for (const inst of instances) {
        if (requestSeq !== iconLoadSeqRef.current) return;
        if (!inst.id) continue;
        const icon = await getInstanceIconIpc(inst.id);
        if (icon) {
          const suffix = icon.includes('?') ? '&' : '?';
          nextIcons[inst.id] = `${icon}${suffix}ts=${Date.now()}`;
        }
      }
      if (requestSeq !== iconLoadSeqRef.current) return;
      setInstanceIcons(nextIcons);
    } catch (err) {
      console.error('Failed to refresh instance icons:', err);
    }
  }, [instances]);

  useEffect(() => {
    loadAllInstanceIcons();
  }, [loadAllInstanceIcons]);

  // #endregion

  // #region Browse Tab Initialization
  
  useEffect(() => {
    if (activeTab !== 'browse') return;
    if (hasOpenedBrowseRef.current) return;
    hasOpenedBrowseRef.current = true;
    setBrowseRefreshSignal((v) => v + 1);
  }, [activeTab]);

  // #endregion

  // #region Click Outside Handlers
  
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (instanceMenuRef.current && !instanceMenuRef.current.contains(e.target as Node)) {
        setShowInstanceMenu(false);
      }
      if (inlineMenuRef.current && !inlineMenuRef.current.contains(e.target as Node)) {
        setInlineMenuInstanceId(null);
      }
    };
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  // #endregion

  // #region Display Helpers
  
  const getInstanceDisplayName = useCallback((inst: InstalledVersionInfo) => {
    if (inst.customName) return inst.customName;
    
    const branchLabel = inst.branch === GameBranch.RELEASE
      ? t('modManager.releaseType.release')
      : inst.branch === GameBranch.PRE_RELEASE
        ? t('common.preRelease')
        : t('modManager.releaseType.release');
    
    if (inst.isLatestInstance) {
      return `${branchLabel} (${t('common.latest')})`;
    }
    return `${branchLabel} v${inst.version}`;
  }, [t]);

  const getValidationInfo = useCallback((inst: InstalledVersionInfo) => {
    const status = inst.validationStatus || 'Unknown';
    
    switch (status) {
      case 'Valid':
        return {
          status: 'valid' as const,
          label: t('instances.status.ready'),
          color: '#22c55e',
          bgColor: 'rgba(34, 197, 94, 0.1)',
        };
      case 'NotInstalled':
        return {
          status: 'error' as const,
          label: t('instances.status.notInstalled'),
          color: '#ef4444',
          bgColor: 'rgba(239, 68, 68, 0.1)',
        };
      case 'Corrupted':
        return {
          status: 'error' as const,
          label: t('instances.status.corrupted'),
          color: '#ef4444',
          bgColor: 'rgba(239, 68, 68, 0.1)',
        };
      default:
        return {
          status: 'warning' as const,
          label: t('instances.status.unknown'),
          color: '#6b7280',
          bgColor: 'rgba(107, 114, 128, 0.1)',
        };
    }
  }, [t]);

  const getTabLabel = useCallback((tab: InstanceTab) => {
    if (tab === 'content') return t('instances.tab.content');
    return t(`instances.tab.${tab}`);
  }, [t]);

  // #endregion

  // #region Mod Helpers
  
  const isLocalInstalledMod = useCallback((mod: InstalledModInfo): boolean => {
    if (typeof mod.id === 'string' && mod.id.startsWith('local-')) return true;
    if (String(mod.version || '').toLowerCase() === 'local') return true;
    if (String(mod.author || '').toLowerCase() === 'local file') return true;
    return false;
  }, []);

  const isTrustedRemoteIdentity = useCallback((mod: InstalledModInfo): boolean => {
    if (!isLocalInstalledMod(mod)) return true;
    if (mod.curseForgeId != null) return true;
    if (mod.slug && mod.slug.trim()) return true;
    return false;
  }, [isLocalInstalledMod]);

  const getLocalFileStem = useCallback((fileName?: string): string => {
    const n = String(fileName || '').trim();
    if (!n) return '';
    const withoutDisabled = n.replace(/\.disabled$/i, '');
    return withoutDisabled.replace(/\.(jar|zip)$/i, '');
  }, []);

  const extractLocalVersionFromStem = useCallback((stem: string): string => {
    const s = String(stem || '').trim();
    if (!s) return '';
    const m = s.match(/(?:^|[\s_-]+)(v?\d+(?:\.\d+){0,4}(?:[-_.]?(?:alpha|beta|rc)\d*)?|\d{4}\.\d+(?:\.\d+)?|v\d+|V\d+)$/i);
    return m?.[1] ?? '';
  }, []);

  const getDisplayVersion = useCallback((mod: InstalledModInfo): string => {
    const v = String(mod.version || '').trim();
    if (!isLocalInstalledMod(mod)) return v || '-';
    if (v && v.toLowerCase() !== 'local') return v;
    const stem = getLocalFileStem(mod.fileName);
    const fromName = extractLocalVersionFromStem(stem);
    return fromName || '-';
  }, [extractLocalVersionFromStem, getLocalFileStem, isLocalInstalledMod]);

  const getCurseForgeUrlFromDetails = useCallback((details: CurseForgeModInfo | null | undefined): string | null => {
    if (!details) return null;
    if (details.slug) return `https://www.curseforge.com/hytale/mods/${details.slug}`;
    if (details.id) return `https://www.curseforge.com/hytale/mods/${String(details.id)}`;
    return null;
  }, []);

  const getCurseForgeUrl = useCallback((mod: InstalledModInfo): string => {
    if (mod.slug) return `https://www.curseforge.com/hytale/mods/${mod.slug}`;
    if (isLocalInstalledMod(mod)) {
      return `https://www.curseforge.com/hytale/mods/search?search=${encodeURIComponent(String(mod.name || ''))}`;
    }
    if (mod.curseForgeId != null) return `https://www.curseforge.com/hytale/mods/${String(mod.curseForgeId)}`;
    const id = (typeof mod.id === 'string' && mod.id.startsWith('cf-') ? mod.id.replace('cf-', '') : mod.id);
    return `https://www.curseforge.com/hytale/mods/search?search=${encodeURIComponent(String(id || mod.name))}`;
  }, [isLocalInstalledMod]);

  const handleOpenModPage = useCallback((e: React.MouseEvent, mod: InstalledModInfo) => {
    e.preventDefault();
    e.stopPropagation();
    const cached = modManager.modDetailsCache[mod.id];
    const cachedUrl = getCurseForgeUrlFromDetails(cached);
    if (cachedUrl) {
      ipc.browser.open(cachedUrl);
      return;
    }
    ipc.browser.open(getCurseForgeUrl(mod));
  }, [getCurseForgeUrl, getCurseForgeUrlFromDetails, modManager.modDetailsCache]);

  const getCurseForgeModId = useCallback((mod: InstalledModInfo): string => {
    if (typeof mod.curseForgeId === 'number' && Number.isFinite(mod.curseForgeId)) return String(mod.curseForgeId);
    if (typeof mod.id === 'string' && mod.id.startsWith('cf-')) return mod.id.replace('cf-', '');
    return mod.id;
  }, []);

  // #endregion

  // #region Prefetch Mod Details
  
  const prefetchModDetails = useCallback(async (mods: InstalledModInfo[]) => {
    const toFetch = mods.filter((m) => modManager.modDetailsCache[m.id] === undefined);
    if (toFetch.length === 0) return;

    for (const mod of toFetch) {
      try {
        if (!isLocalInstalledMod(mod) && mod.curseForgeId != null && Number.isFinite(mod.curseForgeId)) {
          const info = await ipc.mods.info({ modId: String(mod.curseForgeId) });
          if (info && String(info.id || '').trim()) {
            modManager.setModDetailsCache((prev) => ({ ...prev, [mod.id]: info }));
          } else {
            modManager.setModDetailsCache((prev) => ({ ...prev, [mod.id]: null }));
          }
          continue;
        }
        modManager.setModDetailsCache((prev) => ({ ...prev, [mod.id]: null }));
      } catch {
        // ignore
      }
    }
  }, [isLocalInstalledMod, modManager]);

  // Enrich installed mods with details
  useEffect(() => {
    if (modManager.installedMods.length === 0) return;
    const toEnrich = modManager.installedMods.filter((m) => !m.iconUrl || !m.slug);
    void prefetchModDetails(toEnrich);
  }, [modManager.installedMods, prefetchModDetails]);

  // #endregion

  // #region Instance Launch Handler
  
  const handleLaunchInstance = useCallback((inst: InstalledVersionInfo) => {
    const isLikelyRunningThis = isGameRunning && (!runningInstanceId || runningInstanceId === inst.id);

    if (isLikelyRunningThis) {
      onStopGame?.();
    } else {
      onLaunchInstance?.(inst.id);
    }
  }, [isGameRunning, onLaunchInstance, onStopGame, runningInstanceId]);

  // #endregion

  // #region Delete Mod Handler
  
  const handleDeleteMod = useCallback(async () => {
    if (!modToDelete) return;
    setIsDeletingMod(true);
    await modManager.handleDeleteMod(modToDelete);
    setModToDelete(null);
    setIsDeletingMod(false);
  }, [modManager, modToDelete]);

  // #endregion

  // #region Drop Import Mods Handler
  
  const handleDropImportMods = useCallback(async (files: FileList | File[]) => {
    if (!selectedInstance) return;
    const list = Array.from(files || []);
    if (list.length === 0) return;

    // Only allow .jar files (and .disabled for disabled mods)
    const allowedExt = new Set(['.jar', '.disabled']);
    const maxBytes = 100 * 1024 * 1024;

    let okCount = 0;
    let failCount = 0;
    let skippedType = 0;
    let skippedSize = 0;

    const readFileAsBase64 = (file: File): Promise<string> => {
      return new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => {
          const ab = reader.result as ArrayBuffer;
          const bytes = new Uint8Array(ab);
          let binary = '';
          const chunkSize = 0x2000;
          for (let i = 0; i < bytes.length; i += chunkSize) {
            binary += String.fromCharCode(...bytes.subarray(i, i + chunkSize));
          }
          resolve(btoa(binary));
        };
        reader.onerror = () => reject(reader.error);
        reader.readAsArrayBuffer(file);
      });
    };

    for (const file of list) {
      try {
        const name = String(file.name || '');
        const lower = name.toLowerCase();
        const dot = lower.lastIndexOf('.');
        const ext = dot >= 0 ? lower.slice(dot) : '';
        if (!allowedExt.has(ext)) {
          skippedType++;
          continue;
        }
        if (typeof file.size === 'number' && file.size > maxBytes) {
          skippedSize++;
          continue;
        }

        const base64Content = await readFileAsBase64(file);
        const ok = await ipc.mods.installBase64({
          fileName: file.name,
          base64Content,
          instanceId: selectedInstance.id,
        });
        if (ok) okCount++;
        else failCount++;
      } catch {
        failCount++;
      }
    }

    await modManager.loadInstalledMods();

    const skippedTotal = skippedType + skippedSize;
    if (failCount === 0 && skippedTotal === 0) {
      setMessage({ type: 'success', text: `Imported ${okCount} mod(s)` });
    } else if (failCount === 0) {
      setMessage({ type: 'success', text: `Imported ${okCount} mod(s), skipped ${skippedTotal}` });
    } else {
      setMessage({ type: 'error', text: `Imported ${okCount} mod(s), failed ${failCount}, skipped ${skippedTotal}` });
    }
    setTimeout(() => setMessage(null), 3000);
  }, [modManager, selectedInstance]);

  // #endregion

  // #region Bulk Delete List
  
  const bulkDeleteList = useMemo(() => {
    if (modManager.selectedMods.size === 0) return [];
    return modManager.filteredMods.filter((m) => modManager.selectedMods.has(m.id));
  }, [modManager.filteredMods, modManager.selectedMods]);

  // #endregion

  // #region Select Instance Handler
  
  const handleSelectInstance = useCallback((inst: InstalledVersionInfo) => {
    setSelectedInstance(inst);
    setInlineMenuInstanceId(null);
    if (inst.id) {
      ipc.instance.select({ id: inst.id }).then(() => onInstanceSelected?.()).catch(console.error);
    }
  }, [onInstanceSelected]);

  // #endregion

  // #region Context Menu Handler
  
  const handleContextMenuInstance = useCallback((inst: InstalledVersionInfo) => {
    setInlineMenuInstanceId(inst.id);
    setShowInstanceMenu(false);
  }, []);

  // #endregion

  // #region Return
  
  return {
    // Core state
    instances,
    isLoading,
    instanceDir,
    selectedInstance,
    setSelectedInstance,
    message,
    setMessage,
    
    // Tab state
    activeTab,
    setActiveTab,
    tabs,
    
    // Modal states
    instanceToDelete,
    setInstanceToDelete,
    modToDelete,
    setModToDelete,
    showBulkUpdateConfirm,
    setShowBulkUpdateConfirm,
    showBulkDeleteConfirm,
    setShowBulkDeleteConfirm,
    showCreateModal,
    setShowCreateModal,
    showEditModal,
    setShowEditModal,
    exportingInstance,
    setExportingInstance,
    isImporting,
    setIsImporting,
    isDeletingMod,
    setIsDeletingMod,
    isBulkTogglingMods,
    isUpdatingMods,
    setIsUpdatingMods,
    
    // Instance menu
    showInstanceMenu,
    setShowInstanceMenu,
    instanceMenuRef,
    inlineMenuInstanceId,
    setInlineMenuInstanceId,
    inlineMenuRef,
    
    // Saves
    saves,
    isLoadingSaves,
    loadSaves,
    
    // Icons
    instanceIcons,
    setInstanceIcons,
    
    // Browse tab
    browseRefreshSignal,
    
    // Instance actions
    instanceActions,
    loadInstances,
    
    // Mod manager
    modManager,
    
    // Display helpers
    getInstanceDisplayName,
    getValidationInfo,
    getTabLabel,
    
    // Mod helpers
    isLocalInstalledMod,
    isTrustedRemoteIdentity,
    getDisplayVersion,
    getCurseForgeUrlFromDetails,
    handleOpenModPage,
    getCurseForgeModId,
    prefetchModDetails,
    
    // Handlers
    handleLaunchInstance,
    handleDeleteMod,
    handleDropImportMods,
    handleSelectInstance,
    handleContextMenuInstance,
    
    // Computed
    bulkDeleteList,
    
    // Passed options for convenience
    isGameRunning,
    runningInstanceId,
    isDownloading,
    downloadingInstanceId,
    onInstanceDeleted,
  };
  // #endregion
}

// #endregion

/** Full return type of the {@link useInstancesPage} hook. */
export type UseInstancesPageReturn = ReturnType<typeof useInstancesPage>;
