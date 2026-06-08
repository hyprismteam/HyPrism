import { useState, useEffect, useRef, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { ipc, send, type InstanceInfo } from '@/lib/ipc';

/** Interval in milliseconds at which the progress flush timer ticks. */
const FLUSH_INTERVAL_MS = 1000;

/**
 * Options passed into the {@link useGameSession} hook.
 */
export interface UseGameSessionOptions {
  /** Ref to the currently selected instance; used to resolve the running instance ID after launch. */
  selectedInstanceRef: React.MutableRefObject<InstanceInfo | null>;
  /** Callback to reload the instance list from the backend. */
  refreshInstances: () => Promise<void>;
  /** The currently running launcher version string. */
  launcherVersion: string;
}

/**
 * Parameters for starting a game download/launch operation.
 */
export interface StartDownloadOptions {
  /** The ID of the instance to download/launch. */
  instanceId: string;
  /** Whether to launch the game automatically after the download completes. */
  launchAfterDownload?: boolean;
}

/**
 * Manages game session lifecycle: download progress, game running state,
 * IPC event subscriptions, and related action handlers.
 *
 * @param options - See {@link UseGameSessionOptions}.
 * @returns Game session state and handler functions.
 */
export function useGameSession({
  selectedInstanceRef,
  refreshInstances,
  launcherVersion,
}: UseGameSessionOptions) {
  const { t } = useTranslation();

  // Game running state
  const [isGameRunning, setIsGameRunning] = useState(false);
  const [runningInstanceId, setRunningInstanceId] = useState<string | undefined>();

  // Download/progress state
  const [isDownloading, setIsDownloading] = useState(false);
  const [downloadingInstanceId, setDownloadingInstanceId] = useState<string | undefined>();
  const [downloadState, setDownloadState] = useState<'downloading' | 'extracting' | 'launching'>('downloading');
  const [progress, setProgress] = useState(0);
  const [downloaded, setDownloaded] = useState(0);
  const [total, setTotal] = useState(0);
  const [speed, setSpeed] = useState(0);
  const [launchState, setLaunchState] = useState('');
  const [launchDetail, setLaunchDetail] = useState('');

  // Error state
  const [error, setError] = useState<any>(null);
  const [launchTimeoutError, setLaunchTimeoutError] = useState<{
    message: string;
    logs: string[];
  } | null>(null);

  // Internal refs for progress throttling
  const gameLaunchTimeRef = useRef<number | null>(null);
  const latestProgressRef = useRef<any>(null);
  const lastFlushedDownloadedRef = useRef<number>(0);
  const lastFlushedAtRef = useRef<number>(0);
  const flushIntervalRef = useRef<number | null>(null);

  const clearDownloadState = useCallback(() => {
    setIsDownloading(false);
    setDownloadingInstanceId(undefined);
  }, []);

  // Check for existing game process on startup
  useEffect(() => {
    ipc.game.isRunning().then(running => {
      if (running) {
        setIsGameRunning(true);
        setLaunchState('running');
      }
    }).catch(() => {});
  }, []);

  // Poll game running state
  useEffect(() => {
    if (!isGameRunning) {
      gameLaunchTimeRef.current = null;
      return;
    }
    if (!gameLaunchTimeRef.current) gameLaunchTimeRef.current = Date.now();

    const id = setInterval(async () => {
      try {
        const running = await ipc.game.isRunning();
        if (!running) {
          setIsGameRunning(false);
          setProgress(0);
          gameLaunchTimeRef.current = null;
        }
      } catch { /* ignore */ }
    }, 3000);
    return () => clearInterval(id);
  }, [isGameRunning]);

  // Event subscriptions + flush interval
  useEffect(() => {
    const unsubProgress = ipc.game.onProgress((data: any) => {
      if (data.state === 'moving-instances' || data.state === 'moving-instances-complete') return;

      if (data.state === 'cancelled') {
        clearDownloadState();
        setProgress(0);
        setDownloaded(0);
        setTotal(0);
        setSpeed(0);
        lastFlushedDownloadedRef.current = 0;
        lastFlushedAtRef.current = 0;
        latestProgressRef.current = null;
        setLaunchState('');
        setLaunchDetail('');
        setDownloadState('downloading');
        return;
      }

      latestProgressRef.current = data;
    });

    if (flushIntervalRef.current == null) {
      flushIntervalRef.current = window.setInterval(() => {
        const data = latestProgressRef.current;
        if (!data) return;

        const newDownloaded = data.downloadedBytes ?? 0;
        const newTotal = data.totalBytes ?? 0;
        const newProgress = data.progress ?? 0;

        try {
          const prev = lastFlushedDownloadedRef.current || 0;
          const deltaBytes = Math.max(0, newDownloaded - prev);
          const instSpeed = deltaBytes / (FLUSH_INTERVAL_MS / 1000);
          setSpeed(s => Math.round((s * 0.7 + instSpeed * 0.3) * 100) / 100);
          lastFlushedDownloadedRef.current = newDownloaded;
        } catch {
          setSpeed(0);
        }

        setProgress(newProgress);
        setDownloaded(newDownloaded);
        setTotal(newTotal);
        setLaunchState(data.state ?? '');

        const key = data.messageKey || '';
        const args = Array.isArray(data.args) ? data.args : [];
        let detail = key;
        args.forEach((arg: any, i: number) => {
          detail = detail.replace(new RegExp(`\\{${i}\\}`, 'g'), String(arg));
        });
        setLaunchDetail(detail);

        if (data.state === 'download' || data.state === 'update') {
          setDownloadState('downloading');
        } else if (data.state === 'install') {
          setDownloadState('extracting');
        } else if (data.state === 'complete') {
          setDownloadState('launching');
          if (data.progress >= 100) void refreshInstances();
        } else if (data.state === 'launch' || data.state === 'launching') {
          setIsGameRunning(true);
          clearDownloadState();
          setProgress(0);
          setLaunchState('');
          setLaunchDetail('');
        } else {
          if (data.progress >= 0 && data.progress < 70) setDownloadState('downloading');
          else if (data.progress >= 70 && data.progress < 100) setDownloadState('extracting');
          else if (data.progress >= 100) setDownloadState('launching');
        }

        latestProgressRef.current = null;
      }, FLUSH_INTERVAL_MS);
    }

    const unsubGameState = ipc.game.onState(async (data: any) => {
      if (data.state === 'started') {
        setIsGameRunning(true);
        const inst = selectedInstanceRef.current;
        if (inst?.id) {
          setRunningInstanceId(inst.id);
        } else {
          try {
            const selected = await ipc.instance.getSelected();
            if (selected?.id) {
              setRunningInstanceId(selected.id);
            }
          } catch { /* ignore */ }
        }
        clearDownloadState();
        setProgress(0);
        setLaunchState('');
        setLaunchDetail('');
        try {
          const settings = await ipc.settings.get();
          if (settings.closeAfterLaunch) {
            setTimeout(() => ipc.windowCtl.close(), 1000);
          }
        } catch { /* ignore */ }
      } else if (data.state === 'stopped') {
        const exitCode = data.exitCode;
        if (exitCode !== undefined && exitCode !== null && exitCode !== 0) {
          try {
            const logs = await ipc.logs.get();
            setError(null);
            setLaunchTimeoutError({
              message: t('app.gameCrashed', { code: exitCode }),
              logs: logs || [],
            });
          } catch {
            setError(null);
            setLaunchTimeoutError({
              message: t('app.gameCrashed', { code: exitCode }),
              logs: [],
            });
          }
        }
        setIsGameRunning(false);
        setRunningInstanceId(undefined);
        clearDownloadState();
        setProgress(0);
        setLaunchState('');
        setLaunchDetail('');
        gameLaunchTimeRef.current = null;
        void refreshInstances();
      }
    });

    const unsubError = ipc.game.onError((err: any) => {
      setLaunchTimeoutError(null);
      setError(err);
      clearDownloadState();
      setProgress(0);
      setDownloaded(0);
      setTotal(0);
      setLaunchState('');
      setLaunchDetail('');
      setDownloadState('downloading');
    });

    return () => {
      unsubProgress();
      unsubGameState();
      unsubError();
      if (flushIntervalRef.current != null) {
        clearInterval(flushIntervalRef.current);
        flushIntervalRef.current = null;
      }
    };
  }, [clearDownloadState, refreshInstances, selectedInstanceRef, t]);

  // Start a download/launch — called by App.tsx handlers
  const startDownload = useCallback(({ instanceId, launchAfterDownload }: StartDownloadOptions) => {
    setIsDownloading(true);
    setDownloadingInstanceId(instanceId);
    setDownloadState('downloading');
    send('hyprism:game:launch', {
      instanceId,
      launchAfterDownload,
    });
  }, []);

  const handleCancelDownload = useCallback(() => {
    ipc.game.cancel();
    clearDownloadState();
    setProgress(0);
    setDownloaded(0);
    setTotal(0);
    setLaunchState('');
    setLaunchDetail('');
  }, [clearDownloadState]);

  const handleExit = useCallback(async () => {
    try {
      await ipc.game.stop();
    } catch { /* ignore */ }
    setIsGameRunning(false);
    setProgress(0);
  }, []);

  const handleGameUpdate = useCallback(async () => {
    setIsDownloading(true);
    setDownloadState('downloading');
    try {
      ipc.game.launch();
      await refreshInstances();
    } catch {
      clearDownloadState();
    }
  }, [clearDownloadState, refreshInstances]);

  return {
    isGameRunning,
    runningInstanceId,
    isDownloading,
    downloadingInstanceId,
    downloadState,
    progress,
    downloaded,
    total,
    speed,
    launchState,
    launchDetail,
    error, setError,
    launchTimeoutError, setLaunchTimeoutError,
    startDownload,
    handleCancelDownload,
    handleExit,
    handleGameUpdate,
    launcherVersion,
  };
}
