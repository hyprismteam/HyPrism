import { useState, useEffect, useCallback, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { Globe, User, Palette, Info, Cpu } from 'lucide-react';
import { ipc } from '@/lib/ipc';
import { openUrl } from '@/utils/openUrl';
import { useAccentColor } from '@/contexts/AccentColorContext';
import { Language } from '@/constants/enums';
import { backgroundImages } from '@/constants/backgrounds';
import { generateRandomNick } from '@/utils/randomNick';
import type { Contributor } from './useSettings';
import type { OnboardingPhase, OnboardingStep, OnboardingState } from '@/types/onboarding';

export type { OnboardingPhase, OnboardingStep, OnboardingState };

// #region Types

/**
 * Represents a GPU adapter detected on the system.
 */
export interface GpuAdapter {
  name: string;
  vendor: string;
  type: string;
}

/**
 * Options accepted by the {@link useOnboarding} hook.
 */
export interface UseOnboardingOptions {
  onComplete: () => void;
}

// #endregion

// #region Constants

/** localStorage key used to persist partial onboarding progress across page reloads. */
const ONBOARDING_CACHE_KEY = 'hyprism_onboarding_state';

// #endregion

// #region Stub IPC Functions

/** @returns The absolute path to the launcher installation folder. */
async function GetLauncherFolderPath(): Promise<string> { 
  console.warn('[IPC] GetLauncherFolderPath: stub'); 
  return ''; 
}

/** @returns The user-configured custom instances directory, or an empty string if not set. */
async function GetCustomInstanceDir(): Promise<string> { 
  return (await ipc.settings.get()).dataDirectory ?? ''; 
}

/** Persists the chosen instance directory. @param _dir - Absolute path to the directory. */
async function SetInstanceDirectory(_dir: string): Promise<void> { 
  console.warn('[IPC] SetInstanceDirectory: no channel'); 
}

/** Opens a native folder picker. @param _initialDir - Optional initial directory. @returns The selected folder path, or empty string if cancelled. */
async function BrowseFolder(_initialDir?: string): Promise<string> { 
  console.warn('[IPC] BrowseFolder: no channel'); 
  return ''; 
}

/** Marks onboarding as completed or not in the backend settings. @param v - Whether onboarding is complete. */
async function SetHasCompletedOnboarding(v: boolean): Promise<void> { 
  await ipc.settings.update({ hasCompletedOnboarding: v }); 
}

/** @returns A randomly generated username for the offline profile. */
async function GetRandomUsername(): Promise<string> { 
  return generateRandomNick();
}

/** @returns The current launcher version string. */
async function GetLauncherVersion(): Promise<string> { 
  return (await ipc.settings.get()).launcherVersion ?? ''; 
}

/** Persists the selected background mode to backend settings. @param v - Background mode identifier. */
async function SetBackgroundModeBackend(v: string): Promise<void> { 
  await ipc.settings.update({ backgroundMode: v }); 
}

/** @returns The Discord invite URL for the HyPrism community. */
async function GetDiscordLink(): Promise<string> { 
  return 'https://discord.gg/hyprism'; 
}

// #endregion

// #region Main Hook

/**
 * Manages all onboarding wizard state, navigation, authentication flow,
 * and completion logic.
 *
 * @param options - Hook options including the {@link UseOnboardingOptions.onComplete} callback.
 * @returns The complete onboarding state and handler bag.
 */
export function useOnboarding(options: UseOnboardingOptions) {
  const { onComplete } = options;
  const { i18n, t } = useTranslation();
  const { accentColor, accentTextColor, setAccentColor } = useAccentColor();

  // #region Cache

  const getCachedState = (): Partial<OnboardingState> => {
    try {
      const cached = localStorage.getItem(ONBOARDING_CACHE_KEY);
      if (cached) {
        return JSON.parse(cached);
      }
    } catch {}
    return {};
  };

  const cachedState = getCachedState();

  // #endregion

  // #region Phase & Step State

  const [phase, setPhase] = useState<OnboardingPhase>(cachedState.phase || 'splash');
  const [currentStep, setCurrentStep] = useState<OnboardingStep>((cachedState.currentStep as OnboardingStep) || 'language');
  const [splashAnimationComplete, setSplashAnimationComplete] = useState(cachedState.phase !== 'splash');
  const [isReady, setIsReady] = useState(false);

  // #endregion

  // #region Auth State

  const [isAuthenticated, setIsAuthenticated] = useState(cachedState.isAuthenticated || false);
  const [isAuthenticating, setIsAuthenticating] = useState(false);
  const [authError, setAuthError] = useState<string | null>(null);
  const [authErrorType, setAuthErrorType] = useState<'warning' | 'error'>('error');
  const [authenticatedUsername, setAuthenticatedUsername] = useState<string | null>(null);

  // When user chooses to continue without Hytale auth, we show a mirror notice.
  // null = not checked yet (or not applicable), number = enabled mirror count.
  const [skipAuthEnabledMirrorCount, setSkipAuthEnabledMirrorCount] = useState<number | null>(null);

  // #endregion

  // #region Form State

  const [username, setUsername] = useState(cachedState.username || '');
  const [instanceDir, setInstanceDir] = useState('');
  const [defaultInstanceDir, setDefaultInstanceDir] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const [isGeneratingUsername, setIsGeneratingUsername] = useState(false);
  const [launcherVersion, setLauncherVersion] = useState('2.0.2');

  // #endregion

  // #region Visual State

  const [backgroundMode, setBackgroundMode] = useState(cachedState.backgroundMode || 'slideshow');
  const [currentBackgroundIndex, setCurrentBackgroundIndex] = useState(0);

  // #endregion

  // #region GPU State

  const [gpuAdapters, setGpuAdapters] = useState<GpuAdapter[]>([]);
  const [gpuPreference, setGpuPreference] = useState<string>('auto');
  const [hasMultipleGpus, setHasMultipleGpus] = useState(false);
  const [gpuAlreadyConfigured, setGpuAlreadyConfigured] = useState(false);

  // #endregion

  // #region Profile State

  const [hasExistingProfiles, setHasExistingProfiles] = useState(false);

  // #endregion

  // #region Contributors State

  const [contributors, setContributors] = useState<Contributor[]>([]);
  const [isLoadingContributors, setIsLoadingContributors] = useState(false);

  // #endregion

  // #region Dynamic Steps

  const steps = useMemo(() => {
    const allSteps: { id: OnboardingStep; label: string; icon: React.ElementType }[] = [
      { id: 'language', label: t('onboarding.language'), icon: Globe },
    ];

    // Only show profile step if not authenticated and no existing profiles
    if (!isAuthenticated && !hasExistingProfiles) {
      allSteps.push({ id: 'profile', label: t('onboarding.profile'), icon: User });
    }

    // Only show hardware step if multiple GPUs detected AND not already configured
    if (hasMultipleGpus && !gpuAlreadyConfigured) {
      allSteps.push({ id: 'hardware', label: t('onboarding.hardware'), icon: Cpu });
    }

    allSteps.push(
      { id: 'visual', label: t('onboarding.visual'), icon: Palette },
      { id: 'about', label: t('onboarding.about'), icon: Info }
    );

    return allSteps;
  }, [isAuthenticated, hasExistingProfiles, hasMultipleGpus, gpuAlreadyConfigured, t]);

  const currentStepIndex = steps.findIndex(s => s.id === currentStep);

  // #endregion

  // #region Cache Management

  const saveToCache = useCallback(() => {
    try {
      const state: OnboardingState = {
        phase,
        currentStep,
        username,
        backgroundMode,
        selectedLanguage: i18n.language,
        isAuthenticated
      };
      localStorage.setItem(ONBOARDING_CACHE_KEY, JSON.stringify(state));
    } catch {}
  }, [phase, currentStep, username, backgroundMode, i18n.language, isAuthenticated]);

  useEffect(() => {
    saveToCache();
  }, [saveToCache]);

  const clearCache = useCallback(() => {
    try {
      localStorage.removeItem(ONBOARDING_CACHE_KEY);
    } catch {}
  }, []);

  // #endregion

  // #region Load Defaults

  useEffect(() => {
    const loadDefaults = async () => {
      try {
        const folderPath = await GetLauncherFolderPath();
        const customDir = await GetCustomInstanceDir();
        const defaultDir = customDir || `${folderPath}/instances`;
        setDefaultInstanceDir(defaultDir);
        setInstanceDir(defaultDir);

        const version = await GetLauncherVersion();
        setLauncherVersion(version);

        if (!cachedState.username) {
          const randomName = await GetRandomUsername();
          setUsername(randomName);
        }

        // Load GPU adapters
        try {
          const adapters = await ipc.system.gpuAdapters();
          setGpuAdapters(adapters || []);
          setHasMultipleGpus(adapters && adapters.length > 1);

          const settings = await ipc.settings.get();
          const currentGpuPref = settings.gpuPreference ?? 'auto';
          setGpuPreference(currentGpuPref);
          setGpuAlreadyConfigured(currentGpuPref !== 'auto');
        } catch (err) {
          console.error('Failed to load GPU info:', err);
        }

        // Check for existing profiles
        try {
          const profiles = await ipc.profile.list();
          const hasProfiles = profiles && profiles.length > 0;
          setHasExistingProfiles(hasProfiles);
          if (hasProfiles) {
            setIsAuthenticated(true);
          }
        } catch (err) {
          console.error('Failed to load profiles:', err);
        }

        setTimeout(() => setIsReady(true), 50);
      } catch (err) {
        console.error('Failed to load defaults:', err);
        setIsReady(true);
      }
    };
    loadDefaults();
  }, []);

  // #endregion

  // #region Load Contributors

  const loadContributors = useCallback(async () => {
    setIsLoadingContributors(true);
    try {
      const response = await fetch('https://api.github.com/repos/yyyumeniku/HyPrism/contributors');
      if (response.ok) {
        const data = await response.json();
        setContributors(data);
      }
    } catch (err) {
      console.error('Failed to load contributors:', err);
    }
    setIsLoadingContributors(false);
  }, []);

  useEffect(() => {
    if (currentStep === 'about' && contributors.length === 0 && !isLoadingContributors) {
      loadContributors();
    }
  }, [currentStep, contributors.length, isLoadingContributors, loadContributors]);

  // #endregion

  // #region Background Slideshow

  useEffect(() => {
    if (backgroundMode === 'slideshow') {
      const interval = setInterval(() => {
        setCurrentBackgroundIndex(prev => (prev + 1) % backgroundImages.length);
      }, 8000);
      return () => clearInterval(interval);
    }
  }, [backgroundMode]);

  // #endregion

  // #region Splash Animation

  useEffect(() => {
    if (phase === 'splash' && !splashAnimationComplete) {
      const timer = setTimeout(() => {
        setSplashAnimationComplete(true);
      }, 2500);
      return () => clearTimeout(timer);
    }
  }, [phase, splashAnimationComplete]);

  // #endregion

  // #region Handlers

  const handleEnterAuth = useCallback(() => {
    if (hasExistingProfiles) {
      setPhase('setup');
      setCurrentStep('language');
    } else {
      setPhase('auth');
    }
  }, [hasExistingProfiles]);

  const handleLanguageChange = useCallback((langCode: Language) => {
    i18n.changeLanguage(langCode);
  }, [i18n]);

  const handleGenerateUsername = useCallback(async () => {
    setIsGeneratingUsername(true);
    try {
      const randomName = await GetRandomUsername();
      setUsername(randomName);
    } catch (err) {
      console.error('Failed to generate username:', err);
    } finally {
      setIsGeneratingUsername(false);
    }
  }, []);

  const handleNextStep = useCallback(() => {
    const stepIds = steps.map(s => s.id);
    const currentIndex = stepIds.indexOf(currentStep);
    if (currentIndex < stepIds.length - 1) {
      setCurrentStep(stepIds[currentIndex + 1]);
    }
  }, [steps, currentStep]);

  const handlePrevStep = useCallback(() => {
    const stepIds = steps.map(s => s.id);
    const currentIndex = stepIds.indexOf(currentStep);
    if (currentIndex > 0) {
      setCurrentStep(stepIds[currentIndex - 1]);
    }
  }, [steps, currentStep]);

  const handleBrowseInstanceDir = useCallback(async () => {
    try {
      const selectedPath = await BrowseFolder(instanceDir || defaultInstanceDir);
      if (selectedPath) {
        setInstanceDir(selectedPath);
      }
    } catch (err) {
      console.error('Failed to browse folder:', err);
    }
  }, [instanceDir, defaultInstanceDir]);

  const handleBackgroundModeChange = useCallback(async (mode: string) => {
    setBackgroundMode(mode);
    try {
      await SetBackgroundModeBackend(mode);
    } catch {}
  }, []);

  const handleGpuPreferenceChange = useCallback(async (preference: string) => {
    setGpuPreference(preference);
    try {
      await ipc.settings.update({ gpuPreference: preference });
    } catch (err) {
      console.error('Failed to update GPU preference:', err);
    }
  }, []);

  // #endregion

  // #region Auth Handlers

  const handleLogin = useCallback(async () => {
    setIsAuthenticating(true);
    setAuthError(null);
    setAuthErrorType('error');

    try {
      const result = await ipc.auth.login();
      if (result?.loggedIn && result.username && result.uuid) {
        const profile = await ipc.profile.create({
          name: result.username,
          uuid: result.uuid,
          isOfficial: true,
        });
        if (profile && profile.id) {
          setIsAuthenticated(true);
          setAuthenticatedUsername(result.username);
          setPhase('setup');
          setCurrentStep('language');
        } else {
          setAuthError(t('onboarding.auth.createFailed'));
          setAuthErrorType('error');
        }
      } else if (result?.errorType === 'no_profile') {
        setAuthError(t('onboarding.auth.noHytaleProfile'));
        setAuthErrorType('warning');
      } else {
        setAuthError(t('onboarding.auth.failed'));
        setAuthErrorType('error');
      }
    } catch (err) {
      console.error('[Onboarding] Auth failed:', err);
      setAuthError(t('onboarding.auth.error'));
      setAuthErrorType('error');
    } finally {
      setIsAuthenticating(false);
    }
  }, [t]);

  const handleSkipAuth = useCallback(async () => {
    setIsAuthenticated(false);
    setSkipAuthEnabledMirrorCount(null);

    try {
      const sources = await ipc.settings.hasDownloadSources();
      setSkipAuthEnabledMirrorCount(typeof sources.enabledMirrorCount === 'number' ? sources.enabledMirrorCount : 0);
    } catch (err) {
      console.warn('[Onboarding] Failed to check mirror count:', err);
      setSkipAuthEnabledMirrorCount(0);
    }

    setPhase('warning');
  }, []);

  const handleContinueWithoutAuth = useCallback(() => {
    setPhase('setup');
    setCurrentStep('language');
  }, []);

  const handleBackToAuth = useCallback(() => {
    setPhase('auth');
  }, []);

  // #endregion

  // #region Complete / Skip

  const handleComplete = useCallback(async () => {
    setIsLoading(true);
    try {
      if (!isAuthenticated && username.trim()) {
        const uuid = crypto.randomUUID();
        await ipc.profile.create({
          name: username.trim(),
          uuid: uuid,
          isOfficial: false,
        });
      }

      if (instanceDir && instanceDir !== defaultInstanceDir) {
        await SetInstanceDirectory(instanceDir);
      }

      await SetBackgroundModeBackend(backgroundMode);
      await SetHasCompletedOnboarding(true);
      clearCache();
      onComplete();
    } catch (err) {
      console.error('Failed to complete onboarding:', err);
    } finally {
      setIsLoading(false);
    }
  }, [isAuthenticated, username, instanceDir, defaultInstanceDir, backgroundMode, clearCache, onComplete]);

  const handleSkip = useCallback(async () => {
    setIsLoading(true);
    try {
      if (!isAuthenticated) {
        const randomName = username.trim() || generateRandomNick();
        const uuid = crypto.randomUUID();
        await ipc.profile.create({
          name: randomName,
          uuid: uuid,
          isOfficial: false,
        });
      }

      await SetHasCompletedOnboarding(true);
      clearCache();
      onComplete();
    } catch (err) {
      console.error('Failed to skip onboarding:', err);
    } finally {
      setIsLoading(false);
    }
  }, [isAuthenticated, username, clearCache, onComplete]);

  // #endregion

  // #region Social Links

  const openGitHub = useCallback(() => openUrl('https://github.com/yyyumeniku/HyPrism'), []);
  
  const openDiscord = useCallback(async () => {
    const link = await GetDiscordLink();
    openUrl(link);
  }, []);
  
  const openBugReport = useCallback(() => openUrl('https://github.com/yyyumeniku/HyPrism/issues/new'), []);

  // #endregion

  // #region Helpers

  const getCurrentBackground = useCallback(() => {
    if (backgroundMode === 'slideshow') {
      const index = currentBackgroundIndex % backgroundImages.length;
      return backgroundImages[index]?.url || backgroundImages[0]?.url;
    }
    const selected = backgroundImages.find(bg => bg.name === backgroundMode);
    return selected?.url || backgroundImages[0]?.url;
  }, [backgroundMode, currentBackgroundIndex]);

  const truncateName = useCallback((name: string, maxLength: number) => {
    if (name.length <= maxLength) return name;
    return name.substring(0, maxLength - 1) + '…';
  }, []);

  // Maintainer and other contributors
  const maintainer = useMemo(() => contributors.find(c => c.login.toLowerCase() === 'yyyumeniku'), [contributors]);
  const otherContributors = useMemo(() => contributors.filter(c => c.login.toLowerCase() !== 'yyyumeniku').slice(0, 10), [contributors]);

  // #endregion

  // #region Return

  return {
    // Phase & Step
    phase,
    setPhase,
    currentStep,
    setCurrentStep,
    splashAnimationComplete,
    isReady,
    steps,
    currentStepIndex,

    // Auth
    isAuthenticated,
    isAuthenticating,
    authError,
    authErrorType,
    authenticatedUsername,

    // Form
    username,
    setUsername,
    instanceDir,
    setInstanceDir,
    defaultInstanceDir,
    isLoading,
    isGeneratingUsername,
    launcherVersion,

    // Visual
    backgroundMode,
    currentBackgroundIndex,
    accentColor,
    accentTextColor,
    setAccentColor,

    // GPU
    gpuAdapters,
    gpuPreference,
    hasMultipleGpus,

    // Profile
    hasExistingProfiles,

    // Contributors
    contributors,
    isLoadingContributors,
    maintainer,
    otherContributors,

    // Handlers
    handleEnterAuth,
    handleLanguageChange,
    handleGenerateUsername,
    handleNextStep,
    handlePrevStep,
    handleBrowseInstanceDir,
    handleBackgroundModeChange,
    handleGpuPreferenceChange,
    handleLogin,
    handleSkipAuth,
    handleContinueWithoutAuth,
    handleBackToAuth,

    skipAuthEnabledMirrorCount,
    handleComplete,
    handleSkip,
    openGitHub,
    openDiscord,
    openBugReport,

    // Helpers
    getCurrentBackground,
    truncateName,

    // i18n
    t,
  };
  // #endregion
}

/** Full return type of the {@link useOnboarding} hook. */
export type UseOnboardingReturn = ReturnType<typeof useOnboarding>;

// #endregion
