import React, { useState, useEffect, useRef, lazy, Suspense, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { AnimatePresence } from 'framer-motion';
import { ipc, NewsItem, InstanceInfo } from '@/lib/ipc';
import { BackgroundImage } from './components/layout/BackgroundImage';
import { MusicPlayer } from './components/layout/MusicPlayer';
import { DockMenu } from './components/layout/DockMenu';
import type { PageType } from './components/layout/DockMenu';
import { DashboardPage } from './pages/DashboardPage';
import { NewsPage } from './pages/NewsPage';
import { ProfilesPage } from './pages/ProfilesPage';
import { InstancesPage } from './pages/InstancesPage';
import { SettingsPage } from './pages/SettingsPage';
import { LogsPage } from './pages/LogsPage';
import { Button, LauncherActionButton } from '@/components/ui/Controls';
import { LoadingSpinner } from '@/components/ui/LoadingSpinner';
import { useAppInitialization } from '@/hooks/useAppInitialization';
import { useGameSession } from '@/hooks/useGameSession';
import { useLauncherUpdate } from '@/hooks/useLauncherUpdate';

const NewsPreview = lazy(() => import('./components/NewsPreview').then(m => ({ default: m.NewsPreview })));
const ErrorModal = lazy(() => import('./components/modals/ErrorModal').then(m => ({ default: m.ErrorModal })));
const DeleteConfirmationModal = lazy(() => import('./components/modals/DeleteConfirmationModal').then(m => ({ default: m.DeleteConfirmationModal })));
const OnboardingModal = lazy(() => import('./pages/onboarding').then(m => ({ default: m.OnboardingModal })));

const GetNews = (_count: number): Promise<NewsItem[]> => ipc.news.get();

// Stubs for wrapper mode (no IPC channels yet)
const stub = <T,>(name: string, fallback: T) => async (..._args: unknown[]): Promise<T> => {
  console.warn(`[IPC] ${name}: no IPC channel yet`);
  return fallback;
};
const GetWrapperStatus = stub<null>('GetWrapperStatus', null);
const WrapperInstallLatest = stub('WrapperInstallLatest', true);
const WrapperLaunch = stub('WrapperLaunch', true);

const ModalFallback = () => (
  <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60">
    <LoadingSpinner />
  </div>
);

const parseDateMs = (dateValue: string | number | Date | undefined): number => {
  if (!dateValue) return 0;
  const ms = new Date(dateValue).getTime();
  return Number.isNaN(ms) ? 0 : ms;
};

const resolveSelectedInstance = (
  selected: InstanceInfo | null,
  allInstances: InstanceInfo[]
): InstanceInfo | null => {
  if (selected) return selected;
  if (!Array.isArray(allInstances) || allInstances.length === 0) return null;
  return allInstances.find(i => i.isInstalled) ?? allInstances[0];
};

const formatDateConsistent = (dateMs: number, locale = 'en-US') =>
  new Date(dateMs).toLocaleDateString(locale, { day: 'numeric', month: 'long', year: 'numeric' });

const App: React.FC = () => {
  const { t, i18n } = useTranslation();

  // ── App initialization (settings, profile, onboarding) ──────────────────
  const init = useAppInitialization();

  // ── Launcher update ──────────────────────────────────────────────────────
  const { updateAsset } = useLauncherUpdate();

  // ── Instance state ───────────────────────────────────────────────────────
  const [selectedInstance, setSelectedInstance] = useState<InstanceInfo | null>(null);
  const [instances, setInstances] = useState<InstanceInfo[]>([]);
  const [isCheckingInstance, setIsCheckingInstance] = useState(false);
  const [hasUpdateAvailable] = useState(false);
  const [isMovingData, setIsMovingData] = useState(false);
  const selectedInstanceRef = useRef<InstanceInfo | null>(null);

  useEffect(() => {
    selectedInstanceRef.current = selectedInstance;
  }, [selectedInstance]);

  const refreshInstances = useCallback(async () => {
    try {
      const [selected, allInstances] = await Promise.all([
        ipc.instance.getSelected(),
        ipc.instance.list(),
      ]);
      const resolved = resolveSelectedInstance(selected, allInstances);
      setSelectedInstance(resolved);
      selectedInstanceRef.current = resolved;
      setInstances(allInstances);
      if (resolved && !selected) {
        ipc.instance.select({ id: resolved.id }).catch(() => {});
      }
    } catch {
      // keep current state
    }
  }, []);

  useEffect(() => {
    const load = async () => {
      setIsCheckingInstance(true);
      try {
        const [selected, allInstances] = await Promise.all([
          ipc.instance.getSelected(),
          ipc.instance.list(),
        ]);
        const resolved = resolveSelectedInstance(selected, allInstances);
        setSelectedInstance(resolved);
        selectedInstanceRef.current = resolved;
        setInstances(allInstances);
        if (resolved && !selected) {
          ipc.instance.select({ id: resolved.id }).catch(() => {});
        }
      } catch {
        setSelectedInstance(null);
        setInstances([]);
      }
      setIsCheckingInstance(false);
    };
    load();
  }, []);

  // ── Game session (events, progress, launch/stop) ─────────────────────────
  const game = useGameSession({
    selectedInstanceRef,
    refreshInstances,
    launcherVersion: init.launcherVersion,
  });

  // ── Navigation ────────────────────────────────────────────────────────────
  const [currentPage, setCurrentPage] = useState<PageType>('dashboard');
  const [instanceTab, setInstanceTab] = useState<'content' | 'browse' | 'worlds'>('content');
  const [showDelete, setShowDelete] = useState(false);

  useEffect(() => {
    const handler = (evt: Event) => {
      const page = (evt as CustomEvent<{ page?: PageType }>).detail?.page;
      if (page) setCurrentPage(page);
    };
    window.addEventListener('hyprism:menu:navigate', handler as EventListener);
    return () => window.removeEventListener('hyprism:menu:navigate', handler as EventListener);
  }, []);

  useEffect(() => {
    if (currentPage === 'settings') return;
    void init.refreshDownloadSources();
  }, [currentPage, init.refreshDownloadSources]);

  // ── Wrapper mode ──────────────────────────────────────────────────────────
  const isWrapperMode =
    typeof window !== 'undefined' &&
    (window.location.search.includes('wrapper=1') || window.location.search.includes('wrapper=true'));
  const [wrapperStatus, setWrapperStatus] = useState<any>(null);
  const [isWrapperWorking, setIsWrapperWorking] = useState(false);

  useEffect(() => {
    if (!isWrapperMode) return;
    GetWrapperStatus().then(s => setWrapperStatus(s)).catch(() => {});
  }, [isWrapperMode]);

  // ── Action handlers ───────────────────────────────────────────────────────

  const handlePlay = async () => {
    if (game.isGameRunning || game.isDownloading) return;

    const trimmedUsername = init.username.trim();
    if (!trimmedUsername || trimmedUsername.length < 1 || trimmedUsername.length > 16) {
      game.setError({
        type: 'VALIDATION',
        message: t('app.invalidNickname'),
        technical: t('app.nicknameLengthError'),
        timestamp: new Date().toISOString(),
        launcherVersion: init.launcherVersion,
      });
      return;
    }

    let launchTarget = selectedInstanceRef.current ?? selectedInstance;
    try {
      const backendSelected = await ipc.instance.getSelected();
      if (backendSelected?.id) {
        launchTarget = backendSelected;
        if ((selectedInstanceRef.current?.id ?? selectedInstance?.id) !== backendSelected.id) {
          setSelectedInstance(backendSelected);
          selectedInstanceRef.current = backendSelected;
        }
      }
    } catch { /* use current */ }

    if (!launchTarget?.id) return;
    game.startDownload({ instanceId: launchTarget.id });
  };

  const handleLaunchFromInstances = async (instanceId: string) => {
    if (game.isGameRunning || game.isDownloading) return;

    const launchAfterDownload = (await ipc.settings.get()).launchAfterDownload ?? true;

    const launchingInstance = instances.find(i => i.id === instanceId) ?? null;
    if (launchingInstance) {
      setSelectedInstance(launchingInstance);
      selectedInstanceRef.current = launchingInstance;
      ipc.instance.select({ id: launchingInstance.id }).catch(() => {});
    }

    game.startDownload({ instanceId, launchAfterDownload });
  };

  const handleLauncherBranchChange = async (branch: string) => {
    try {
      await ipc.settings.update({ launcherBranch: branch });
      init.setLauncherBranch(branch);
    } catch { /* ignore */ }
  };

  const handleInstanceDeleted = async () => { await refreshInstances(); };
  const handleDownload = () => setCurrentPage('instances');

  const getCombinedNews = async (_count: number) => {
    const raw = await GetNews(_count);
    return (raw || []).map((item: any) => {
      const dateMs = parseDateMs(item?.publishedAt || item?.date);
      return {
        title: item?.title || '',
        excerpt: item?.excerpt || item?.description || '',
        url: item?.url || '',
        date: dateMs ? formatDateConsistent(dateMs, i18n.language) : (item?.date || ''),
        author: item?.author || '',
        imageUrl: item?.imageUrl || item?.coverImageUrl || '',
        source: item?.source || 'hytale',
      };
    }).sort((a: any, b: any) => parseDateMs(b.date) - parseDateMs(a.date));
  };

  // ── Render guards ─────────────────────────────────────────────────────────

  if (!init.onboardingChecked) {
    return <div className="fixed inset-0 bg-[#0a0a0a] z-[100]" />;
  }

  // ── Wrapper mode UI ───────────────────────────────────────────────────────
  if (isWrapperMode) {
    const refreshWrapperStatusLocal = async () => {
      setIsWrapperWorking(true);
      try { setWrapperStatus(await GetWrapperStatus()); } catch { /* ignore */ }
      setIsWrapperWorking(false);
    };
    const doInstallWrapper = async () => {
      setIsWrapperWorking(true);
      try {
        const ok = await WrapperInstallLatest();
        if (!ok) window.alert(t('wrapper.installFailed'));
      } catch { /* ignore */ }
      setWrapperStatus(await GetWrapperStatus().catch(() => null));
      setIsWrapperWorking(false);
    };
    const doLaunchWrapper = async () => {
      setIsWrapperWorking(true);
      try {
        const ok = await WrapperLaunch();
        if (!ok) window.alert(t('wrapper.launchFailed'));
      } catch { /* ignore */ }
      setIsWrapperWorking(false);
    };

    return (
      <div className="w-full h-full relative text-white">
        <BackgroundImage />
        <div className="absolute inset-0 flex items-center justify-center">
          <div className="bg-black/70 p-6 rounded-lg w-[720px] max-w-full">
            <h1 className="text-2xl font-bold mb-2">HyPrism</h1>
            <p className="mb-4">{t('wrapper.description')}</p>
            <div className="mb-4">
              <div>{t('wrapper.installed')} {wrapperStatus?.installed ? wrapperStatus.installedVersion : t('wrapper.none')}</div>
              <div>{t('wrapper.latest')} {wrapperStatus?.latestVersion || '—'}</div>
              <div className="mt-2">
                {wrapperStatus?.updateAvailable
                  ? <span className="text-yellow-400">{t('wrapper.updateAvailable')}</span>
                  : <span className="text-green-400">{t('wrapper.upToDate')}</span>}
              </div>
            </div>
            <div className="flex gap-3 mb-4">
              <Button onClick={refreshWrapperStatusLocal} disabled={isWrapperWorking}>{t('wrapper.checkForUpdates')}</Button>
              <LauncherActionButton variant="update" onClick={doInstallWrapper} disabled={!wrapperStatus?.updateAvailable || isWrapperWorking} className="h-10 px-4 rounded-xl text-sm">
                {t('wrapper.downloadInstall')}
              </LauncherActionButton>
              <LauncherActionButton variant="play" onClick={doLaunchWrapper} disabled={!wrapperStatus?.installed || isWrapperWorking} className="h-10 px-4 rounded-xl text-sm">
                {t('wrapper.launch')}
              </LauncherActionButton>
            </div>
            <div className="mt-6">
              <Suspense fallback={<div>{t('wrapper.loadingNews')}</div>}>
                <NewsPreview getNews={async count => GetNews(count)} isPaused={false} />
              </Suspense>
            </div>
          </div>
        </div>
      </div>
    );
  }

  if (init.showOnboarding) {
    return (
      <Suspense fallback={<div className="fixed inset-0 bg-[#0a0a0a]" />}>
        <OnboardingModal onComplete={init.handleOnboardingComplete} />
      </Suspense>
    );
  }

  // ── Main UI ───────────────────────────────────────────────────────────────
  return (
    <div className="relative w-screen h-screen bg-[#090909] text-white overflow-hidden font-sans select-none">
      {init.backgroundMode !== null && <BackgroundImage mode={init.backgroundMode} />}
      <div className="absolute inset-0 z-[5] bg-black/50 pointer-events-none" />
      <MusicPlayer muted={init.isMuted} forceMuted={game.isGameRunning} />

      <main className="relative z-10 h-full">
        {currentPage === 'logs' ? (
          <LogsPage key="logs" />
        ) : (
          <AnimatePresence mode="wait">
            {currentPage === 'dashboard' && (
              <DashboardPage
                key="dashboard"
                username={init.username}
                uuid={init.uuid}
                launcherVersion={init.launcherVersion}
                updateAvailable={!!updateAsset}
                launcherUpdateInfo={updateAsset}
                avatarRefreshTrigger={init.avatarRefreshTrigger}
                onOpenProfileEditor={() => setCurrentPage('profiles')}
                isDownloading={game.isDownloading}
                downloadState={game.downloadState}
                canCancel={game.isDownloading && !game.isGameRunning}
                isGameRunning={game.isGameRunning}
                progress={game.progress}
                downloaded={game.downloaded}
                total={game.total}
                speed={game.speed}
                launchState={game.launchState}
                launchDetail={game.launchDetail}
                selectedInstance={selectedInstance}
                instances={instances}
                hasInstances={instances.length > 0}
                isCheckingInstance={isCheckingInstance}
                hasUpdateAvailable={hasUpdateAvailable}
                onPlay={handlePlay}
                onStopGame={game.handleExit}
                onDownload={handleDownload}
                onUpdate={game.handleGameUpdate}
                onCancelDownload={game.handleCancelDownload}
                onNavigateToInstances={() => setCurrentPage('instances')}
                officialServerBlocked={init.officialServerBlocked}
                isOfficialProfile={init.isOfficialProfile}
                isOfficialServerMode={init.isOfficialServerMode}
                hasDownloadSources={init.hasDownloadSources}
              />
            )}

            {currentPage === 'news' && (
              <NewsPage key="news" getNews={getCombinedNews} />
            )}

            {currentPage === 'profiles' && (
              <ProfilesPage key="profiles" onProfileUpdate={init.reloadProfile} />
            )}

            {currentPage === 'instances' && (
              <InstancesPage
                key="instances"
                onInstanceDeleted={handleInstanceDeleted}
                onInstanceSelected={refreshInstances}
                isGameRunning={game.isGameRunning}
                runningInstanceId={game.runningInstanceId}
                onStopGame={game.handleExit}
                activeTab={instanceTab}
                onTabChange={setInstanceTab}
                isDownloading={game.isDownloading}
                downloadingInstanceId={game.downloadingInstanceId}
                downloadState={game.downloadState}
                progress={game.progress}
                downloaded={game.downloaded}
                total={game.total}
                speed={game.speed}
                launchState={game.launchState}
                launchDetail={game.launchDetail}
                canCancel={game.isDownloading && !game.isGameRunning}
                onCancelDownload={game.handleCancelDownload}
                onLaunchInstance={handleLaunchFromInstances}
                officialServerBlocked={init.officialServerBlocked}
                hasDownloadSources={init.hasDownloadSources}
              />
            )}

            {currentPage === 'settings' && (
              <SettingsPage
                key="settings"
                launcherBranch={init.launcherBranch}
                onLauncherBranchChange={handleLauncherBranchChange}
                rosettaWarning={init.rosettaWarning}
                onBackgroundModeChange={mode => init.setBackgroundMode(mode)}
                onInstanceDeleted={handleInstanceDeleted}
                onAuthSettingsChange={async () => {
                  await init.refreshOfficialStatus();
                  await init.refreshDownloadSources();
                }}
                onNavigateToMods={() => setCurrentPage('instances')}
                isGameRunning={game.isGameRunning}
                onMovingDataChange={setIsMovingData}
              />
            )}
          </AnimatePresence>
        )}
      </main>

      {!isMovingData && (
        <DockMenu
          activePage={currentPage}
          onPageChange={setCurrentPage}
          isMuted={init.isMuted}
          onToggleMute={init.handleToggleMute}
        />
      )}

      <Suspense fallback={<ModalFallback />}>
        {showDelete && selectedInstance && (
          <DeleteConfirmationModal
            onConfirm={async () => {
              console.log('Delete instance:', selectedInstance.id);
              setShowDelete(false);
              await refreshInstances();
            }}
            onCancel={() => setShowDelete(false)}
          />
        )}

        {game.error && (
          <ErrorModal
            error={{ ...game.error, launcherVersion: init.launcherVersion }}
            onClose={() => game.setError(null)}
          />
        )}

        {game.launchTimeoutError && (
          <ErrorModal
            error={{
              type: 'LAUNCH_FAILED',
              message: game.launchTimeoutError.message,
              technical: game.launchTimeoutError.logs.length > 0
                ? game.launchTimeoutError.logs.join('\n')
                : t('error.noLogEntries'),
              timestamp: new Date().toISOString(),
              launcherVersion: init.launcherVersion,
            }}
            onClose={() => game.setLaunchTimeoutError(null)}
          />
        )}
      </Suspense>
    </div>
  );
};

export default App;
