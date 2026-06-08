import React, { useCallback, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { motion } from 'framer-motion';
import { 
  Search, Package, Plus, Trash2, RefreshCw, RotateCw, 
  Loader2, Download
} from 'lucide-react';
import { useAccentColor } from '@/contexts/AccentColorContext';
import { ipc, type ModInfo as CurseForgeModInfo } from '@/lib/ipc';
import { Button, IconButton, LinkButton, SelectableCheckbox } from '@/components/ui/Controls';
import type { InstalledVersionInfo, InstalledModInfo, InstanceTab } from '@/types';

export interface ContentTabProps {
  selectedInstance: InstalledVersionInfo | null;
  // Mod state
  installedMods: InstalledModInfo[];
  filteredMods: InstalledModInfo[];
  isLoadingMods: boolean;
  modsSearchQuery: string;
  setModsSearchQuery: (query: string) => void;
  selectedMods: Set<string>;
  modsWithUpdates: InstalledModInfo[];
  updateCount: number;
  modDetailsCache: Record<string, CurseForgeModInfo | null>;
  // Actions
  loadInstalledMods: () => Promise<void>;
  toggleModSelection: (modId: string, index: number) => void;
  handleShiftSelect: (index: number) => void;
  selectOnlyMod: (modId: string, index: number) => void;
  selectAllMods: () => void;
  handleToggleMod: (mod: InstalledModInfo) => Promise<void>;
  handleBulkToggleMods: (enabled: boolean) => Promise<void>;
  setModToDelete: (mod: InstalledModInfo | null) => void;
  setShowBulkUpdateConfirm: (show: boolean) => void;
  setShowBulkDeleteConfirm: (show: boolean) => void;
  onTabChange?: (tab: InstanceTab) => void;
  // Drag & drop
  onDropImportMods: (files: FileList | File[]) => Promise<void>;
  // State flags
  isUpdatingMods: boolean;
  isBulkTogglingMods: boolean;
  isDeletingMod: boolean;
  // Helpers
  getDisplayVersion: (mod: InstalledModInfo) => string;
  isLocalInstalledMod: (mod: InstalledModInfo) => boolean;
  isTrustedRemoteIdentity: (mod: InstalledModInfo) => boolean;
  getCurseForgeUrlFromDetails: (details: CurseForgeModInfo | null | undefined) => string | null;
  handleOpenModPage: (e: React.MouseEvent, mod: InstalledModInfo) => void;
}

export const ContentTab: React.FC<ContentTabProps> = ({
  selectedInstance,
  filteredMods,
  isLoadingMods,
  modsSearchQuery,
  setModsSearchQuery,
  selectedMods,
  modsWithUpdates,
  updateCount,
  modDetailsCache,
  loadInstalledMods,
  toggleModSelection,
  handleShiftSelect,
  selectOnlyMod,
  selectAllMods,
  handleToggleMod,
  handleBulkToggleMods,
  setModToDelete,
  setShowBulkUpdateConfirm,
  setShowBulkDeleteConfirm,
  onTabChange,
  onDropImportMods,
  isUpdatingMods,
  isBulkTogglingMods,
  isDeletingMod,
  getDisplayVersion,
  isTrustedRemoteIdentity,
  getCurseForgeUrlFromDetails,
  handleOpenModPage,
}) => {
  const { t } = useTranslation();
  const { accentColor, accentTextColor } = useAccentColor();
  
  const modDropDepthRef = useRef(0);
  const [isModDropActive, setIsModDropActive] = React.useState(false);
  const [, setIsImportingDroppedMods] = React.useState(false);

  const handleDropImport = useCallback(async (files: FileList | File[]) => {
    // Filter to only .jar files before processing
    const jarFiles = Array.from(files).filter((file) => {
      const name = file.name?.toLowerCase() || '';
      return name.endsWith('.jar') || name.endsWith('.jar.disabled');
    });
    if (jarFiles.length === 0) return;
    
    setIsImportingDroppedMods(true);
    await onDropImportMods(jarFiles);
    setIsImportingDroppedMods(false);
  }, [onDropImportMods]);

  const handleRowClick = useCallback((e: React.MouseEvent, modId: string, index: number) => {
    if (e.shiftKey) {
      e.preventDefault();
      handleShiftSelect(index);
      return;
    }

    if (e.ctrlKey || e.metaKey) {
      e.preventDefault();
      toggleModSelection(modId, index);
      return;
    }

    selectOnlyMod(modId, index);
  }, [handleShiftSelect, selectOnlyMod, toggleModSelection]);

  if (!selectedInstance) return null;

  const isInstalled = selectedInstance.validationStatus === 'Valid';

  return (
    <div
      className="absolute inset-0 flex flex-col"
      onDragEnter={(e) => {
        if (!isInstalled) return;
        if (!Array.from(e.dataTransfer.types).includes('Files')) return;
        e.preventDefault();
        e.stopPropagation();
        modDropDepthRef.current++;
        setIsModDropActive(true);
      }}
      onDragOver={(e) => {
        if (!isInstalled) return;
        if (!Array.from(e.dataTransfer.types).includes('Files')) return;
        e.preventDefault();
        e.stopPropagation();
        e.dataTransfer.dropEffect = 'copy';
      }}
      onDragLeave={(e) => {
        if (!isInstalled) return;
        if (!Array.from(e.dataTransfer.types).includes('Files')) return;
        e.preventDefault();
        e.stopPropagation();
        modDropDepthRef.current = Math.max(0, modDropDepthRef.current - 1);
        if (modDropDepthRef.current === 0) setIsModDropActive(false);
      }}
      onDrop={(e) => {
        if (!isInstalled) return;
        if (!Array.from(e.dataTransfer.types).includes('Files')) return;
        
        // Check if any files are .jar before accepting the drop
        const files = Array.from(e.dataTransfer.files);
        const hasJarFiles = files.some((file) => {
          const name = file.name?.toLowerCase() || '';
          return name.endsWith('.jar') || name.endsWith('.jar.disabled');
        });
        
        if (!hasJarFiles) {
          e.preventDefault();
          e.stopPropagation();
          modDropDepthRef.current = 0;
          setIsModDropActive(false);
          return;
        }
        
        e.preventDefault();
        e.stopPropagation();
        modDropDepthRef.current = 0;
        setIsModDropActive(false);
        void handleDropImport(e.dataTransfer.files);
      }}
    >
      {/* Drop overlay */}
      {isModDropActive && (
        <div className="absolute inset-0 z-20 flex items-center justify-center bg-black/50">
          <div className="px-5 py-4 rounded-2xl border border-white/10 bg-[#1c1c1e]/80 text-white/80 text-sm font-medium">
            Drop mod files to import
          </div>
        </div>
      )}

      {isInstalled && (
        <>
          {/* Header */}
          <div className="p-4 border-b border-white/[0.06] flex items-center gap-3">
            {/* Search */}
            <div className="relative flex-1 max-w-md">
              <Search size={16} className="absolute left-3 top-1/2 -translate-y-1/2 text-white opacity-40" />
              <input
                type="text"
                value={modsSearchQuery}
                onChange={(e) => setModsSearchQuery(e.target.value)}
                onKeyDown={(e) => {
                  if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'a') {
                    e.preventDefault();
                    e.stopPropagation();
                    (e.currentTarget as HTMLInputElement).select();
                  }
                }}
                placeholder={t('modManager.searchMods')}
                className="w-full h-10 pl-10 pr-4 rounded-xl bg-[#2c2c2e] border border-white/[0.08] text-white text-sm placeholder-white/40 focus:outline-none focus:border-white/20"
              />
            </div>

            {/* Actions */}
            <div className="flex items-center gap-2 ml-auto">
              <IconButton variant="ghost" onClick={() => void loadInstalledMods()} disabled={isLoadingMods} title={t('common.refresh')}>
                <RotateCw size={16} className={isLoadingMods ? 'animate-spin' : ''} />
              </IconButton>

              <button
                onClick={() => setShowBulkUpdateConfirm(true)}
                disabled={updateCount === 0 || isUpdatingMods || isBulkTogglingMods}
                className={`relative p-2 rounded-xl transition-all disabled:opacity-40 disabled:hover:bg-transparent ${
                  updateCount > 0
                    ? 'text-green-400 bg-green-500/10 hover:bg-green-500/15 border border-green-500/20'
                    : 'text-white/50 hover:text-white hover:bg-white/[0.06]'
                }`}
                title={t('modManager.checkForUpdates')}
              >
                <RefreshCw size={16} className={isUpdatingMods ? 'animate-spin' : ''} />
                {updateCount > 0 && (
                  <span className="absolute -top-1 -right-1 min-w-4 h-4 px-1 rounded-full text-[10px] leading-4 text-center bg-green-500 text-black font-bold">
                    {updateCount}
                  </span>
                )}
              </button>
              <button
                onClick={() => setShowBulkDeleteConfirm(true)}
                disabled={selectedMods.size === 0 || isDeletingMod || isBulkTogglingMods}
                className="relative p-2 rounded-xl text-white/50 hover:text-red-300 hover:bg-red-500/10 transition-all disabled:opacity-40 disabled:hover:bg-transparent"
                title={t('modManager.deleteSelected')}
              >
                <Trash2 size={16} />
                {selectedMods.size > 0 && (
                  <span className="absolute -top-1 -right-1 min-w-4 h-4 px-1 rounded-full text-[10px] leading-4 text-center bg-red-500 text-black font-bold">
                    {selectedMods.size}
                  </span>
                )}
              </button>
            </div>
          </div>
        </>
      )}

      {/* Mods List */}
      <div
        className="flex-1 overflow-y-auto focus:outline-none"
        tabIndex={0}
        onMouseDown={(e) => (e.currentTarget as HTMLDivElement).focus()}
        onKeyDown={(e) => {
          if (!(e.metaKey || e.ctrlKey) || e.key.toLowerCase() !== 'a') return;
          const target = e.target as HTMLElement | null;
          const tag = target?.tagName?.toLowerCase();
          const isTypingTarget = tag === 'input' || tag === 'textarea' || Boolean((target as HTMLElement & { isContentEditable?: boolean })?.isContentEditable);
          if (isTypingTarget) return;
          e.preventDefault();
          e.stopPropagation();
          selectAllMods();
        }}
      >
        {!isInstalled ? (
          <div className="flex flex-col items-center justify-center h-full">
            <Download size={48} className="mb-4 text-white opacity-40" />
            <p className="text-lg font-medium text-white/60">{t('instances.instanceNotInstalled')}</p>
            <p className="text-sm mt-1 text-white/40">{t('instances.instanceNotInstalledHint')}</p>
          </div>
        ) : isLoadingMods ? (
          <div className="flex items-center justify-center h-full">
            <Loader2 size={32} className="animate-spin" style={{ color: accentColor }} />
          </div>
        ) : filteredMods.length === 0 ? (
          <div className="flex flex-col items-center justify-center h-full">
            <Package size={48} className="mb-4 text-white opacity-40" />
            <p className="text-lg font-medium text-white/60">{t('modManager.noModsInstalled')}</p>
            <p className="text-sm mt-1 text-white/40">{t('modManager.clickInstallContent')}</p>
            <Button variant="primary" onClick={() => onTabChange?.('browse')} className="mt-4 shadow-lg">
              <Plus size={16} />
              {t('instances.installContent')}
            </Button>
          </div>
        ) : (
          <div className="p-4">
            <div className="grid grid-cols-1 gap-2">
              {filteredMods.map((mod, index) => {
                const hasUpdate = modsWithUpdates.some(u => u.id === mod.id);
                const isSelected = selectedMods.has(mod.id);
                const details = modDetailsCache[mod.id];
                const canUseRemoteDetails = isTrustedRemoteIdentity(mod) || details != null;
                const resolvedIconUrl = mod.iconUrl || (canUseRemoteDetails ? (details?.iconUrl || details?.thumbnailUrl) : undefined);
                const displayName = canUseRemoteDetails ? (details?.name || mod.name) : mod.name;
                const displayAuthor = canUseRemoteDetails ? (details?.author || mod.author) : mod.author;
                const cfUrlFromDetails = canUseRemoteDetails ? getCurseForgeUrlFromDetails(details) : null;

                return (
                  <div
                    key={mod.id}
                    onClick={(e) => handleRowClick(e, mod.id, index)}
                    className={`group flex items-center gap-3 p-3 rounded-xl cursor-pointer transition-all ${
                      isSelected ? 'bg-[#252527]' : 'hover:bg-[#252527]'
                    }`}
                  >
                    {/* Checkbox */}
                    <SelectableCheckbox
                      checked={isSelected}
                      onChange={() => toggleModSelection(mod.id, index)}
                      onClick={(e) => {
                        e.stopPropagation();
                        if (e.shiftKey) {
                          handleShiftSelect(index);
                        }
                      }}
                    />

                    {/* Icon */}
                    <div className="w-12 h-12 rounded-lg bg-[#1c1c1e] flex items-center justify-center overflow-hidden flex-shrink-0">
                      {resolvedIconUrl ? (
                        <img src={resolvedIconUrl} alt="" className="w-full h-full object-cover" loading="lazy" />
                      ) : (
                        <Package size={20} className="text-white opacity-30" />
                      )}
                    </div>

                    {/* Info */}
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center gap-2">
                        <LinkButton
                          onClick={(e) => {
                            if (cfUrlFromDetails) {
                              e.preventDefault();
                              e.stopPropagation();
                              ipc.browser.open(cfUrlFromDetails);
                              return;
                            }
                            handleOpenModPage(e, mod);
                          }}
                          className="text-white font-medium truncate text-left"
                          title={t('modManager.openCurseforge')}
                        >
                          {displayName}
                        </LinkButton>
                        {hasUpdate && (
                          <span className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-green-500/20 text-green-400 flex-shrink-0">
                            {t('modManager.updateBadge')}
                          </span>
                        )}
                        {(() => {
                          const firstCategory = mod.categories?.[0];
                          if (typeof firstCategory !== 'string') return null;
                          return (
                            <span className="px-1.5 py-0.5 rounded text-[10px] text-white/40 bg-[#2c2c2e] flex-shrink-0">
                              {(() => {
                                const key = `modManager.category.${firstCategory.replace(/[\s\\/]+/g, '_').toLowerCase()}`;
                                const translated = t(key);
                                return translated !== key ? translated : firstCategory;
                              })()}
                            </span>
                          );
                        })()}
                      </div>
                      <p className="text-white/30 text-xs truncate mt-0.5">
                        {displayAuthor || t('modManager.unknownAuthor')}
                      </p>
                    </div>

                    {/* Right side: version + toggle */}
                    <div className="flex flex-col items-end gap-1 flex-shrink-0">
                      <div className="flex items-center gap-1.5">
                        <span className="text-white/60 text-xs truncate max-w-[140px]">{getDisplayVersion(mod)}</span>
                        {mod.releaseType && mod.releaseType !== 1 && (
                          <span
                            className={`px-1.5 py-0.5 rounded text-[10px] font-medium flex-shrink-0 ${
                              mod.releaseType === 2
                                ? 'bg-yellow-500/20 text-yellow-400'
                                : 'bg-red-500/20 text-red-400'
                            }`}
                          >
                            {mod.releaseType === 2 ? 'β' : 'α'}
                          </span>
                        )}
                      </div>

                      <button
                        className="w-11 h-6 rounded-full p-0.5 transition-colors"
                        style={{ backgroundColor: mod.enabled ? accentColor : 'rgba(255,255,255,0.18)' }}
                        disabled={isBulkTogglingMods}
                        onClick={async (e) => {
                          e.stopPropagation();
                          if (selectedMods.size > 0 && selectedMods.has(mod.id)) {
                            await handleBulkToggleMods(!mod.enabled);
                            return;
                          }
                          await handleToggleMod(mod);
                        }}
                        title={t('modManager.enabled')}
                      >
                        <motion.div
                          className="w-5 h-5 rounded-full shadow-md"
                          style={{ backgroundColor: mod.enabled ? accentTextColor : 'white' }}
                          animate={{ x: mod.enabled ? 20 : 0 }}
                          transition={{ type: 'spring', stiffness: 500, damping: 30 }}
                        />
                      </button>
                    </div>

                    {/* Delete button */}
                    <IconButton 
                      variant="ghost" 
                      size="sm" 
                      onClick={(e) => { e.stopPropagation(); setModToDelete(mod); }} 
                      className="!w-7 !h-7 text-white/30 hover:text-red-400 hover:bg-red-500/10 flex-shrink-0" 
                      title={t('common.delete')}
                    >
                      <Trash2 size={14} />
                    </IconButton>
                  </div>
                );
              })}
            </div>
          </div>
        )}
      </div>

      {/* Footer */}
      {filteredMods.length > 0 && (
        <div className="p-4 border-t border-white/10 flex items-center justify-between text-sm text-white/50">
          <span>{filteredMods.length} {t('modManager.modsInstalled')}</span>
        </div>
      )}
    </div>
  );
};

export default ContentTab;
