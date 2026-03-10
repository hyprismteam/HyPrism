import React from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import {
  Search, Download, Package, Loader2, AlertCircle,
  Check, Upload, ArrowLeft, X
} from 'lucide-react';
import {
  useModBrowser,
  formatDownloads,
  getReleaseTypeLabel,
  type UseModBrowserOptions,
} from '@/hooks/useModBrowser';
import {
  Button,
  IconButton,
  LinkButton,
  DropdownTriggerButton,
  DropdownMenu,
  MenuItemButton,
  ImageLightbox,
} from '@/components/ui/Controls';

// #region Props

/**
 * Props for the {@link InlineModBrowser} component.
 */
export interface InlineModBrowserProps {
  currentInstanceId?: string;
  installedModIds?: Set<string>;
  installedFileIds?: Set<string>;
  onModsInstalled?: () => void;
  onBack?: () => void;
  refreshSignal?: number;
}

// #endregion

// #region Component

/**
 * Inline mod browser panel embedded within the instances page.
 * Provides CurseForge search, download queue, drag-and-drop import,
 * and mod detail view.
 *
 * @param props - See {@link InlineModBrowserProps}.
 */
export const InlineModBrowser: React.FC<InlineModBrowserProps> = (props) => {
  const { onBack } = props;

  const options: UseModBrowserOptions = {
    currentInstanceId: props.currentInstanceId,
    installedModIds: props.installedModIds,
    installedFileIds: props.installedFileIds,
    onModsInstalled: props.onModsInstalled,
    onBack: props.onBack,
    refreshSignal: props.refreshSignal,
  };

  const {
    t,
    accentColor,
    accentTextColor,
    normalizeId,

    // Search & Results
    searchQuery,
    setSearchQuery,
    searchResults,
    selectedCategory,
    setSelectedCategory,
    selectedSortField,
    setSelectedSortField,
    isSearching,
    hasSearched,
    hasMore,
    isLoadingMore,
    sortOptions,
    categories,

    // Detail Panel
    selectedMod,
    setSelectedMod,
    selectedModFiles,
    isLoadingModFiles,
    activeScreenshot,
    setActiveScreenshot,
    lightboxIndex,
    setLightboxIndex,
    detailSelectedFileId,
    setDetailSelectedFileId,
    setSelectedVersions,

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

    // Options
    installedModIds,
    installedFileIds,

    // Handlers
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
  } = useModBrowser(options);

  return (
    <div
      className="h-full flex flex-col"
      onDragEnter={handleDragEnter}
      onDragOver={handleDragOver}
      onDragLeave={handleDragLeave}
      onDrop={handleDrop}
    >
      {/* Header with search, categories, sort, and back button */}
      <div className="p-4 border-b border-white/[0.06] flex flex-col gap-3 flex-shrink-0">
        <div className="flex items-center gap-3">
          {/* Back button */}
          {onBack && (
            <IconButton
              variant="ghost"
              onClick={onBack}
              className="!rounded-xl flex-shrink-0"
              title={t('common.back')}
            >
              <ArrowLeft size={18} />
            </IconButton>
          )}

          {/* Search input */}
          <div className="relative flex-1">
            <Search size={16} className="absolute left-3 top-1/2 -translate-y-1/2 text-white opacity-40" />
            <input
              type="text"
              value={searchQuery}
              onChange={e => setSearchQuery(e.target.value)}
              placeholder={t('modManager.searchMods')}
              className="w-full h-10 pl-10 pr-4 rounded-xl bg-[#2c2c2e] border border-white/[0.08] text-white text-sm placeholder-white/40 focus:outline-none focus:border-white/20"
              autoFocus
            />
          </div>

          {/* Category dropdown */}
          <div className="relative" ref={categoryDropdownRef}>
            <DropdownTriggerButton
              label={getCategoryName(selectedCategory)}
              open={isCategoryDropdownOpen}
              onClick={() => { setIsCategoryDropdownOpen(!isCategoryDropdownOpen); setIsSortDropdownOpen(false); }}
            />
            <DropdownMenu isOpen={isCategoryDropdownOpen} maxHeight="256px">
              {categories.map(cat => (
                <MenuItemButton
                  key={cat.id}
                  onClick={() => { setSelectedCategory(cat.id); setIsCategoryDropdownOpen(false); }}
                  className={selectedCategory === cat.id ? '!text-white bg-white/[0.08]' : ''}
                >
                  {(() => {
                    const key = `modManager.category.${cat.name.replace(/[\s\\/]+/g, '_').toLowerCase()}`;
                    const translated = t(key);
                    return translated !== key ? translated : cat.name;
                  })()}
                </MenuItemButton>
              ))}
            </DropdownMenu>
          </div>

          {/* Sort dropdown */}
          <div className="relative" ref={sortDropdownRef}>
            <DropdownTriggerButton
              label={getSortName(selectedSortField)}
              open={isSortDropdownOpen}
              onClick={() => { setIsSortDropdownOpen(!isSortDropdownOpen); setIsCategoryDropdownOpen(false); }}
            />
            <DropdownMenu isOpen={isSortDropdownOpen}>
              {sortOptions.map(opt => (
                <MenuItemButton
                  key={opt.id}
                  onClick={() => { setSelectedSortField(opt.id); setIsSortDropdownOpen(false); }}
                  className={selectedSortField === opt.id ? '!text-white bg-white/[0.08]' : ''}
                >
                  {opt.name}
                </MenuItemButton>
              ))}
            </DropdownMenu>
          </div>
        </div>

        {/* Batch download bar (only when mods are selected) */}
        {selectedMods.size > 0 && !isDownloading && (
          <div className="flex items-center justify-between px-3 py-2 rounded-xl border border-white/[0.08] bg-[#2c2c2e]">
            <span className="text-sm text-white/70">
              {selectedMods.size} {t('modManager.modsSelected')}
            </span>
            <div className="flex items-center gap-2">
              <LinkButton
                onClick={() => setSelectedMods(new Set())}
                className="text-xs"
              >
                {t('common.clear')}
              </LinkButton>
              <Button
                size="sm"
                variant="primary"
                onClick={handleDownloadSelected}
              >
                <Download size={12} />
                {t('modManager.downloadSelected')}
              </Button>
            </div>
          </div>
        )}

        {/* Download progress bar */}
        {isDownloading && downloadProgress && (
          <div className="px-3 py-2 rounded-xl border border-white/[0.08] bg-[#2c2c2e] space-y-2">
            <div className="flex items-center justify-between text-sm">
              <span className="text-white/70">
                {t('modManager.downloading')} ({downloadProgress.current}/{downloadProgress.total})
              </span>
              <span className="text-white/50 text-xs truncate ml-2">{downloadProgress.currentMod}</span>
            </div>
            <div className="h-1.5 bg-[#1c1c1e] rounded-full overflow-hidden">
              <div
                className="h-full rounded-full transition-all duration-300"
                style={{ width: `${(downloadProgress.current / downloadProgress.total) * 100}%`, backgroundColor: accentColor }}
              />
            </div>
            {downloadJobs.length > 0 && (
              <div className="space-y-1 max-h-24 overflow-y-auto">
                {downloadJobs.map(job => (
                  <div key={job.id} className="flex items-center gap-2 text-xs">
                    {job.status === 'running' && <Loader2 size={10} className="animate-spin text-white opacity-60" />}
                    {job.status === 'success' && <Check size={10} className="text-green-400" />}
                    {job.status === 'error' && <AlertCircle size={10} className="text-red-400" />}
                    {job.status === 'pending' && <div className="w-2.5 h-2.5 rounded-full bg-white/20" />}
                    <span className={`truncate ${job.status === 'error' ? 'text-red-400' : 'text-white/60'}`}>{job.name}</span>
                  </div>
                ))}
              </div>
            )}
          </div>
        )}
      </div>

      {/* Error toast */}
      <AnimatePresence>
        {error && (
          <motion.div
            initial={{ opacity: 0, y: -10 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -10 }}
            className="mx-4 mt-2 px-3 py-2 rounded-xl bg-red-500/15 border border-red-500/20 text-red-400 text-sm flex items-center gap-2"
          >
            <AlertCircle size={14} />
            <span className="flex-1 truncate">{error}</span>
            <IconButton variant="ghost" title={t('common.dismiss')} onClick={() => setError(null)} className="h-6 w-6">
              <X size={14} />
            </IconButton>
          </motion.div>
        )}
      </AnimatePresence>

      {/* Import progress */}
      {importProgress && (
        <div className="mx-4 mt-2 px-3 py-2 rounded-xl bg-blue-500/15 border border-blue-500/20 text-blue-300 text-sm flex items-center gap-2">
          {isImporting && <Loader2 size={14} className="animate-spin" />}
          {!isImporting && <Check size={14} className="text-green-400" />}
          <span>{importProgress}</span>
        </div>
      )}

      {/* Main content: grid + detail panel */}
      <div className="flex-1 flex overflow-hidden">
        {/* Mod grid */}
        <div
          ref={scrollContainerRef}
          className="overflow-y-auto p-4 min-w-0"
          onScroll={handleScroll}
          style={{
            flex: selectedMod ? '0 0 55%' : '1 1 100%',
            transition: 'flex 0.25s cubic-bezier(0.4, 0, 0.2, 1)',
          }}
        >
          {isSearching ? (
            <div className="flex items-center justify-center h-full">
              <Loader2 size={32} className="animate-spin" style={{ color: accentColor }} />
            </div>
          ) : !hasSearched ? (
            <div className="flex items-center justify-center h-full">
              <Loader2 size={32} className="animate-spin" style={{ color: accentColor }} />
            </div>
          ) : searchResults.length === 0 ? (
            <div className="flex flex-col items-center justify-center h-full text-white/40">
              <Package size={48} className="mb-4 opacity-40" />
              <p className="text-lg font-medium">{t('modManager.noModsFound')}</p>
              <p className="text-sm mt-1 text-white/30">{t('modManager.noMatch')}</p>
            </div>
          ) : (
            <div className="grid grid-cols-1 gap-2">
              {searchResults.map((mod, index) => {
                const modId = normalizeId(mod.id);
                const isSelected = selectedMods.has(modId);
                const isDetailSelected = normalizeId(selectedMod?.id) === modId;
                const isInstalled = installedModIds?.has(`cf-${modId}`) ?? false;

                return (
                  <div
                    key={mod.id}
                    onClick={(e) => {
                      if (e.shiftKey) {
                        handleBrowseShiftLeftClick(e, index);
                        return;
                      }
                      browseSelectionAnchorRef.current = index;
                      handleModClick(mod);
                    }}
                    className={`group flex items-center gap-3 p-3 rounded-xl cursor-pointer transition-all border ${
                      isDetailSelected
                        ? 'border-white/20 bg-[#2c2c2e]'
                        : isSelected
                          ? 'border-white/[0.08] bg-[#252527]'
                          : 'border-transparent hover:bg-[#252527]'
                    }`}
                  >
                    {/* Checkbox */}
                    <button
                      onClick={e => {
                        e.stopPropagation();
                        if (e.shiftKey) {
                          handleBrowseShiftLeftClick(e, index);
                          return;
                        }
                        toggleModSelection(mod, index);
                      }}
                      className={`w-5 h-5 rounded border-2 flex items-center justify-center flex-shrink-0 ${
                        isSelected ? '' : 'bg-transparent border-white/30 hover:border-white/50'
                      }`}
                      style={isSelected ? { backgroundColor: accentColor, borderColor: accentColor } : undefined}
                    >
                      {isSelected && <Check size={12} style={{ color: accentTextColor }} />}
                    </button>

                    {/* Icon */}
                    <div className="w-12 h-12 rounded-lg bg-[#1c1c1e] flex items-center justify-center overflow-hidden flex-shrink-0">
                      {mod.iconUrl ? (
                        <img src={mod.iconUrl} alt="" className="w-full h-full object-cover" loading="lazy" />
                      ) : (
                        <Package size={20} className="text-white opacity-30" />
                      )}
                    </div>

                    {/* Info */}
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center gap-2">
                        <LinkButton
                          onClick={(e) => handleOpenModPage(e, mod)}
                          className="!text-white font-medium truncate text-left underline-offset-2"
                          title={t('modManager.openCurseforge')}
                        >
                          {mod.name}
                        </LinkButton>
                        {isInstalled && (
                          <span className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-green-500/20 text-green-400 flex-shrink-0">
                            {t('modManager.installedBadge')}
                          </span>
                        )}
                        {mod.categories?.length > 0 && typeof mod.categories[0] === 'string' && (
                          <span className="px-1.5 py-0.5 rounded text-[10px] text-white/40 bg-[#2c2c2e] flex-shrink-0">
                            {(() => {
                              const raw = mod.categories[0] as string;
                              const key = `modManager.category.${raw.replace(/[\s\\/]+/g, '_').toLowerCase()}`;
                              const translated = t(key);
                              return translated !== key ? translated : raw;
                            })()}
                          </span>
                        )}
                      </div>
                      <p className="text-white/40 text-xs truncate mt-0.5">{mod.summary}</p>
                    </div>

                    {/* Stats */}
                    <div className="flex flex-col items-end gap-1 flex-shrink-0">
                      <span className="text-white/50 text-xs flex items-center gap-1">
                        <Download size={10} />
                        {formatDownloads(mod.downloadCount)}
                      </span>
                      <span className="text-white/30 text-xs">{mod.author}</span>
                    </div>
                  </div>
                );
              })}

              {/* Infinite scroll loading indicator */}
              {isLoadingMore && (
                <div className="flex justify-center py-4">
                  <Loader2 size={24} className="animate-spin" style={{ color: accentColor }} />
                </div>
              )}
              {!hasMore && searchResults.length > 0 && (
                <p className="text-center text-white/30 text-xs py-4">{t('modManager.allModsLoaded')}</p>
              )}
            </div>
          )}
        </div>

        {/* Detail panel */}
        <AnimatePresence mode="wait">
          {selectedMod && (
            <motion.div
              key="detail"
              initial={{ opacity: 0, width: 0 }}
              animate={{ opacity: 1, width: '45%' }}
              exit={{ opacity: 0, width: 0 }}
              transition={{ duration: 0.25, ease: [0.4, 0, 0.2, 1] }}
              className="border-l border-white/[0.06] flex flex-col overflow-y-auto overflow-x-hidden flex-shrink-0"
            >
              {/* Close detail */}
              <div className="flex items-center justify-between p-4 border-b border-white/[0.06]">
                <LinkButton
                  onClick={(e) => handleOpenModPage(e, selectedMod)}
                  className="!text-white font-bold text-lg truncate flex-1 text-left underline-offset-2"
                  title={t('modManager.openCurseforge')}
                >
                  {selectedMod.name}
                </LinkButton>
                <IconButton
                  size="sm"
                  variant="ghost"
                  onClick={() => setSelectedMod(null)}
                  className="ml-2"
                >
                  <X size={16} />
                </IconButton>
              </div>

              {/* Screenshots carousel */}
              {selectedMod.screenshots && selectedMod.screenshots.length > 0 && (
                <div className="relative px-4 pt-3">
                  <div className="aspect-video bg-[#0a0a0a] rounded-xl overflow-hidden">
                    <img
                      src={selectedMod.screenshots[activeScreenshot]?.url || selectedMod.screenshots[activeScreenshot]?.thumbnailUrl}
                      alt={selectedMod.screenshots[activeScreenshot]?.title}
                      className="w-full h-full object-cover cursor-pointer"
                      onClick={() => setLightboxIndex(activeScreenshot)}
                      draggable={false}
                    />
                  </div>
                  {selectedMod.screenshots.length > 1 && (
                    <div className="flex gap-2 mt-2 overflow-x-auto pb-1">
                      {selectedMod.screenshots.map((ss, i) => (
                        <button
                          key={ss.id}
                          onClick={() => setActiveScreenshot(i)}
                          className={`w-16 h-10 rounded-lg overflow-hidden flex-shrink-0 border-2 transition-all ${
                            i === activeScreenshot ? 'border-white/40' : 'border-transparent opacity-60 hover:opacity-100'
                          }`}
                        >
                          <img src={ss.thumbnailUrl} alt="" className="w-full h-full object-cover" loading="lazy" draggable={false} />
                        </button>
                      ))}
                    </div>
                  )}
                </div>
              )}

              {/* Mod info */}
              <div className="p-4 space-y-4">
                <p className="text-white/60 text-sm leading-relaxed">{selectedMod.summary}</p>

                <div className="grid grid-cols-2 gap-3 text-sm">
                  <div>
                    <span className="text-white/40 text-xs">{t('modManager.author')}</span>
                    <p className="text-white/80 mt-0.5">{selectedMod.author || t('modManager.unknownAuthor')}</p>
                  </div>
                  <div>
                    <span className="text-white/40 text-xs">{t('modManager.downloads')}</span>
                    <p className="text-white/80 mt-0.5">{formatDownloads(selectedMod.downloadCount)}</p>
                  </div>
                </div>

                {/* File version selector */}
                <div>
                  <span className="text-white/40 text-xs block mb-2">{t('modManager.selectVersion')}</span>
                  {isLoadingModFiles ? (
                    <div className="flex items-center justify-center py-4">
                      <Loader2 size={18} className="animate-spin" style={{ color: accentColor }} />
                    </div>
                  ) : selectedModFiles.length === 0 ? (
                    <p className="text-white/30 text-sm">{t('modManager.noFilesAvailable')}</p>
                  ) : (
                    <div className="space-y-1.5 max-h-40 overflow-y-auto">
                      {selectedModFiles.slice(0, 10).map(file => {
                        const fileId = normalizeId(file.id);
                        const isFileInstalled = installedFileIds?.has(fileId) ?? false;
                        return (
                          <MenuItemButton
                            key={file.id}
                            onClick={() => {
                              const selectedModId = normalizeId(selectedMod.id);
                              setDetailSelectedFileId(fileId);
                              setSelectedVersions(prev => new Map(prev).set(selectedModId, fileId));
                            }}
                            className={`!block !px-3 !py-2 !rounded-lg border ${
                              isFileInstalled
                                ? 'border-green-500/30 bg-green-500/10'
                                : detailSelectedFileId === fileId
                                  ? 'border-white/20 bg-[#2c2c2e]'
                                  : 'border-transparent !hover:bg-[#252527]'
                            }`}
                          >
                            <div className="flex items-center justify-between">
                              <span className="text-white/80 truncate flex-1">{file.displayName || file.fileName}</span>
                              <div className="flex items-center gap-1.5 flex-shrink-0">
                                {isFileInstalled && (
                                  <span className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-green-500/20 text-green-400">
                                    {t('modManager.installedBadge')}
                                  </span>
                                )}
                                <span className={`px-1.5 py-0.5 rounded text-[10px] font-medium ${
                                  file.releaseType === 1 ? 'bg-green-500/20 text-green-400'
                                    : file.releaseType === 2 ? 'bg-yellow-500/20 text-yellow-400'
                                      : 'bg-red-500/20 text-red-400'
                                }`}>
                                  {getReleaseTypeLabel(file.releaseType, t)}
                                </span>
                              </div>
                            </div>
                            {file.gameVersions && file.gameVersions.length > 0 && (
                              <span className="text-white/30 text-xs">{file.gameVersions.join(', ')}</span>
                            )}
                          </MenuItemButton>
                        );
                      })}
                    </div>
                  )}
                </div>

                {/* Install button */}
                {(() => {
                  const fileId = detailSelectedFileId || normalizeId(selectedModFiles[0]?.id);
                  const isSelectedFileInstalled = fileId ? (installedFileIds?.has(fileId) ?? false) : false;
                  return (
                    <Button
                      variant="primary"
                      onClick={() => {
                        const selectedModId = normalizeId(selectedMod.id);
                        if (fileId && !isSelectedFileInstalled && selectedModId) {
                          handleInstallSingleMod(selectedModId, fileId, selectedMod.name);
                        }
                      }}
                      disabled={isDownloading || selectedModFiles.length === 0 || isSelectedFileInstalled}
                      className={`w-full font-bold ${
                        isSelectedFileInstalled ? 'cursor-default' : ''
                      }`}
                      style={isSelectedFileInstalled
                        ? { backgroundColor: 'rgba(34, 197, 94, 0.15)', color: 'rgb(74, 222, 128)' }
                        : undefined
                      }
                    >
                      {isDownloading ? (
                        <Loader2 size={16} className="animate-spin" />
                      ) : isSelectedFileInstalled ? (
                        <Check size={16} />
                      ) : (
                        <Download size={16} />
                      )}
                      {isSelectedFileInstalled ? t('modManager.installedBadge') : t('modManager.download')}
                    </Button>
                  );
                })()}
              </div>
            </motion.div>
          )}
        </AnimatePresence>
      </div>

      {/* Drag overlay */}
      {isDragging && (
        <div className="absolute inset-0 z-50 flex items-center justify-center bg-black/60 border-2 border-dashed rounded-2xl" style={{ borderColor: accentColor }}>
          <div className="flex flex-col items-center gap-3">
            <Upload size={48} style={{ color: accentColor }} />
            <p className="text-white font-medium text-lg">{t('modManager.dropModsHere')}</p>
          </div>
        </div>
      )}

      {/* Screenshot lightbox */}
      <ImageLightbox
        isOpen={lightboxIndex !== null}
        title={selectedMod?.name}
        images={(selectedMod?.screenshots ?? []).map((ss) => ({ url: ss.url, title: ss.title }))}
        index={lightboxIndex ?? 0}
        onIndexChange={(next) => setLightboxIndex(next)}
        onClose={() => setLightboxIndex(null)}
      />
    </div>
  );
};
// #endregion
