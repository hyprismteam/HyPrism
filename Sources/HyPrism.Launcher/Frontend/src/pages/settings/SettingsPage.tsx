import React, { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import { 
  X, Settings, Download, Coffee, Image, Wifi, Monitor, 
  Terminal, FileText, Database, Globe, Code, AlertTriangle, 
  Trash2, ExternalLink,
  type LucideIcon
} from 'lucide-react';
import { openUrl } from '@/utils/openUrl';

// Hooks
import { useSettings, type SettingsTab } from '@/hooks/useSettings';
import { useMirrorSpeedTests } from '@/hooks/useMirrorSpeedTests';

// Components
import { IconButton, Button, Switch } from '@/components/ui/Controls';
import { LogsPage } from '@/pages/LogsPage';

// Tab components
import {
  GeneralTab,
  DownloadsTab,
  JavaTab,
  VisualTab,
  NetworkTab,
  GraphicsTab,
  VariablesTab,
  DataTab,
  AboutTab,
  DeveloperTab,
} from './tabs';

export interface SettingsPageProps {
  onClose?: () => void;
  launcherBranch: string;
  onLauncherBranchChange: (branch: string) => void;
  rosettaWarning?: { message: string; command: string; tutorialUrl?: string } | null;
  onBackgroundModeChange?: (mode: string) => void;
  onInstanceDeleted?: () => void;
  onAuthSettingsChange?: () => void;
  isGameRunning?: boolean;
  onMovingDataChange?: (isMoving: boolean) => void;
}

export const SettingsPage: React.FC<SettingsPageProps> = ({
  onClose,
  launcherBranch,
  onLauncherBranchChange,
  rosettaWarning,
  onBackgroundModeChange,
  onInstanceDeleted,
  onAuthSettingsChange,
  isGameRunning = false,
  onMovingDataChange,
}) => {
  const { t } = useTranslation();

  // Main settings hook
  const settings = useSettings({
    launcherBranch,
    onLauncherBranchChange,
    onBackgroundModeChange,
    onInstanceDeleted,
    onAuthSettingsChange,
    onMovingDataChange,
    onClose,
  });

  // Mirror speed tests hook
  const mirrorTests = useMirrorSpeedTests();

  // Tab configuration
  const tabs: { id: SettingsTab; icon: LucideIcon; label: string }[] = [
    { id: 'general', icon: Settings, label: t('settings.general') },
    { id: 'downloads', icon: Download, label: t('settings.downloads.title') },
    { id: 'java', icon: Coffee, label: t('settings.java') },
    { id: 'visual', icon: Image, label: t('settings.visual') },
    { id: 'network', icon: Wifi, label: t('settings.network') },
    { id: 'graphics', icon: Monitor, label: t('settings.graphics') },
    ...(settings.isLinux ? [{ id: 'variables' as const, icon: Terminal, label: t('settings.variables') }] : []),
    { id: 'logs', icon: FileText, label: t('logs.title') },
    { id: 'data', icon: Database, label: t('settings.data') },
    { id: 'about', icon: Globe, label: t('settings.about') },
    ...(settings.devModeEnabled ? [{ id: 'developer' as const, icon: Code, label: t('settings.developer') }] : []),
  ];

  // Escape key handler
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && onClose) {
        onClose();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [onClose]);

  // Open launcher folder
  const handleOpenLauncherFolder = async () => {
    if (settings.launcherFolderPath) {
      openUrl(`file://${encodeURI(settings.launcherFolderPath)}`);
    }
  };

  return (
    <>
      <div className="w-full h-full flex gap-4">
        <div className="contents">
          {/* Sidebar */}
          <div className="w-52 h-full min-h-0 flex-shrink-0 flex flex-col py-4 rounded-2xl glass-panel-static-solid">
            <nav className="flex-1 space-y-1 px-2">
              {tabs.map((tab) => (
                <button
                  key={tab.id}
                  onClick={() => settings.setActiveTab(tab.id)}
                  className={`w-full flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm transition-[color,background-color,opacity] ${settings.activeTab === tab.id
                    ? 'bg-white/10 text-white opacity-100'
                    : 'text-white opacity-60 hover:opacity-100 hover:bg-white/5'
                  }`}
                  style={settings.activeTab === tab.id ? { backgroundColor: `${settings.accentColor}20`, color: settings.accentColor } : {}}
                >
                  <tab.icon size={18} />
                  <span>{tab.label}</span>
                </button>
              ))}
            </nav>
            
            {/* Dev Mode Toggle */}
            <div className="px-2 pt-4 border-t border-white/[0.06] mx-2">
              <div
                className="flex items-center justify-between px-3 py-2 rounded-lg cursor-pointer hover:bg-white/5 transition-colors"
                onClick={settings.handleDevModeToggle}
              >
                <div className="flex items-center gap-2 text-white/40">
                  <Code size={14} />
                  <span className="text-xs">{t('settings.devMode')}</span>
                </div>
                <Switch checked={settings.devModeEnabled} onCheckedChange={settings.handleDevModeToggle} className="scale-[0.6] origin-right" />
              </div>
            </div>
          </div>

          {/* Content */}
          <div className="flex-1 h-full min-h-0 flex flex-col min-w-0 overflow-hidden rounded-2xl glass-panel-static-solid">
            {/* Header */}
            {settings.activeTab !== 'logs' && (
              <div className="flex items-center justify-between p-4 border-b border-white/[0.06]">
                <h3 className="text-white font-medium">{tabs.find(tab => tab.id === settings.activeTab)?.label}</h3>
                {onClose && (
                  <IconButton variant="ghost" size="sm" onClick={onClose} title={t('common.close')}>
                    <X size={18} />
                  </IconButton>
                )}
              </div>
            )}

            {/* Scrollable Content */}
            <div className={settings.activeTab === 'logs' ? 'flex-1 min-h-0' : 'flex-1 overflow-y-auto p-6 space-y-6'}>
              {/* Rosetta Warning */}
              {rosettaWarning && settings.activeTab !== 'logs' && (
                <div className="p-4 bg-yellow-500/10 border border-yellow-500/30 rounded-xl">
                  <div className="flex items-start gap-3">
                    <AlertTriangle size={20} className="text-yellow-500 flex-shrink-0 mt-0.5" />
                    <div className="flex-1">
                      <p className="text-yellow-500 text-sm font-medium mb-2">{rosettaWarning.message}</p>
                      <div className="flex flex-col gap-2">
                        <code className="text-xs text-white/70 bg-[#1c1c1e] px-2 py-1 rounded font-mono break-all">
                          {rosettaWarning.command}
                        </code>
                        {rosettaWarning.tutorialUrl && (
                          <button
                            onClick={() => openUrl(rosettaWarning.tutorialUrl!)}
                            className="text-xs w-fit flex items-center gap-1"
                            style={{ color: settings.accentColor }}
                          >
                            <ExternalLink size={12} />
                            {t('settings.generalSettings.watchTutorial')}
                          </button>
                        )}
                      </div>
                    </div>
                  </div>
                </div>
              )}

              {/* Tab Content */}
              {settings.activeTab === 'general' && (
                <GeneralTab
                  gc={settings.gc}
                  accentColor={settings.accentColor}
                  isLanguageOpen={settings.isLanguageOpen}
                  setIsLanguageOpen={settings.setIsLanguageOpen}
                  languageDropdownRef={settings.languageDropdownRef as React.RefObject<HTMLDivElement>}
                  handleLanguageSelect={settings.handleLanguageSelect}
                  isBranchOpen={settings.isBranchOpen}
                  setIsBranchOpen={settings.setIsBranchOpen}
                  branchDropdownRef={settings.branchDropdownRef as React.RefObject<HTMLDivElement>}
                  selectedLauncherBranch={settings.selectedLauncherBranch}
                  handleLauncherBranchChange={settings.handleLauncherBranchChange}
                  closeAfterLaunch={settings.closeAfterLaunch}
                  handleCloseAfterLaunchChange={settings.handleCloseAfterLaunchChange}
                  showAlphaMods={settings.showAlphaMods}
                  handleShowAlphaModsChange={settings.handleShowAlphaModsChange}
                  onlineMode={settings.onlineMode}
                  authMode={settings.authMode}
                  useDualAuth={settings.useDualAuth}
                  handleUseDualAuthChange={settings.handleUseDualAuthChange}
                  isActiveProfileOfficial={settings.isActiveProfileOfficial}
                  profileLoaded={settings.profileLoaded}
                />
              )}

              {settings.activeTab === 'downloads' && (
                <DownloadsTab
                  hasOfficialAccount={settings.hasOfficialAccount}
                  launchAfterDownload={settings.launchAfterDownload}
                  setLaunchAfterDownload={settings.setLaunchAfterDownload}
                  officialSpeedTest={mirrorTests.officialResult}
                  isOfficialTesting={mirrorTests.isOfficialTesting}
                  handleTestOfficialSpeed={mirrorTests.testOfficial}
                  mirrors={mirrorTests.mirrors}
                  mirrorStates={mirrorTests.mirrorStates}
                  isLoading={mirrorTests.isLoading}
                  testMirror={mirrorTests.testMirror}
                  addMirror={mirrorTests.addMirror}
                  deleteMirror={mirrorTests.deleteMirror}
                  toggleMirror={mirrorTests.toggleMirror}
                  refreshMirrors={mirrorTests.refresh}
                  isAdding={mirrorTests.isAdding}
                  addError={mirrorTests.addError}
                  setAddError={mirrorTests.setAddError}
                />
              )}

              {settings.activeTab === 'java' && (
                <JavaTab
                  gc={settings.gc}
                  accentColor={settings.accentColor}
                  accentTextColor={settings.accentTextColor}
                  javaRuntimeMode={settings.javaRuntimeMode}
                  handleJavaRuntimeModeChange={settings.handleJavaRuntimeModeChange}
                  customJavaPath={settings.customJavaPath}
                  setCustomJavaPath={settings.setCustomJavaPath}
                  javaCustomPathError={settings.javaCustomPathError}
                  handleBrowseCustomJavaPath={settings.handleBrowseCustomJavaPath}
                  handleCustomJavaPathSave={settings.handleCustomJavaPathSave}
                  detectedSystemRamMb={settings.detectedSystemRamMb}
                  minJavaRamMb={settings.minJavaRamMb}
                  maxJavaRamMb={settings.maxJavaRamMb}
                  javaRamMb={settings.javaRamMb}
                  javaInitialRamMb={settings.javaInitialRamMb}
                  handleJavaRamChange={settings.handleJavaRamChange}
                  handleJavaInitialRamChange={settings.handleJavaInitialRamChange}
                  javaGcMode={settings.javaGcMode}
                  handleJavaGcModeChange={settings.handleJavaGcModeChange}
                  javaArguments={settings.javaArguments}
                  setJavaArguments={settings.setJavaArguments}
                  javaArgumentsError={settings.javaArgumentsError}
                  setJavaArgumentsError={settings.setJavaArgumentsError}
                  handleSaveJavaArguments={settings.handleSaveJavaArguments}
                />
              )}

              {settings.activeTab === 'visual' && (
                <VisualTab
                  accentColor={settings.accentColor}
                  handleAccentColorChange={settings.handleAccentColorChange}
                  backgroundMode={settings.backgroundMode}
                  handleBackgroundModeChange={settings.handleBackgroundModeChange}
                />
              )}

              {settings.activeTab === 'network' && (
                <NetworkTab
                  onlineMode={settings.onlineMode}
                  setOnlineMode={settings.setOnlineMode}
                  authMode={settings.authMode}
                  setAuthModeState={settings.setAuthModeState}
                  authDomain={settings.authDomain}
                  setAuthDomain={settings.setAuthDomain}
                  customAuthDomain={settings.customAuthDomain}
                  setCustomAuthDomain={settings.setCustomAuthDomain}
                  onAuthSettingsChange={onAuthSettingsChange}
                  isActiveProfileOfficial={settings.isActiveProfileOfficial}
                />
              )}

              {settings.activeTab === 'graphics' && (
                <GraphicsTab
                  accentColor={settings.accentColor}
                  accentTextColor={settings.accentTextColor}
                  gpuPreference={settings.gpuPreference}
                  gpuAdapters={settings.gpuAdapters}
                  hasSingleGpu={settings.hasSingleGpu}
                  handleGpuPreferenceChange={settings.handleGpuPreferenceChange}
                />
              )}

              {settings.activeTab === 'variables' && (
                <VariablesTab
                  accentColor={settings.accentColor}
                  envForceX11={settings.envForceX11}
                  envDisableVkLayers={settings.envDisableVkLayers}
                  handleEnvPresetToggle={settings.handleEnvPresetToggle}
                  gameEnvVars={settings.gameEnvVars}
                  setGameEnvVars={settings.setGameEnvVars}
                  gameEnvVarsError={settings.gameEnvVarsError}
                  gameEnvVarsFocus={settings.gameEnvVarsFocus}
                  setGameEnvVarsFocus={settings.setGameEnvVarsFocus}
                  handleSaveGameEnvVars={settings.handleSaveGameEnvVars}
                />
              )}

              {settings.activeTab === 'logs' && <LogsPage embedded />}

              {settings.activeTab === 'data' && (
                <DataTab
                  gc={settings.gc}
                  isGameRunning={isGameRunning}
                  instanceDir={settings.instanceDir}
                  launcherDataDir={settings.launcherDataDir}
                  handleBrowseInstanceDir={settings.handleBrowseInstanceDir}
                  handleResetInstanceDir={settings.handleResetInstanceDir}
                  handleOpenLauncherFolder={handleOpenLauncherFolder}
                  setShowDeleteConfirm={settings.setShowDeleteConfirm}
                />
              )}

              {settings.activeTab === 'about' && (
                <AboutTab
                  gc={settings.gc}
                  accentColor={settings.accentColor}
                  contributors={settings.contributors}
                  isLoadingContributors={settings.isLoadingContributors}
                  contributorsError={settings.contributorsError}
                  openGitHub={settings.openGitHub}
                  openDiscord={settings.openDiscord}
                  openBugReport={settings.openBugReport}
                  resetOnboarding={settings.resetOnboarding}
                />
              )}

              {settings.activeTab === 'developer' && settings.devModeEnabled && (
                <DeveloperTab
                  gc={settings.gc}
                  accentColor={settings.accentColor}
                  activeTab={settings.activeTab}
                  selectedLauncherBranch={settings.selectedLauncherBranch}
                  resetOnboarding={settings.resetOnboarding}
                />
              )}
            </div>
          </div>
        </div>
      </div>

      {/* Data Moving Overlay */}
      <AnimatePresence>
        {settings.isMovingData && (
          <motion.div
            className="fixed inset-0 z-[300] flex items-center justify-center bg-[#0a0a0a]/95"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.2 }}
          >
            <motion.div
              className="max-w-lg w-full mx-8 text-center"
              initial={{ opacity: 0, scale: 0.95, y: 10 }}
              animate={{ opacity: 1, scale: 1, y: 0 }}
              exit={{ opacity: 0, scale: 0.95, y: 10 }}
              transition={{ duration: 0.25, delay: 0.05 }}
            >
              <motion.div
                key="moving-progress"
                initial={{ opacity: 0, y: 6, scale: 0.99 }}
                animate={{ opacity: 1, y: 0, scale: 1 }}
                exit={{ opacity: 0, y: -10, scale: 0.98 }}
                transition={{ duration: 0.3, ease: 'easeOut' }}
              >
                <h2 className="text-2xl font-bold text-white mb-2">{t('settings.dataSettings.movingData')}</h2>
                <p className="text-white/60 mb-8">{t('settings.dataSettings.movingDataHint', { file: settings.moveCurrentFile || '...' })}</p>

                {/* Progress bar */}
                <div className="relative h-3 bg-white/10 rounded-full overflow-hidden mb-4">
                  <motion.div
                    className="absolute inset-y-0 left-0 rounded-full"
                    initial={{ width: 0 }}
                    animate={{ width: `${settings.moveProgress}%` }}
                    transition={{ duration: 0.3, ease: 'easeOut' }}
                    style={{ backgroundColor: settings.accentColor }}
                  />
                  {settings.moveProgress === 0 && (
                    <motion.div
                      className="absolute inset-y-0 w-1/3 rounded-full"
                      style={{
                        background: `linear-gradient(90deg, transparent, ${settings.accentColor}80, transparent)`
                      }}
                      animate={{ x: ['-100%', '400%'] }}
                      transition={{ duration: 1.5, repeat: Infinity, ease: 'linear' }}
                    />
                  )}
                </div>

                <div className="flex justify-center text-sm">
                  <span className="text-white/80">{settings.moveProgress > 0 ? `${settings.moveProgress}%` : ''}</span>
                </div>
              </motion.div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>

      {/* Delete Confirmation Modal */}
      {settings.showDeleteConfirm && (
        <div className="fixed inset-0 z-[250] flex items-center justify-center bg-[#0a0a0a]/90">
          <div className="p-6 max-w-md w-full mx-4 glass-panel-static-solid">
            <div className="flex items-center gap-3 mb-4">
              <div className="w-10 h-10 rounded-full bg-red-500/20 flex items-center justify-center">
                <Trash2 size={20} className="text-red-400" />
              </div>
              <h3 className="text-lg font-bold text-white">{t('settings.deleteAllData.title')}</h3>
            </div>
            <p className="text-white/70 text-sm mb-6">
              {t('settings.deleteAllData.message')}
            </p>
            <div className="flex gap-3">
              <Button onClick={() => settings.setShowDeleteConfirm(false)} className="flex-1">
                {t('common.cancel')}
              </Button>
              <Button variant="danger" onClick={settings.handleDeleteLauncherData} className="flex-1">
                {t('common.delete')}
              </Button>
            </div>
          </div>
        </div>
      )}
    </>
  );
};

// Re-export for backwards compatibility
export { SettingsPage as SettingsModal };
