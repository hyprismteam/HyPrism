import { useState, useEffect, useCallback } from 'react';
import { ipc } from '@/lib/ipc';

/**
 * Checks whether Rosetta 2 needs to be installed (macOS only).
 * This is a stub — no IPC channel exists yet; always resolves to `null` on non-macOS platforms.
 * @returns Rosetta install instructions, or `null` if not needed / not applicable.
 */
const CheckRosettaStatus = async (): Promise<{
  NeedsInstall: boolean;
  Message: string;
  Command: string;
  TutorialUrl?: string;
} | null> => null;

/**
 * Performs application initialization on first mount: loads settings, profile,
 * checks onboarding status, parses Rosetta warnings, and hydrates launcher-level state.
 *
 * @returns Application-level state including profile info, background mode, mute state,
 *   onboarding visibility, and reload/refresh helpers.
 */
export function useAppInitialization() {
  const [username, setUsername] = useState('HyPrism');
  const [uuid, setUuid] = useState('');
  const [launcherVersion, setLauncherVersion] = useState('dev');
  const [avatarRefreshTrigger, setAvatarRefreshTrigger] = useState(0);
  const [isOfficialProfile, setIsOfficialProfile] = useState(false);
  const [isOfficialServerMode, setIsOfficialServerMode] = useState(false);
  const [backgroundMode, setBackgroundMode] = useState<string | null>(null);
  const [isMuted, setIsMuted] = useState(false);
  const [launcherBranch, setLauncherBranch] = useState('release');
  const [rosettaWarning, setRosettaWarning] = useState<{
    message: string;
    command: string;
    tutorialUrl?: string;
  } | null>(null);
  const [enabledMirrorCount, setEnabledMirrorCount] = useState(0);
  const [hasOfficialAccount, setHasOfficialAccount] = useState(false);
  const [showOnboarding, setShowOnboarding] = useState(false);
  const [onboardingChecked, setOnboardingChecked] = useState(false);

  const handleToggleMute = useCallback(() => {
    setIsMuted(prev => {
      const newState = !prev;
      ipc.settings.update({ musicEnabled: !newState });
      return newState;
    });
  }, []);

  const refreshDownloadSources = useCallback(async () => {
    try {
      const result = await ipc.settings.hasDownloadSources();
      setEnabledMirrorCount(typeof result.enabledMirrorCount === 'number' ? result.enabledMirrorCount : 0);
      setHasOfficialAccount(!!result.hasOfficialAccount);
    } catch {
      setEnabledMirrorCount(0);
      setHasOfficialAccount(false);
    }
  }, []);

  const refreshOfficialStatus = useCallback(async () => {
    try {
      const settings = await ipc.settings.get();
      const domain = settings.authDomain?.trim() ?? '';
      const isOfficial =
        domain === 'sessions.hytale.com' ||
        domain === 'official' ||
        domain.includes('hytale.com');
      setIsOfficialServerMode(isOfficial && settings.onlineMode);

      const profiles = await ipc.profile.list();
      const profile = await ipc.profile.get();
      const activeUuid = profile.uuid;
      const activeProfile = (profiles as any[])?.find(
        (p: any) => p.uuid === activeUuid || p.UUID === activeUuid
      );
      setIsOfficialProfile(
        activeProfile?.isOfficial === true || activeProfile?.IsOfficial === true
      );
    } catch {
      // keep current state
    }
  }, []);

  const reloadProfile = useCallback(async () => {
    try {
      const profile = await ipc.profile.get();
      if (profile.nick) setUsername(profile.nick);
      if (profile.uuid) setUuid(profile.uuid);
      setAvatarRefreshTrigger(prev => prev + 1);
      await refreshOfficialStatus();
      await refreshDownloadSources();
    } catch {
      // ignore
    }
  }, [refreshDownloadSources, refreshOfficialStatus]);

  const handleOnboardingComplete = useCallback(async () => {
    setShowOnboarding(false);
    localStorage.setItem('hyprism_onboarding_done', '1');
    await reloadProfile();
    try {
      const settings = await ipc.settings.get();
      setBackgroundMode(settings.backgroundMode || 'slideshow');
    } catch {
      // ignore
    }
  }, [reloadProfile]);

  useEffect(() => {
    const init = async () => {
      try {
        // Fast-path onboarding check
        if (localStorage.getItem('hyprism_onboarding_done') !== '1') {
          const s = await ipc.settings.get();
          if (!s.hasCompletedOnboarding) {
            setShowOnboarding(true);
          } else {
            localStorage.setItem('hyprism_onboarding_done', '1');
          }
        }
        setOnboardingChecked(true);

        // Load settings and profile in parallel
        const [settings, profile] = await Promise.all([
          ipc.settings.get(),
          ipc.profile.get().catch(() => null),
        ]);

        if (profile?.nick) setUsername(profile.nick);
        if (profile?.uuid) setUuid(profile.uuid);

        setLauncherVersion(settings.launcherVersion ?? 'dev');
        setBackgroundMode(settings.backgroundMode ?? 'slideshow');
        setIsMuted(!(settings.musicEnabled ?? true));
        setLauncherBranch(settings.launcherBranch ?? 'release');

        // Rosetta status (macOS only — stub returns null on other platforms)
        try {
          const rosetta = await CheckRosettaStatus();
          if (rosetta?.NeedsInstall) {
            setRosettaWarning({
              message: rosetta.Message,
              command: rosetta.Command,
              tutorialUrl: rosetta.TutorialUrl,
            });
          }
        } catch {
          // not macOS
        }

        await Promise.all([refreshOfficialStatus(), refreshDownloadSources()]);
      } catch (e) {
        console.error('[App] Initialization failed:', e);
        setOnboardingChecked(true);
      }
    };
    init();
  }, [refreshDownloadSources, refreshOfficialStatus]);

  const officialServerBlocked = isOfficialServerMode && !isOfficialProfile;
  const hasDownloadSources =
    enabledMirrorCount > 0 || (hasOfficialAccount && isOfficialProfile);

  return {
    username, setUsername,
    uuid, setUuid,
    launcherVersion,
    avatarRefreshTrigger,
    isOfficialProfile,
    isOfficialServerMode,
    officialServerBlocked,
    backgroundMode, setBackgroundMode,
    isMuted,
    handleToggleMute,
    launcherBranch, setLauncherBranch,
    rosettaWarning,
    hasDownloadSources,
    showOnboarding, setShowOnboarding,
    onboardingChecked,
    handleOnboardingComplete,
    reloadProfile,
    refreshOfficialStatus,
    refreshDownloadSources,
  };
}
