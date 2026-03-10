import React, { useState, useEffect, memo } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { useTranslation } from 'react-i18next';
import { Play, Download, Loader2, X, RefreshCw, User, ShieldAlert, ArrowRight, AlertTriangle } from 'lucide-react';
import { useAccentColor } from '../contexts/AccentColorContext';

import { ipc, InstanceInfo } from '@/lib/ipc';
import { DiscordIcon } from '../components/icons/DiscordIcon';
import { formatBytes } from '../utils/format';
import previewLogo from '../assets/images/preview_logo.png';
import { PageContainer } from '@/components/ui/PageContainer';
import { Button, IconButton, LauncherActionButton, LinkButton } from '@/components/ui/Controls';
import { pageVariants } from '@/constants/animations';

interface DashboardPageProps {
  // Profile
  username: string;
  uuid: string;
  launcherVersion: string;
  updateAvailable: boolean;
  launcherUpdateInfo?: {
    currentVersion: string;
    latestVersion: string;
    changelog?: string;
    releaseUrl?: string;
  } | null;
  avatarRefreshTrigger: number;
  onOpenProfileEditor: () => void;
  // Game state
  isDownloading: boolean;
  downloadState: 'downloading' | 'extracting' | 'launching';
  canCancel: boolean;
  isGameRunning: boolean;
  progress: number;
  downloaded: number;
  total: number;
  launchState: string;
  launchDetail: string;
  speed?: number;
  // Instance-based
  selectedInstance: InstanceInfo | null;
  instances: InstanceInfo[];
  hasInstances: boolean;
  isCheckingInstance: boolean;
  hasUpdateAvailable: boolean;
  // Download sources
  hasDownloadSources: boolean;
  // Actions
  onPlay: () => void;
  onStopGame: () => void;
  onDownload: () => void;
  onUpdate: () => void;
  onCancelDownload: () => void;
  onNavigateToInstances: () => void;
  // Official server state
  officialServerBlocked: boolean;
  isOfficialProfile: boolean;
  isOfficialServerMode: boolean;
}

export const DashboardPage: React.FC<DashboardPageProps> = memo((props) => {
  const { t } = useTranslation();
  const { accentColor } = useAccentColor();

  const [localAvatar, setLocalAvatar] = useState<string | null>(null);
  const [showCancelButton, setShowCancelButton] = useState(false);
  const [selectedInstanceIcon, setSelectedInstanceIcon] = useState<string | null>(null);
  const [showNoSourcesModal, setShowNoSourcesModal] = useState(false);

  const withCacheBust = (iconUrl: string) => {
    if (!iconUrl) return iconUrl;
    const separator = iconUrl.includes('?') ? '&' : '?';
    return `${iconUrl}${separator}t=${Date.now()}`;
  };

  useEffect(() => {
    ipc.profile.get().then(p => { if (p.avatarPath) setLocalAvatar(p.avatarPath); }).catch(() => {});
  }, [props.uuid, props.avatarRefreshTrigger]);

  useEffect(() => {
    if (!props.isDownloading) setShowCancelButton(false);
  }, [props.isDownloading]);

  const openNoSourcesModal = () => setShowNoSourcesModal(true);
  const closeNoSourcesModal = () => setShowNoSourcesModal(false);

  // Check if selectors should be hidden (during download/launch or game running)
  const shouldHideInfo = props.isDownloading || props.isGameRunning;
  const showInstanceSwitcher = !shouldHideInfo && !!props.selectedInstance && props.instances.length > 0;

  // Eager selected-instance icon: instant local cache + background refresh from backend
  useEffect(() => {
    const selected = props.selectedInstance;
    if (!selected) {
      setSelectedInstanceIcon(null);
      return;
    }

    const selectedId = (selected.id || '').trim();
    if (!selectedId) {
      setSelectedInstanceIcon(null);
      return;
    }

    const storageKey = `hyprism:instance-icon:${selectedId}`;

    try
    {
      const cachedIcon = localStorage.getItem(storageKey);
      if (cachedIcon)
      {
        setSelectedInstanceIcon(withCacheBust(cachedIcon));
      }
      else
      {
        setSelectedInstanceIcon(null);
      }
    }
    catch
    {
      // Ignore localStorage access errors
    }

    let cancelled = false;
    const loadSelectedIcon = async () => {
      try {
        const icon = await ipc.instance.getIcon({ instanceId: selected.id });
        if (cancelled) return;

        if (icon) {
          try { localStorage.setItem(storageKey, icon); } catch { /* ignore */ }
          setSelectedInstanceIcon(withCacheBust(icon));
        } else {
          try { localStorage.removeItem(storageKey); } catch { /* ignore */ }
          setSelectedInstanceIcon(null);
        }
      } catch {
        // Ignore selected icon loading errors
      }
    };

    loadSelectedIcon();
    return () => {
      cancelled = true;
    };
  }, [props.selectedInstance]);

  // Get translated launch state label
  const getLaunchStateLabel = () => {
    const stateKey = `launch.state.${props.launchState}`;
    const translated = t(stateKey);
    // If translation returns the key itself, fall back to raw state or generic text
    return translated !== stateKey ? translated : (props.launchState || t('launch.state.preparing'));
  };

  // Compute display name with fallback (like InstancesPage)
  const getInstanceDisplayName = (inst?: InstanceInfo | null) => {
    const target = inst ?? props.selectedInstance;
    if (!target) return '';
    const { name, branch, version } = target;
    if (name && name.trim()) return name;
    const branchLabel = branch === 'release' ? t('common.release') : t('common.preRelease');
    return `${branchLabel} ${version}`;
  };

  // Render an instance icon (custom image or version badge)
  const renderInstanceIcon = (inst: InstanceInfo, size: number = 28, full: boolean = false) => {
    const isSelected = inst.id === props.selectedInstance?.id;
    const customIcon = isSelected ? selectedInstanceIcon : null;
    if (customIcon) {
      return (
        <img
          src={customIcon}
          alt=""
          className={full ? 'w-full h-full object-cover rounded-[inherit]' : 'rounded-lg object-cover'}
          style={full ? undefined : { width: size, height: size }}
          onError={() => setSelectedInstanceIcon(null)}
        />
      );
    }
    const versionLabel = inst.version > 0 ? `${inst.version}` : '?';
    return (
      <span className="font-bold" style={{ color: accentColor, fontSize: full ? 20 : size * 0.5 }}>
        {versionLabel}
      </span>
    );
  };

  // Render the action section of the play button
  const renderActionButton = () => {
    // Official servers with unofficial profile — block play
    if (props.officialServerBlocked) {
      return (
        <LauncherActionButton
          variant="play"
          onClick={openNoSourcesModal}
          className="h-full px-8 text-base"
          title={t('onboarding.warning.title', 'No Download Sources')}
        >
          <Play size={16} fill="currentColor" />
          <span>{t('main.play')}</span>
        </LauncherActionButton>
      );
    }

    if (props.isGameRunning) {
      return (
        <LauncherActionButton variant="stop" onClick={props.onStopGame} className="h-full px-8 text-base">
          <X size={16} />
          <span>{t('main.stop')}</span>
        </LauncherActionButton>
      );
    }

    if (props.isDownloading) {
      return (
        <div
          className={`h-full px-6 flex items-center justify-center relative overflow-hidden w-[160px] rounded-2xl ${props.canCancel ? 'cursor-pointer' : 'cursor-default'}`}
          style={{ background: 'rgba(255,255,255,0.05)' }}
          onMouseEnter={() => props.canCancel && setShowCancelButton(true)}
          onMouseLeave={() => setShowCancelButton(false)}
          onClick={() => showCancelButton && props.canCancel && props.onCancelDownload()}
        >
          {props.total > 0 && (
            <div
              className="absolute inset-0 transition-all duration-300"
              style={{ width: `${Math.min(props.progress, 100)}%`, backgroundColor: `${accentColor}40` }}
            />
          )}
          {showCancelButton && props.canCancel ? (
            <div className="relative z-10 flex items-center gap-2 text-red-500 hover:text-red-400 transition-colors">
              <X size={16} />
              <span className="text-xs font-bold uppercase">{t('main.cancel')}</span>
            </div>
          ) : (
            <div className="relative z-10 flex items-center gap-2">
              <Loader2 size={14} className="animate-spin text-white" />
              <span className="text-sm font-bold text-white">{getLaunchStateLabel()}</span>
            </div>
          )}
        </div>
      );
    }

    if (props.isCheckingInstance) {
      return (
        <Button
          disabled
          className="h-full px-8 rounded-2xl font-black tracking-tight text-base bg-white/10 text-white/50 border border-transparent"
        >
          <Loader2 size={16} className="animate-spin" />
          <span>{t('main.checking')}</span>
        </Button>
      );
    }

    // Has instance with update available
    if (props.selectedInstance && props.hasUpdateAvailable) {
      return (
        <div className="flex items-center h-full">
          <LauncherActionButton
            variant="update"
            onClick={props.onUpdate}
            className="h-full px-5 rounded-l-2xl rounded-r-none text-sm"
          >
            <RefreshCw size={14} />
            <span>{t('main.update')}</span>
          </LauncherActionButton>
          <div className="w-px h-6 bg-white/10" />
          <LauncherActionButton variant="play" onClick={props.onPlay} className="h-full px-6 rounded-r-2xl rounded-l-none text-base">
            <Play size={16} fill="currentColor" />
            <span>{t('main.play')}</span>
          </LauncherActionButton>
        </div>
      );
    }

    // Has selected instance - play or download
    if (props.selectedInstance) {
      // Not installed - show download button
      if (!props.selectedInstance.isInstalled) {
        // Disable download if no sources available
        if (!props.hasDownloadSources) {
          return (
            <LauncherActionButton
              variant="download"
              onClick={openNoSourcesModal}
              className="h-full px-8 text-base"
              title={t('instances.noDownloadSources', 'No download sources available')}
            >
              <Download size={16} />
              <span>{t('main.download')}</span>
            </LauncherActionButton>
          );
        }
        return (
          <LauncherActionButton variant="download" onClick={props.onPlay} className="h-full px-8 text-base">
            <Download size={16} />
            <span>{t('main.download')}</span>
          </LauncherActionButton>
        );
      }
      // Installed - show play button
      return (
        <LauncherActionButton variant="play" onClick={props.onPlay} className="h-full px-8 text-lg">
          <Play size={18} fill="currentColor" />
          <span>{t('main.play')}</span>
        </LauncherActionButton>
      );
    }

    // No instance selected but instances exist - go to instances page
    if (props.hasInstances) {
      return (
        <LauncherActionButton variant="select" onClick={props.onNavigateToInstances} className="h-full px-8 text-base">
          <span>{t('main.selectInstance')}</span>
        </LauncherActionButton>
      );
    }

    // No instances - download
    // Disable download if no sources available
    if (!props.hasDownloadSources) {
      return (
        <LauncherActionButton
          variant="download"
          onClick={openNoSourcesModal}
          className="h-full px-8 text-base"
          title={t('instances.noDownloadSources', 'No download sources available')}
        >
          <Download size={16} />
          <span>{t('main.download')}</span>
        </LauncherActionButton>
      );
    }
    return (
      <LauncherActionButton variant="download" onClick={props.onDownload} className="h-full px-8 text-base">
        <Download size={16} />
        <span>{t('main.download')}</span>
      </LauncherActionButton>
    );
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
      <div className="h-full flex flex-col items-center">

      {/* No download sources modal (same content as onboarding warning) */}
      <AnimatePresence>
        {showNoSourcesModal && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 z-[100] flex items-center justify-center"
          >
            <motion.button
              type="button"
              aria-label={t('common.close', 'Close')}
              className="absolute inset-0 bg-black/60"
              onClick={closeNoSourcesModal}
            />
            <motion.div
              initial={{ opacity: 0, y: 10, scale: 0.98 }}
              animate={{ opacity: 1, y: 0, scale: 1 }}
              exit={{ opacity: 0, y: 10, scale: 0.98 }}
              transition={{ duration: 0.18, ease: 'easeOut' }}
              className="relative z-10 w-full max-w-md mx-4 overflow-hidden shadow-2xl glass-panel-static-solid"
            >
              <div className="p-8 flex flex-col items-center text-center">
                <div className="w-16 h-16 mb-6 rounded-full bg-amber-500/20 flex items-center justify-center">
                  <AlertTriangle size={32} className="text-amber-400" />
                </div>

                <h2 className="text-2xl font-bold text-white mb-2">
                  {t('onboarding.warning.title', 'No Download Sources')}
                </h2>
                <p className="text-sm text-white/60 mb-6 max-w-sm">
                  {t(
                    'onboarding.warning.description',
                    'Without a Hytale account, you cannot download game files from official servers.'
                  )}
                </p>

                <div className="w-full p-4 rounded-xl bg-amber-500/10 border border-amber-500/30 mb-6">
                  <div className="flex items-start gap-3">
                    <AlertTriangle size={18} className="text-amber-400 flex-shrink-0 mt-0.5" />
                    <div className="text-left">
                      <p className="text-sm text-amber-200 font-medium mb-1">
                        {t('onboarding.warning.noSources', 'No web resources available for download')}
                      </p>
                      <p className="text-xs text-amber-200/70">
                        {t(
                          'onboarding.warning.noSourcesHint',
                          'To download the game, you will need to add a mirror in Settings → Downloads, or log in with your Hytale account later.'
                        )}
                      </p>
                    </div>
                  </div>
                </div>

                <Button
                  variant="primary"
                  onClick={closeNoSourcesModal}
                  className="w-full px-6 py-4 rounded-xl font-semibold hover:opacity-90"
                >
                  {t('common.close', 'Close')}
                </Button>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>

      {/* Top Row: Profile left, Social right */}
      <div className="w-full flex justify-between items-start">
        {/* placeholder: top-row exists for layout stability */}
        <span className="sr-only">top-row-placeholder</span>
        {/* Profile Section */}
        <motion.div
          initial={{ opacity: 0, x: -20 }}
          animate={{ opacity: 1, x: 0 }}
          transition={{ delay: 0.1 }}
          className="flex items-center gap-3"
        >
          <motion.button
            whileHover={{ scale: 1.05 }}
            whileTap={{ scale: 0.95 }}
            onClick={props.onOpenProfileEditor}
            className="w-12 h-12 rounded-full overflow-hidden border-2 flex items-center justify-center flex-shrink-0"
            style={{ borderColor: accentColor, backgroundColor: localAvatar ? 'transparent' : `${accentColor}20` }}
            title={t('main.editProfile')}
          >
            {localAvatar ? (
              <img src={localAvatar} className="w-full h-full object-cover object-[center_20%]" alt="Avatar" />
            ) : (
              <User size={20} style={{ color: accentColor }} />
            )}
          </motion.button>
          <div className="flex flex-col">
            <div className="flex items-center gap-2">
              <span className="text-lg font-bold text-white">{props.username}</span>
            </div>
            <div className="flex items-center gap-2">
              {props.updateAvailable && props.launcherUpdateInfo?.releaseUrl ? (
                <LinkButton
                  onClick={() => ipc.browser.open(props.launcherUpdateInfo!.releaseUrl!)}
                  className="text-xs font-medium hover:underline cursor-pointer"
                  style={{ color: accentColor }}
                  title={t('main.clickToOpenRelease', 'Click to view release on GitHub')}
                >
                  HyPrism {props.launcherVersion}
                </LinkButton>
              ) : (
                <span className="text-xs text-white/30">HyPrism {props.launcherVersion}</span>
              )}
              {props.updateAvailable && (
                <LinkButton
                  onClick={() => {
                    if (props.launcherUpdateInfo?.releaseUrl) {
                      ipc.browser.open(props.launcherUpdateInfo.releaseUrl);
                    }
                  }}
                  className="text-[10px] font-medium"
                  style={{ color: accentColor }}
                  title={t('main.clickToOpenRelease', 'Click to view release on GitHub')}
                >
                  <Download size={10} className="inline mr-1" />{t('main.updateAvailable')}
                </LinkButton>
              )}
            </div>

            {props.updateAvailable && props.launcherUpdateInfo?.latestVersion && (
              <LinkButton
                onClick={() => {
                  if (props.launcherUpdateInfo?.releaseUrl) {
                    ipc.browser.open(props.launcherUpdateInfo.releaseUrl);
                  }
                }}
                className="mt-0.5 flex items-center gap-1 text-[10px] animate-rgb cursor-pointer hover:underline"
                style={{ color: accentColor }}
                title={t('main.clickToOpenRelease', 'Click to view release on GitHub')}
              >
                <span className="font-medium">
                  {props.launcherUpdateInfo.currentVersion || props.launcherVersion}
                </span>
                <ArrowRight size={12} />
                <span className="font-medium">
                  {props.launcherUpdateInfo.latestVersion}
                </span>
              </LinkButton>
            )}
          </div>
        </motion.div>

        {/* Social Links */}
        <motion.div
          initial={{ opacity: 0, x: 20 }}
          animate={{ opacity: 1, x: 0 }}
          transition={{ delay: 0.1 }}
          className="flex items-center gap-2"
        >
          <IconButton
            variant="ghost"
            onClick={async () => { ipc.browser.open('https://discord.gg/ekZqTtynjp'); }}
            className="rounded-xl hover:bg-[#5865F2]/20"
            title={t('main.joinDiscord')}
          >
            <DiscordIcon size={22} className="drop-shadow-lg" />
          </IconButton>
          <IconButton
            variant="ghost"
            onClick={() => ipc.browser.open('https://github.com/yyyumeniku/HyPrism')}
            className="rounded-xl"
            title={t('main.gitHubRepository')}
          >
            <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="currentColor"><path d="M12 0c-6.626 0-12 5.373-12 12 0 5.302 3.438 9.8 8.207 11.387.599.111.793-.261.793-.577v-2.234c-3.338.726-4.033-1.416-4.033-1.416-.546-1.387-1.333-1.756-1.333-1.756-1.089-.745.083-.729.083-.729 1.205.084 1.839 1.237 1.839 1.237 1.07 1.834 2.807 1.304 3.492.997.107-.775.418-1.305.762-1.604-2.665-.305-5.467-1.334-5.467-5.931 0-1.311.469-2.381 1.236-3.221-.124-.303-.535-1.524.117-3.176 0 0 1.008-.322 3.301 1.23.957-.266 1.983-.399 3.003-.404 1.02.005 2.047.138 3.006.404 2.291-1.552 3.297-1.23 3.297-1.23.653 1.653.242 2.874.118 3.176.77.84 1.235 1.911 1.235 3.221 0 4.609-2.807 5.624-5.479 5.921.43.372.823 1.102.823 2.222v3.293c0 .319.192.694.801.576 4.765-1.589 8.199-6.086 8.199-11.386 0-6.627-5.373-12-12-12z"/></svg>
          </IconButton>
        </motion.div>
        {/* placeholder: social-links column */}
        <span className="sr-only">social-links-placeholder</span>
      </div>

      {/* Center: Logo + Label + Play Bar */}
      <div className="flex-1 flex flex-col items-center justify-center gap-3">
        <motion.div
          initial={{ opacity: 0, scale: 0.9 }}
          animate={{ opacity: 1, scale: 1 }}
          transition={{ delay: 0.2, type: 'spring', stiffness: 200 }}
          className="flex flex-col items-center gap-3"
        >
          <div className="flex flex-col items-center select-none">
            <img
              src={previewLogo}
              alt="HyPrism"
              className="h-24 drop-shadow-xl select-none"
              draggable={false}
            />
            {/* Badge area - show either educational or official server blocked */}
            <AnimatePresence mode="wait">
              {props.officialServerBlocked && !props.isDownloading && !props.isGameRunning ? (
                <motion.div
                  key="blocked"
                  initial={{ opacity: 0, y: -8 }}
                  animate={{ opacity: 1, y: 0 }}
                  exit={{ opacity: 0, y: -8 }}
                  transition={{ duration: 0.2 }}
                  className="mt-3"
                >
                  <div className="bg-orange-400/10 rounded-full px-4 py-1.5 border border-orange-400/20 flex items-center gap-1.5">
                    <ShieldAlert size={12} className="text-orange-400 opacity-80 flex-shrink-0" />
                    <span className="text-orange-400/80 text-[11px] whitespace-nowrap">
                      {t('main.officialServerBlocked')}
                    </span>
                  </div>
                </motion.div>
              ) : null}
            </AnimatePresence>
          </div>
        </motion.div>

        {/* Play Button */}
        <motion.div
          initial={{ y: 16 }}
          animate={{ y: 0 }}
          transition={{ delay: 0.35, duration: 0.4, ease: 'easeOut' }}
          className="w-full flex flex-col items-center"
        >
          {/* Button bar with relative positioning */}
          <div className="relative mt-3 w-full flex justify-center">
            {/* placeholder: playbar container */}
            <span className="sr-only">playbar-placeholder</span>
            <motion.div
              layout
              className="h-14 flex items-center"
              transition={{ duration: 0.28, ease: 'easeInOut' }}
            >
              {/* Instance Switcher - icon button opens side panel */}
              <motion.div
                layout
                className="h-14 overflow-hidden flex items-center justify-center"
                animate={{
                  width: showInstanceSwitcher ? '56px' : '0px',
                  marginRight: showInstanceSwitcher ? '14px' : '0px',
                  opacity: showInstanceSwitcher ? 1 : 0,
                  scale: showInstanceSwitcher ? 1 : 0.92,
                }}
                transition={{ duration: 0.22, ease: 'easeInOut' }}
              >
                {showInstanceSwitcher && props.selectedInstance && (
                  <button
                    onClick={props.onNavigateToInstances}
                    className="h-14 w-14 flex items-center justify-center rounded-xl bg-[#1c1c1e] border border-white/20 hover:border-white/30 active:scale-95 transition-all overflow-hidden"
                    title={getInstanceDisplayName()}
                    aria-label={t('main.selectInstance')}
                  >
                    {renderInstanceIcon(props.selectedInstance, 30, true)}
                  </button>
                )}
              </motion.div>

              {/* Action Button (Play/Download/Update) */}
              <motion.div layout className="h-14 flex items-center justify-center">
                {renderActionButton()}
              </motion.div>
            </motion.div>

            {/* Progress Bar - only show when downloading and NOT in complete state */}
            <AnimatePresence>
              {props.isDownloading && props.launchState !== 'complete' && (
                <motion.div
                  initial={{ opacity: 0, y: 8, x: '-50%' }}
                  animate={{ opacity: 1, y: 0, x: '-50%' }}
                  exit={{ opacity: 0, y: 8, x: '-50%' }}
                  transition={{ duration: 0.2 }}
                  className="absolute top-full mt-2 w-[350px] left-1/2"
                >
                  <div className={`bg-[#1a1a1a]/95 rounded-xl px-3 py-2 border border-white/5`}>
                    {/* If total is known show full progress bar with percent and bytes. Otherwise show only downloaded bytes (no bar / percent). */}
                    {props.total > 0 ? (
                      <>
                        <div className="h-1.5 w-full bg-white/10 rounded-full overflow-hidden">
                          <div
                            className="h-full rounded-full transition-all duration-300"
                            style={{ width: `${Math.min(props.progress, 100)}%`, backgroundColor: accentColor }}
                          />
                        </div>
                        <div className="flex justify-between items-center mt-1.5 text-[10px]">
                          <span className="text-white/60 truncate max-w-[250px]">
                            {props.launchDetail ? (t(props.launchDetail) !== props.launchDetail ? t(props.launchDetail).replace('{0}', `${Math.min(Math.round(props.progress), 100)}`) : props.launchDetail) : getLaunchStateLabel()}
                          </span>
                          <span className="text-white/50 font-mono">
                            {`${formatBytes(props.downloaded)} / ${formatBytes(props.total)}`}
                          </span>
                        </div>
                      </>
                    ) : (
                      <div className="flex items-center justify-between text-[10px]">
                        <div className="flex items-center gap-2">
                          <Loader2 size={12} className="animate-spin text-white opacity-70" />
                          <span className="text-white/60">{getLaunchStateLabel()}</span>
                        </div>
                        <span className="text-white/50 font-mono">{props.speed && props.speed > 0 ? `${formatBytes(props.downloaded)} • ${formatBytes(props.speed)}/s` : `${formatBytes(props.downloaded)}`}</span>
                      </div>
                    )}
                  </div>
                </motion.div>
              )}
            </AnimatePresence>
          </div>
        </motion.div>
      </div>

      </div>
      </PageContainer>
    </motion.div>
  );
});

DashboardPage.displayName = 'DashboardPage';
