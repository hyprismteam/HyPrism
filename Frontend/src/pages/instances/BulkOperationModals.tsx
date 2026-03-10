import React, { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import { 
  RefreshCw, Trash2, X, Package, ChevronDown, 
  Loader2
} from 'lucide-react';
import { useAccentColor } from '@/contexts/AccentColorContext';
import { invoke, type ModInfo as CurseForgeModInfo, type ModScreenshot } from '@/lib/ipc';
import { Button, IconButton, SelectableCheckbox } from '@/components/ui/Controls';
import type { InstalledModInfo } from '@/types';

interface BulkOperationModalProps {
  isOpen: boolean;
  onClose: () => void;
  modList: InstalledModInfo[];
  modDetailsCache: Record<string, CurseForgeModInfo | null>;
  prefetchModDetails: (mods: InstalledModInfo[]) => Promise<void>;
  getCurseForgeModId: (mod: InstalledModInfo) => string;
}

interface BulkUpdateModalProps extends BulkOperationModalProps {
  onUpdate: (selectedIds: Set<string>) => Promise<void>;
  isUpdating: boolean;
}

interface BulkDeleteModalProps extends BulkOperationModalProps {
  onDelete: (selectedIds: Set<string>) => Promise<void>;
  isDeleting: boolean;
}

/**
 * Modal for bulk updating mods with update available
 */
export const BulkUpdateModal: React.FC<BulkUpdateModalProps> = ({
  isOpen,
  onClose,
  modList,
  modDetailsCache,
  prefetchModDetails,
  getCurseForgeModId,
  onUpdate,
  isUpdating,
}) => {
  const { t } = useTranslation();
  useAccentColor(); // For context subscription
  
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [previewId, setPreviewId] = useState<string | null>(null);
  const [openChangelogIds, setOpenChangelogIds] = useState<Set<string>>(new Set());
  const [changelogCache, setChangelogCache] = useState<Record<string, { status: 'idle' | 'loading' | 'ready' | 'error'; text: string }>>({});

  useEffect(() => {
    if (!isOpen) return;
    setSelectedIds(new Set(modList.map((m) => m.id)));
    setPreviewId(modList[0]?.id ?? null);
    void prefetchModDetails(modList);
  }, [isOpen, modList, prefetchModDetails]);

  const loadChangelogFor = useCallback(async (mod: InstalledModInfo) => {
    if (changelogCache[mod.id]?.status === 'loading' || changelogCache[mod.id]?.status === 'ready') return;
    if (typeof mod.latestFileId !== 'number' || !Number.isFinite(mod.latestFileId)) return;

    setChangelogCache((prev) => ({
      ...prev,
      [mod.id]: { status: 'loading', text: '' },
    }));

    try {
      const text = await invoke<string>('hyprism:mods:changelog', {
        modId: getCurseForgeModId(mod),
        fileId: String(mod.latestFileId),
      });

      setChangelogCache((prev) => ({
        ...prev,
        [mod.id]: { status: 'ready', text: (text ?? '').trim() },
      }));
    } catch {
      setChangelogCache((prev) => ({
        ...prev,
        [mod.id]: { status: 'error', text: '' },
      }));
    }
  }, [changelogCache, getCurseForgeModId]);

  useEffect(() => {
    if (!isOpen) return;
    const activeId = previewId ?? modList[0]?.id;
    const active = modList.find((x) => x.id === activeId) ?? modList[0];
    if (!active) return;
    void loadChangelogFor(active);
  }, [isOpen, previewId, modList, loadChangelogFor]);

  const handleSubmit = async () => {
    await onUpdate(selectedIds);
    onClose();
  };

  return (
    <AnimatePresence>
      {isOpen && (
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          className="fixed inset-0 z-[300] flex items-center justify-center bg-[#0a0a0a]/90"
          onClick={(e) => e.target === e.currentTarget && !isUpdating && onClose()}
        >
          <motion.div
            initial={{ scale: 0.95, opacity: 0 }}
            animate={{ scale: 1, opacity: 1 }}
            exit={{ scale: 0.95, opacity: 0 }}
            className="p-6 w-full max-w-4xl mx-4 shadow-2xl glass-panel-static-solid"
          >
            <div className="flex items-start justify-between gap-4 mb-4">
              <div>
                <h3 className="text-white font-bold text-lg flex items-center gap-2">
                  <RefreshCw size={16} className="text-green-400" />
                  {t('modManager.updateAll')}
                </h3>
                <p className="text-white/60 text-sm mt-1">
                  {modList.length > 0
                    ? t('modManager.updatesAvailable', { count: modList.length })
                    : t('modManager.allUpToDate')}
                </p>
              </div>
              <IconButton variant="ghost" onClick={onClose} disabled={isUpdating} title={t('common.close')}>
                <X size={16} />
              </IconButton>
            </div>

            {modList.length > 0 && (
              <div className="max-h-[55vh] overflow-y-auto pr-1 space-y-4">
                <div className="rounded-2xl border border-white/10 bg-[#1c1c1e]/60 overflow-hidden">
                  <div className="p-2 space-y-1">
                    {modList.map((m) => {
                      const details = modDetailsCache[m.id];
                      const ss0 = (details?.screenshots as ModScreenshot[] | undefined)?.[0];
                      const iconUrl = m.iconUrl || details?.iconUrl || details?.thumbnailUrl;
                      const isChecked = selectedIds.has(m.id);
                      const isActive = previewId === m.id;
                      const summary = details?.summary || m.description || '';
                      const isChangelogOpen = openChangelogIds.has(m.id);
                      const changelogState = changelogCache[m.id]?.status ?? 'idle';
                      const changelogText = changelogCache[m.id]?.text ?? '';

                      return (
                        <div key={m.id} className="rounded-xl">
                          <div
                            className={`flex items-center gap-3 p-3 rounded-xl cursor-pointer transition-colors ${
                              isActive ? 'bg-white/5 border border-white/10' : 'hover:bg-white/5'
                            }`}
                            onClick={() => {
                              setPreviewId((prev) => (prev === m.id ? null : m.id));
                              setOpenChangelogIds((prev) => {
                                const next = new Set(prev);
                                next.delete(m.id);
                                return next;
                              });
                            }}
                          >
                            <SelectableCheckbox
                              checked={isChecked}
                              onChange={() => {
                                setSelectedIds((prev) => {
                                  const next = new Set(prev);
                                  if (next.has(m.id)) next.delete(m.id);
                                  else next.add(m.id);
                                  return next;
                                });
                              }}
                              onClick={(e) => e.stopPropagation()}
                              title={t('modManager.selected')}
                            />

                            <div className="w-12 h-12 rounded-lg bg-[#2c2c2e] overflow-hidden flex-shrink-0">
                              {ss0?.thumbnailUrl ? (
                                <img src={ss0.thumbnailUrl} alt="" className="w-full h-full object-cover" loading="lazy" />
                              ) : iconUrl ? (
                                <img src={iconUrl} alt="" className="w-full h-full object-cover" loading="lazy" />
                              ) : (
                                <div className="w-full h-full flex items-center justify-center">
                                  <Package size={18} className="text-white opacity-30" />
                                </div>
                              )}
                            </div>

                            <div className="min-w-0 flex-1">
                              <div className="flex items-center justify-between gap-3">
                                <div className="flex items-center gap-2 min-w-0">
                                  <div className="text-white font-medium truncate">{m.name}</div>
                                  <span className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-green-500/20 text-green-400 flex-shrink-0">
                                    {m.latestVersion || t('modManager.update')}
                                  </span>
                                </div>
                                <div className="text-white/40 text-xs truncate max-w-[55%] text-right">
                                  {m.latestVersion ? `${t('modManager.update')} → ${m.latestVersion}` : t('modManager.update')}
                                </div>
                              </div>
                            </div>
                          </div>

                          {isActive && (
                            <div className="mt-2 px-4 pb-4">
                              <div className="text-white/70 text-sm leading-relaxed">
                                {summary?.trim() ? summary : t('modManager.noDescription')}
                              </div>

                              <div className="mt-3">
                                <button
                                  onClick={async (e) => {
                                    e.stopPropagation();
                                    const willOpen = !openChangelogIds.has(m.id);
                                    setOpenChangelogIds((prev) => {
                                      const next = new Set(prev);
                                      if (next.has(m.id)) next.delete(m.id);
                                      else next.add(m.id);
                                      return next;
                                    });
                                    if (willOpen) {
                                      await loadChangelogFor(m);
                                    }
                                  }}
                                  className="flex items-center gap-2 text-white/70 hover:text-white/85 transition-colors text-xs font-medium"
                                >
                                  <ChevronDown
                                    size={14}
                                    className={`text-white/40 transition-transform ${isChangelogOpen ? 'rotate-180' : ''}`}
                                  />
                                  {t('modManager.viewChangelog')}
                                </button>

                                {isChangelogOpen && (
                                  <div className="mt-1">
                                    {changelogState === 'loading' && (
                                      <div className="flex items-center gap-2 text-white/50 text-xs">
                                        <Loader2 size={12} className="animate-spin" />
                                        {t('modManager.updating')}
                                      </div>
                                    )}
                                    {changelogState === 'error' && (
                                      <div className="text-red-400 text-xs">{t('modManager.toggleFailed')}</div>
                                    )}
                                    {changelogState === 'ready' && (
                                      <pre className="whitespace-pre-wrap text-white/60 text-xs leading-relaxed font-sans">
                                        {changelogText || t('modManager.noDescription')}
                                      </pre>
                                    )}
                                  </div>
                                )}
                              </div>
                            </div>
                          )}
                        </div>
                      );
                    })}
                  </div>
                </div>
              </div>
            )}

            <div className="flex justify-end gap-2 mt-5">
              <Button onClick={onClose} disabled={isUpdating}>
                {t('common.cancel')}
              </Button>
              <Button
                onClick={handleSubmit}
                disabled={modList.length === 0 || isUpdating || selectedIds.size === 0}
                className="bg-green-500/20 text-green-400 hover:bg-green-500/30 border-green-500/20"
              >
                {isUpdating && <Loader2 size={14} className="animate-spin" />}
                {isUpdating ? t('modManager.updating') : `${t('modManager.updateAll')} (${selectedIds.size})`}
              </Button>
            </div>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
};

/**
 * Modal for bulk deleting selected mods
 */
export const BulkDeleteModal: React.FC<BulkDeleteModalProps> = ({
  isOpen,
  onClose,
  modList,
  modDetailsCache,
  prefetchModDetails,
  onDelete,
  isDeleting,
}) => {
  const { t } = useTranslation();
  useAccentColor(); // For context subscription
  
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [previewId, setPreviewId] = useState<string | null>(null);

  useEffect(() => {
    if (!isOpen) return;
    setSelectedIds(new Set(modList.map((m) => m.id)));
    setPreviewId(modList[0]?.id ?? null);
    void prefetchModDetails(modList);
  }, [isOpen, modList, prefetchModDetails]);

  const handleSubmit = async () => {
    await onDelete(selectedIds);
    onClose();
  };

  return (
    <AnimatePresence>
      {isOpen && (
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          className="fixed inset-0 z-[300] flex items-center justify-center bg-[#0a0a0a]/90"
          onClick={(e) => e.target === e.currentTarget && !isDeleting && onClose()}
        >
          <motion.div
            initial={{ scale: 0.95, opacity: 0 }}
            animate={{ scale: 1, opacity: 1 }}
            exit={{ scale: 0.95, opacity: 0 }}
            className="p-6 w-full max-w-4xl mx-4 shadow-2xl glass-panel-static-solid"
          >
            <div className="flex items-start justify-between gap-4 mb-4">
              <div>
                <h3 className="text-white font-bold text-lg flex items-center gap-2">
                  <Trash2 size={16} className="text-red-400" />
                  {t('modManager.deleteMods')}
                </h3>
                <p className="text-white/60 text-sm mt-1">
                  {modList.length > 0
                    ? `${t('modManager.deleteSelected')} (${modList.length})`
                    : t('modManager.noModsInstalled')}
                </p>
              </div>
              <IconButton variant="ghost" onClick={onClose} disabled={isDeleting} title={t('common.close')}>
                <X size={16} />
              </IconButton>
            </div>

            {modList.length > 0 && (
              <div className="max-h-[55vh] overflow-y-auto pr-1 space-y-4">
                <div className="rounded-2xl border border-white/10 bg-[#1c1c1e]/60 overflow-hidden">
                  <div className="p-2 space-y-1">
                    {modList.map((m) => {
                      const details = modDetailsCache[m.id];
                      const ss0 = (details?.screenshots as ModScreenshot[] | undefined)?.[0];
                      const iconUrl = m.iconUrl || details?.iconUrl || details?.thumbnailUrl;
                      const isChecked = selectedIds.has(m.id);
                      const isActive = previewId === m.id;
                      const summary = details?.summary || m.description || '';

                      return (
                        <div key={m.id} className="rounded-xl">
                          <div
                            className={`flex items-center gap-3 p-3 rounded-xl cursor-pointer transition-colors ${
                              isActive ? 'bg-white/5 border border-white/10' : 'hover:bg-white/5'
                            }`}
                            onClick={() => setPreviewId((prev) => (prev === m.id ? null : m.id))}
                          >
                            <SelectableCheckbox
                              checked={isChecked}
                              onChange={() => {
                                setSelectedIds((prev) => {
                                  const next = new Set(prev);
                                  if (next.has(m.id)) next.delete(m.id);
                                  else next.add(m.id);
                                  return next;
                                });
                              }}
                              onClick={(e) => e.stopPropagation()}
                              title={t('modManager.selected')}
                            />

                            <div className="w-12 h-12 rounded-lg bg-[#2c2c2e] overflow-hidden flex-shrink-0">
                              {ss0?.thumbnailUrl ? (
                                <img src={ss0.thumbnailUrl} alt="" className="w-full h-full object-cover" loading="lazy" />
                              ) : iconUrl ? (
                                <img src={iconUrl} alt="" className="w-full h-full object-cover" loading="lazy" />
                              ) : (
                                <div className="w-full h-full flex items-center justify-center">
                                  <Package size={18} className="text-white opacity-30" />
                                </div>
                              )}
                            </div>

                            <div className="min-w-0 flex-1">
                              <div className="text-white font-medium truncate">{m.name}</div>
                              <div className="text-white/40 text-xs truncate">{m.author || t('modManager.unknownAuthor')}</div>
                            </div>
                          </div>

                          {isActive && (
                            <div className="mt-2 px-4 pb-4">
                              <div className="text-white/70 text-sm leading-relaxed">
                                {summary?.trim() ? summary : t('modManager.noDescription')}
                              </div>
                            </div>
                          )}
                        </div>
                      );
                    })}
                  </div>
                </div>
              </div>
            )}

            <div className="flex justify-end gap-2 mt-5">
              <Button onClick={onClose} disabled={isDeleting}>
                {t('common.cancel')}
              </Button>
              <Button
                variant="danger"
                onClick={handleSubmit}
                disabled={modList.length === 0 || isDeleting || selectedIds.size === 0}
              >
                {isDeleting && <Loader2 size={14} className="animate-spin" />}
                {`${t('modManager.deleteSelected')} (${selectedIds.size})`}
              </Button>
            </div>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
};
