import React from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { useTranslation } from 'react-i18next';
import {
  HardDrive, FolderOpen, Trash2, Upload,
  Check, Plus, Package, MoreVertical, Play, X, Edit2,
  Download, AlertCircle, AlertTriangle, Loader2
} from 'lucide-react';
import { useAccentColor } from '../contexts/AccentColorContext';

import type { InstalledVersionInfo, InstanceTab } from '@/types';
import { InstancesSidebar } from '@/pages/instances/InstancesSidebar';
import { InlineModBrowser } from '../components/InlineModBrowser';
import { formatBytes } from '../utils/format';
import { CreateInstanceModal } from '../components/modals/CreateInstanceModal';
import { EditInstanceModal } from '../components/modals/EditInstanceModal';
import { PageContainer } from '@/components/ui/PageContainer';
import { AccentSegmentedControl, IconButton, LauncherActionButton, MenuItemButton, Button } from '@/components/ui/Controls';
import { ConfirmationModal } from '@/components/ui/modals';
import { pageVariants } from '@/constants/animations';
import { openInstanceModsFolder } from '@/hooks';
import { useInstancesPage } from '@/hooks/useInstancesPage';

// Tab components
import { ContentTab } from '@/pages/instances/ContentTab';
import { WorldsTab } from '@/pages/instances/WorldsTab';
import { BulkUpdateModal, BulkDeleteModal } from '@/pages/instances/BulkOperationModals';

// #region Props

/**
 * Props for the {@link InstancesPage} component.
 */
interface InstancesPageProps {
  onInstanceDeleted?: () => void;
  onInstanceSelected?: () => void;
  isGameRunning?: boolean;
  runningInstanceId?: string;
  onStopGame?: () => void;
  activeTab?: InstanceTab;
  onTabChange?: (tab: InstanceTab) => void;
  isDownloading?: boolean;
  downloadingInstanceId?: string;
  downloadState?: 'downloading' | 'extracting' | 'launching';
  progress?: number;
  downloaded?: number;
  total?: number;
  speed?: number;
  launchState?: string;
  launchDetail?: string;
  canCancel?: boolean;
  onCancelDownload?: () => void;
  onLaunchInstance?: (instanceId: string) => void;
  officialServerBlocked?: boolean;
  hasDownloadSources?: boolean;
}

// #endregion

// #region Component

/**
 * Full-page component that displays the installed Hytale instances list,
 * mod management tabs (content/browse/worlds), download progress overlays,
 * and all associated modals.
 *
 * @param props - See {@link InstancesPageProps}.
 */
export const InstancesPage: React.FC<InstancesPageProps> = (props) => {
  const {
    progress = 0,
    downloaded = 0,
    total = 0,
    launchState = '',
    launchDetail = '',
    canCancel = false,
    onCancelDownload,
    officialServerBlocked = false,
    hasDownloadSources = true,
  } = props;

  const { t } = useTranslation();
  const { accentColor } = useAccentColor();

  // Main hook with all state and handlers
  const page = useInstancesPage({
    onInstanceDeleted: props.onInstanceDeleted,
    onInstanceSelected: props.onInstanceSelected,
    isGameRunning: props.isGameRunning,
    runningInstanceId: props.runningInstanceId,
    onStopGame: props.onStopGame,
    activeTab: props.activeTab,
    onTabChange: props.onTabChange,
    isDownloading: props.isDownloading,
    downloadingInstanceId: props.downloadingInstanceId,
    onLaunchInstance: props.onLaunchInstance,
  });

  // Render instance icon
  const renderInstanceIcon = (inst: InstalledVersionInfo, size: number = 18) => {
    const customIcon = page.instanceIcons[inst.id];

    if (customIcon) {
      return (
        <img
          src={customIcon}
          alt=""
          className="w-full h-full object-cover rounded-lg"
          onError={() => {
            page.setInstanceIcons(prev => {
              const next = { ...prev };
              delete next[inst.id];
              return next;
            });
          }}
        />
      );
    }

    const versionLabel = inst.version > 0 ? `v${inst.version}` : '?';
    return <span className="font-bold" style={{ color: accentColor, fontSize: size * 0.8 }}>{versionLabel}</span>;
  };

  // Get validation icon
  const getValidationIcon = (inst: InstalledVersionInfo) => {
    const status = inst.validationStatus || 'Unknown';
    switch (status) {
      case 'Valid':
        return <Check size={12} />;
      case 'NotInstalled':
        return <AlertCircle size={12} />;
      case 'Corrupted':
        return <AlertTriangle size={12} />;
      default:
        return <AlertCircle size={12} />;
    }
  };

  return (
    <motion.div
      variants={pageVariants}
      initial="initial"
      animate="animate"
      exit="exit"
      transition={{ duration: 0.3, ease: 'easeOut' }}
      className="h-full w-full"
    >
      <PageContainer contentClassName="h-full">
        <div className="h-full flex gap-4">
          {/* Left Sidebar */}
          <InstancesSidebar
            title={t('instances.title')}
            accentColor={accentColor}
            instances={page.instances}
            isLoading={page.isLoading}
            instanceDir={page.instanceDir}
            selectedInstanceId={page.selectedInstance?.id ?? null}
            onCreate={() => page.setShowCreateModal(true)}
            onImport={() => page.instanceActions.handleImport(page.setIsImporting)}
            importDisabled={page.isImporting}
            onRefresh={page.loadInstances}
            refreshDisabled={page.isLoading}
            onSelectInstance={page.handleSelectInstance}
            onContextMenuInstance={page.handleContextMenuInstance}
            inlineMenuInstanceId={page.inlineMenuInstanceId}
            inlineMenuRef={page.inlineMenuRef}
            exportingInstanceId={page.exportingInstance}
            exportDisabled={page.exportingInstance !== null}
            onEdit={(inst) => {
              page.setSelectedInstance(inst);
              page.setShowEditModal(true);
              page.setInlineMenuInstanceId(null);
            }}
            onOpenFolder={(inst) => {
              page.instanceActions.openFolder(inst.id);
              page.setInlineMenuInstanceId(null);
            }}
            onOpenModsFolder={(inst) => {
              openInstanceModsFolder(inst.id);
              page.setInlineMenuInstanceId(null);
            }}
            onExport={(inst) => {
              page.instanceActions.handleExport(inst, page.setExportingInstance);
              page.setInlineMenuInstanceId(null);
            }}
            onDelete={(inst) => {
              page.setInstanceToDelete(inst);
              page.setInlineMenuInstanceId(null);
            }}
            getDisplayName={page.getInstanceDisplayName}
            getIcon={renderInstanceIcon}
            getValidationInfo={(inst) => ({
              ...page.getValidationInfo(inst),
              icon: getValidationIcon(inst),
            })}
            formatSize={formatBytes}
            tCommonUnknown={t('common.unknown')}
            tAddInstance={t('instances.addInstance')}
            tNoInstances={t('instances.noInstances')}
            tImport={t('instances.import')}
            tOpenModsFolder={t('modManager.openModsFolder')}
            tCommonRefresh={t('common.refresh')}
            tCommonEdit={t('common.edit')}
            tCommonOpenFolder={t('common.openFolder')}
            tCommonExport={t('common.export')}
            tCommonDelete={t('common.delete')}
          />

          {/* Main Content */}
          <div className="flex-1 flex flex-col min-w-0">
            {page.selectedInstance ? (
              <>
                <div className="flex-1 flex flex-col overflow-hidden rounded-2xl glass-panel-static-solid">
                  {/* Tabs & Actions */}
                  <div className="flex items-center justify-between gap-4 px-3 py-3 flex-shrink-0 border-b border-white/[0.06]">
                    <AccentSegmentedControl
                      value={page.activeTab}
                      onChange={page.setActiveTab}
                      items={page.tabs.map((tab) => {
                        const isTabDisabled = tab === 'browse' && page.selectedInstance?.validationStatus !== 'Valid';
                        return {
                          value: tab,
                          label: page.getTabLabel(tab),
                          disabled: isTabDisabled,
                          title: isTabDisabled ? t('instances.instanceNotInstalled') : undefined,
                          className: 'text-sm font-bold',
                        };
                      })}
                    />

                    <div className="flex items-center gap-2">
                      {/* Play/Stop/Download Button */}
                      {(() => {
                        const isThisRunning = page.isGameRunning && (!page.runningInstanceId || page.runningInstanceId === page.selectedInstance?.id);
                        const isThisDownloading = page.isDownloading && page.downloadingInstanceId === page.selectedInstance?.id;
                        const isInstalled = page.selectedInstance?.validationStatus === 'Valid';

                        if (isThisRunning) {
                          return (
                            <LauncherActionButton variant="stop" onClick={() => page.selectedInstance && page.handleLaunchInstance(page.selectedInstance)} className="h-10 px-4 rounded-xl text-sm">
                              <X size={16} />
                              {t('main.stop')}
                            </LauncherActionButton>
                          );
                        }

                        if (isThisDownloading) {
                          const stateKey = `launch.state.${launchState}`;
                          const stateLabel = t(stateKey) !== stateKey ? t(stateKey) : (launchState || t('launch.state.preparing'));
                          return (
                            <div
                              className={`px-4 py-2 flex items-center justify-center relative overflow-hidden rounded-xl min-w-[140px] ${canCancel ? 'cursor-pointer' : 'cursor-default'}`}
                              style={{ background: 'rgba(255,255,255,0.05)' }}
                              onClick={() => canCancel && onCancelDownload?.()}
                            >
                              {total > 0 && (
                                <div className="absolute inset-0 transition-all duration-300" style={{ width: `${Math.min(progress, 100)}%`, backgroundColor: `${accentColor}40` }} />
                              )}
                              <div className="relative z-10 flex items-center gap-2">
                                <Loader2 size={14} className="animate-spin text-white" />
                                <span className="text-sm font-bold text-white">{stateLabel}</span>
                                {canCancel && <span className="ml-1 text-xs text-red-400 hover:text-red-300"><X size={12} className="inline" /></span>}
                              </div>
                            </div>
                          );
                        }

                        if (page.isGameRunning && !!page.runningInstanceId) {
                          return (
                            <LauncherActionButton variant="play" disabled className="h-10 px-4 text-sm">
                              <Play size={16} fill="currentColor" />
                              {t('main.play')}
                            </LauncherActionButton>
                          );
                        }

                        if (!isInstalled) {
                          const anotherDownloading = page.isDownloading && !isThisDownloading;
                          const downloadDisabled = anotherDownloading || !hasDownloadSources;
                          return (
                            <LauncherActionButton
                              variant="download"
                              onClick={() => page.selectedInstance && !downloadDisabled && page.handleLaunchInstance(page.selectedInstance)}
                              disabled={downloadDisabled}
                              className="h-10 px-4 rounded-xl text-sm"
                              title={!hasDownloadSources ? t('instances.noDownloadSources') : undefined}
                            >
                              <Download size={16} />
                              {t('main.download')}
                            </LauncherActionButton>
                          );
                        }

                        if (officialServerBlocked) {
                          return (
                            <div className="relative group">
                              <LauncherActionButton variant="play" disabled className="h-10 px-4 text-sm">
                                <Play size={16} fill="currentColor" />
                                {t('main.play')}
                              </LauncherActionButton>
                              <div className="absolute bottom-full left-1/2 -translate-x-1/2 mb-2 px-3 py-2 bg-black/90 text-white text-xs rounded-lg opacity-0 group-hover:opacity-100 transition-opacity whitespace-nowrap pointer-events-none z-50">
                                {t('main.officialServerBlocked')}
                              </div>
                            </div>
                          );
                        }

                        return (
                          <LauncherActionButton variant="play" onClick={() => page.selectedInstance && page.handleLaunchInstance(page.selectedInstance)} className="h-10 px-4 rounded-xl text-sm">
                            <Play size={16} fill="currentColor" />
                            {t('main.play')}
                          </LauncherActionButton>
                        );
                      })()}

                      {/* Settings Menu */}
                      <div className="relative" ref={page.instanceMenuRef}>
                        <IconButton onClick={() => page.setShowInstanceMenu(!page.showInstanceMenu)}>
                          <MoreVertical size={18} />
                        </IconButton>

                        <AnimatePresence>
                          {page.showInstanceMenu && (
                            <motion.div
                              initial={{ opacity: 0, y: -8, scale: 0.96 }}
                              animate={{ opacity: 1, y: 0, scale: 1 }}
                              exit={{ opacity: 0, y: -8, scale: 0.96 }}
                              transition={{ duration: 0.15, ease: [0.4, 0, 0.2, 1] }}
                              className="absolute right-0 top-full mt-2 w-48 bg-[#1c1c1e] border border-white/[0.08] rounded-xl shadow-xl z-50 overflow-hidden"
                            >
                              <MenuItemButton onClick={() => { page.setShowEditModal(true); page.setShowInstanceMenu(false); }}>
                                <Edit2 size={14} />
                                {t('common.edit')}
                              </MenuItemButton>
                              <MenuItemButton onClick={() => { page.instanceActions.openFolder(page.selectedInstance!.id); page.setShowInstanceMenu(false); }}>
                                <FolderOpen size={14} />
                                {t('common.openFolder')}
                              </MenuItemButton>
                              <MenuItemButton onClick={() => { openInstanceModsFolder(page.selectedInstance!.id); page.setShowInstanceMenu(false); }}>
                                <Package size={14} />
                                {t('modManager.openModsFolder')}
                              </MenuItemButton>
                              <MenuItemButton onClick={() => { page.instanceActions.handleExport(page.selectedInstance!, page.setExportingInstance); page.setShowInstanceMenu(false); }} disabled={page.exportingInstance !== null}>
                                {page.exportingInstance === page.selectedInstance?.id ? <Loader2 size={14} className="animate-spin" /> : <Upload size={14} />}
                                {t('common.export')}
                              </MenuItemButton>
                              <div className="border-t border-white/10 my-1" />
                              <MenuItemButton onClick={() => { page.setInstanceToDelete(page.selectedInstance); page.setShowInstanceMenu(false); }} variant="danger">
                                <Trash2 size={14} />
                                {t('common.delete')}
                              </MenuItemButton>
                            </motion.div>
                          )}
                        </AnimatePresence>
                      </div>
                    </div>
                  </div>

                  {/* Download Progress */}
                  <AnimatePresence>
                    {page.isDownloading && page.downloadingInstanceId === page.selectedInstance?.id && launchState !== 'complete' && (
                      <motion.div
                        initial={{ opacity: 0, height: 0 }}
                        animate={{ opacity: 1, height: 'auto' }}
                        exit={{ opacity: 0, height: 0 }}
                        transition={{ duration: 0.2 }}
                        className={`px-4 py-2 border-b border-white/[0.06] flex-shrink-0 ${canCancel ? 'cursor-pointer' : ''}`}
                        onClick={() => canCancel && onCancelDownload?.()}
                      >
                        {total > 0 ? (
                          <>
                            <div className="h-1.5 w-full bg-[#1c1c1e] rounded-full overflow-hidden mb-1.5">
                              <div className="h-full rounded-full transition-all duration-300" style={{ width: `${Math.min(progress, 100)}%`, backgroundColor: accentColor }} />
                            </div>
                            <div className="flex justify-between items-center text-[10px]">
                              <span className="text-white/60 truncate max-w-[280px]">
                                {launchDetail ? (t(launchDetail) !== launchDetail ? t(launchDetail).replace('{0}', `${Math.min(Math.round(progress), 100)}`) : launchDetail) : (() => { const k = `launch.state.${launchState}`; const v = t(k); return v !== k ? v : (launchState || t('launch.state.preparing')); })()}
                              </span>
                              <div className="flex items-center gap-2">
                                <span className="text-white/50 font-mono">{`${formatBytes(downloaded)} / ${formatBytes(total)}`}</span>
                                {canCancel && <span className="text-red-400 hover:text-red-300 transition-colors text-[9px] font-bold uppercase"><X size={10} className="inline" /> {t('main.cancel')}</span>}
                              </div>
                            </div>
                          </>
                        ) : (
                            <div className="flex justify-between items-center text-[10px]">
                              <div className="flex items-center gap-2">
                                <Loader2 size={12} className="animate-spin text-white opacity-70" />
                                <span className="text-white/60">{launchDetail ? (t(launchDetail) !== launchDetail ? t(launchDetail) : launchDetail) : (launchState || t('launch.state.preparing'))}</span>
                              </div>
                              <div className="flex items-center gap-2">
                                <span className="text-white/50 font-mono">{props.speed && props.speed > 0 ? `${formatBytes(downloaded)} • ${formatBytes(props.speed)}/s` : `${formatBytes(downloaded)}`}</span>
                                {canCancel && <span className="text-red-400 hover:text-red-300 transition-colors text-[9px] font-bold uppercase"><X size={10} className="inline" /> {t('main.cancel')}</span>}
                              </div>
                            </div>
                        )}
                      </motion.div>
                    )}
                  </AnimatePresence>

                  {/* Tab Content */}
                  <div className="flex-1 overflow-hidden relative">
                    {/* Content tab */}
                    <div className={`absolute inset-0 ${page.activeTab === 'content' ? 'opacity-100 z-10' : 'opacity-0 z-0 pointer-events-none'}`}>
                      <ContentTab
                        selectedInstance={page.selectedInstance}
                        installedMods={page.modManager.installedMods}
                        filteredMods={page.modManager.filteredMods}
                        isLoadingMods={page.modManager.isLoadingMods}
                        modsSearchQuery={page.modManager.modsSearchQuery}
                        setModsSearchQuery={page.modManager.setModsSearchQuery}
                        selectedMods={page.modManager.selectedMods}
                        modsWithUpdates={page.modManager.modsWithUpdates}
                        updateCount={page.modManager.updateCount}
                        modDetailsCache={page.modManager.modDetailsCache}
                        loadInstalledMods={page.modManager.loadInstalledMods}
                        toggleModSelection={page.modManager.toggleModSelection}
                        handleShiftSelect={page.modManager.handleShiftSelect}
                        selectOnlyMod={page.modManager.selectOnlyMod}
                        selectAllMods={page.modManager.selectAllMods}
                        handleToggleMod={page.modManager.handleToggleMod}
                        handleBulkToggleMods={page.modManager.handleBulkToggleMods}
                        setModToDelete={page.setModToDelete}
                        setShowBulkUpdateConfirm={page.setShowBulkUpdateConfirm}
                        setShowBulkDeleteConfirm={page.setShowBulkDeleteConfirm}
                        onTabChange={page.setActiveTab}
                        onDropImportMods={page.handleDropImportMods}
                        isUpdatingMods={page.isUpdatingMods}
                        isBulkTogglingMods={page.isBulkTogglingMods}
                        isDeletingMod={page.isDeletingMod}
                        getDisplayVersion={page.getDisplayVersion}
                        isLocalInstalledMod={page.isLocalInstalledMod}
                        isTrustedRemoteIdentity={page.isTrustedRemoteIdentity}
                        getCurseForgeUrlFromDetails={page.getCurseForgeUrlFromDetails}
                        handleOpenModPage={page.handleOpenModPage}
                      />
                    </div>

                    {/* Browse tab */}
                    <div className={`absolute inset-0 ${page.activeTab === 'browse' ? 'opacity-100 z-10' : 'opacity-0 z-0 pointer-events-none'}`}>
                      {page.selectedInstance && (
                        <InlineModBrowser
                          currentInstanceId={page.selectedInstance.id}
                          installedModIds={new Set(page.modManager.installedMods.map(m => m.curseForgeId ? `cf-${m.curseForgeId}` : m.id))}
                          installedFileIds={new Set(page.modManager.installedMods.filter(m => m.fileId).map(m => String(m.fileId)))}
                          onModsInstalled={() => page.modManager.loadInstalledMods()}
                          onBack={() => page.setActiveTab('content')}
                          refreshSignal={page.browseRefreshSignal}
                        />
                      )}
                    </div>

                    {/* Worlds tab */}
                    <div className={`absolute inset-0 ${page.activeTab === 'worlds' ? 'opacity-100 z-10' : 'opacity-0 z-0 pointer-events-none'}`}>
                      <WorldsTab
                        selectedInstance={page.selectedInstance}
                        saves={page.saves}
                        isLoadingSaves={page.isLoadingSaves}
                        loadSaves={page.loadSaves}
                        setMessage={page.setMessage}
                      />
                    </div>
                  </div>
                </div>

                {/* Edit Instance Modal */}
                <EditInstanceModal
                  isOpen={page.showEditModal}
                  onClose={() => page.setShowEditModal(false)}
                  onSave={() => page.loadInstances()}
                  instanceId={page.selectedInstance.id}
                  initialName={page.selectedInstance.customName || page.getInstanceDisplayName(page.selectedInstance)}
                  initialIconUrl={page.instanceIcons[page.selectedInstance.id]}
                  initialBranch={page.selectedInstance.branch}
                  initialVersion={page.selectedInstance.version}
                />
              </>
            ) : page.instances.length === 0 ? (
              <div className="flex-1 flex flex-col items-center justify-center rounded-2xl glass-panel-static-solid">
                <HardDrive size={64} className="mb-4 text-white opacity-20" />
                <p className="text-xl font-medium text-white/70">{t('instances.noInstances')}</p>
                <p className="text-sm mt-2 text-white/40 text-center max-w-xs">{t('instances.createInstanceHint')}</p>
                <Button variant="primary" onClick={() => page.setShowCreateModal(true)} className="mt-6 px-6 py-3 font-bold shadow-lg">
                  <Plus size={18} />
                  {t('instances.createInstance')}
                </Button>
              </div>
            ) : (
              <div className="flex-1 flex flex-col items-center justify-center text-white/30">
                <HardDrive size={64} className="mb-4 opacity-30" />
                <p className="text-xl font-medium">{t('instances.selectInstance')}</p>
                <p className="text-sm mt-2">{t('instances.selectInstanceHint')}</p>
              </div>
            )}
          </div>

          {/* Message Toast */}
          <AnimatePresence>
            {page.message && (
              <motion.div
                initial={{ opacity: 0, y: 50 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: 50 }}
                className={`fixed bottom-32 left-1/2 -translate-x-1/2 px-4 py-2 rounded-xl text-sm flex items-center gap-2 z-50 ${
                  page.message.type === 'success' ? 'bg-green-500/20 text-green-400 border border-green-500/20' : 'bg-red-500/20 text-red-400 border border-red-500/20'
                }`}
              >
                {page.message.type === 'success' ? <Check size={14} /> : <AlertTriangle size={14} />}
                {page.message.text}
              </motion.div>
            )}
          </AnimatePresence>

          {/* Delete Instance Modal */}
          <ConfirmationModal
            isOpen={!!page.instanceToDelete}
            onClose={() => page.setInstanceToDelete(null)}
            onConfirm={() => page.instanceToDelete && page.instanceActions.handleDelete(page.instanceToDelete, page.selectedInstance, page.setSelectedInstance, page.setInstanceToDelete, page.onInstanceDeleted)}
            title={t('instances.deleteTitle')}
            message={<>{t('instances.deleteConfirm')} <strong>{page.instanceToDelete && page.getInstanceDisplayName(page.instanceToDelete)}</strong>?</>}
            confirmText={t('common.delete')}
            cancelText={t('common.cancel')}
            variant="danger"
          />

          {/* Delete Mod Modal */}
          <ConfirmationModal
            isOpen={!!page.modToDelete}
            onClose={() => page.setModToDelete(null)}
            onConfirm={page.handleDeleteMod}
            title={t('modManager.deleteModTitle')}
            message={<>{t('modManager.deleteModConfirm')} <strong>{page.modToDelete?.name}</strong>?</>}
            confirmText={t('common.delete')}
            cancelText={t('common.cancel')}
            variant="danger"
            isLoading={page.isDeletingMod}
          />

          {/* Bulk Update Modal */}
          <BulkUpdateModal
            isOpen={page.showBulkUpdateConfirm}
            onClose={() => page.setShowBulkUpdateConfirm(false)}
            modList={page.modManager.bulkUpdateList}
            modDetailsCache={page.modManager.modDetailsCache}
            prefetchModDetails={page.prefetchModDetails}
            getCurseForgeModId={page.getCurseForgeModId}
            onUpdate={async (ids) => {
              page.setIsUpdatingMods(true);
              await page.modManager.handleBulkUpdateMods(ids);
              page.setIsUpdatingMods(false);
            }}
            isUpdating={page.isUpdatingMods}
          />

          {/* Bulk Delete Modal */}
          <BulkDeleteModal
            isOpen={page.showBulkDeleteConfirm}
            onClose={() => page.setShowBulkDeleteConfirm(false)}
            modList={page.bulkDeleteList}
            modDetailsCache={page.modManager.modDetailsCache}
            prefetchModDetails={page.prefetchModDetails}
            getCurseForgeModId={page.getCurseForgeModId}
            onDelete={async (ids) => {
              page.setIsDeletingMod(true);
              await page.modManager.handleBulkDeleteMods(ids);
              page.setIsDeletingMod(false);
            }}
            isDeleting={page.isDeletingMod}
          />

          {/* Create Instance Modal */}
          <CreateInstanceModal
            isOpen={page.showCreateModal}
            onClose={() => page.setShowCreateModal(false)}
            onCreateStart={() => page.loadInstances()}
          />
        </div>
      </PageContainer>
    </motion.div>
  );
};

// #endregion
