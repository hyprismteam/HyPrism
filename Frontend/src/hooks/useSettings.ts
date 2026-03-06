import { useState, useEffect, useRef, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { ipc, on } from '@/lib/ipc';
import { useAccentColor } from '@/contexts/AccentColorContext';
import { changeLanguage } from '@/i18n';
import { Language } from '@/constants/enums';
import type { InstalledVersionInfo } from '@/types';
import {
  parseJavaHeapMb,
  upsertJavaHeapArgument,
  upsertJavaGcMode,
  detectJavaGcMode,
  sanitizeAdvancedJavaArguments,
} from '@/lib/java-utils';

// Re-export Java utilities for backward compatibility
export {
  parseJavaHeapMb,
  upsertJavaHeapArgument,
  removeJavaFlag,
  upsertJavaGcMode,
  detectJavaGcMode,
  sanitizeAdvancedJavaArguments,
} from '@/lib/java-utils';

export interface Contributor {
  login: string;
  avatar_url: string;
  html_url: string;
  contributions: number;
}

export type SettingsTab = 
  | 'general' 
  | 'downloads' 
  | 'java' 
  | 'visual' 
  | 'network' 
  | 'graphics' 
  | 'variables' 
  | 'logs' 
  | 'data' 
  | 'about' 
  | 'developer';

export interface UseSettingsOptions {
  launcherBranch: string;
  onLauncherBranchChange: (branch: string) => void;
  onBackgroundModeChange?: (mode: string) => void;
  onInstanceDeleted?: () => void;
  onAuthSettingsChange?: () => void;
  onMovingDataChange?: (isMoving: boolean) => void;
  onClose?: () => void;
}

// ============================================================================
// IPC Helper Functions
// ============================================================================

async function GetCloseAfterLaunch(): Promise<boolean> { 
  return (await ipc.settings.get()).closeAfterLaunch ?? false; 
}

async function SetCloseAfterLaunch(v: boolean): Promise<void> { 
  await ipc.settings.update({ closeAfterLaunch: v }); 
}

async function GetShowAlphaMods(): Promise<boolean> { 
  return (await ipc.settings.get()).showAlphaMods ?? false; 
}

async function SetShowAlphaMods(v: boolean): Promise<void> { 
  await ipc.settings.update({ showAlphaMods: v }); 
}

async function SetUseDualAuth(v: boolean): Promise<void> {
  await ipc.settings.update({ useDualAuth: v });
}

async function GetBackgroundMode(): Promise<string> { 
  return (await ipc.settings.get()).backgroundMode ?? 'image'; 
}

async function SetBackgroundMode(v: string): Promise<void> { 
  await ipc.settings.update({ backgroundMode: v }); 
}

async function GetCustomInstanceDir(): Promise<string> { 
  return (await ipc.settings.get()).instanceDirectory ?? ''; 
}

async function GetNick(): Promise<string> { 
  return (await ipc.profile.get()).nick ?? 'HyPrism'; 
}

async function GetAvatarPreview(): Promise<string | null> { 
  return (await ipc.profile.get()).avatarPath ?? null; 
}

async function GetAuthDomain(): Promise<string> { 
  return (await ipc.settings.get()).authDomain ?? 'sessions.sanasol.ws'; 
}

async function GetDiscordLink(): Promise<string> { 
  console.warn('[IPC] GetDiscordLink: stub'); 
  return 'https://discord.gg/ekZqTtynjp'; 
}

async function GetLauncherFolderPath(): Promise<string> { 
  return ipc.settings.launcherPath(); 
}

async function GetDefaultInstanceDir(): Promise<string> { 
  return ipc.settings.defaultInstanceDir(); 
}

async function SetInstanceDirectory(path: string): Promise<{ success: boolean, path: string }> {
  const result = await ipc.settings.setInstanceDir(path);
  return result;
}

async function BrowseFolder(initialPath?: string): Promise<string> { 
  return (await ipc.file.browseFolder(initialPath)) ?? ''; 
}

async function BrowseJavaExecutable(): Promise<string> { 
  return (await ipc.file.browseJavaExecutable()) ?? ''; 
}

async function FileExists(path: string): Promise<boolean> { 
  return await ipc.file.exists(path); 
}

// Stubs for IPC channels that need implementation
const _stub = <T,>(name: string, fb: T) => async (..._a: unknown[]): Promise<T> => { 
  console.warn(`[IPC] ${name}: no channel`); 
  return fb; 
};

const DeleteLauncherData = _stub('DeleteLauncherData', true);
const ResetOnboarding = _stub('ResetOnboarding', undefined as void);

async function GetInstalledVersionsDetailed(): Promise<InstalledVersionInfo[]> {
  const instances = await ipc.game.instances();
  return (instances || []).map((inst) => ({
    id: inst.id,
    branch: inst.branch,
    version: inst.version,
    path: inst.path,
    sizeBytes: inst.totalSize,
    isLatest: inst.version === 0,
    isLatestInstance: inst.version === 0,
  }));
}


// ============================================================================
// Environment Variable Helpers
// ============================================================================

export const ENV_PRESETS = {
  forceX11: 'SDL_VIDEODRIVER=x11',
  disableVkLayers: 'VK_LOADER_LAYERS_DISABLE=all',
} as const;

export const toggleEnvPreset = (currentVars: string, preset: string, enabled: boolean): string => {
  const vars = currentVars.split(/\s+/).filter(v => v.trim());
  const withoutPreset = vars.filter(v => !v.startsWith(preset.split('=')[0] + '='));
  if (enabled) withoutPreset.push(preset);
  return withoutPreset.join(' ');
};

export const validateEnvVars = (value: string, t: (key: string) => string): { valid: boolean; error: string } => {
  if (!value.trim()) return { valid: true, error: '' };

  const parts = value.split(/\s+/).filter(v => v.trim());
  const envVarPattern = /^[A-Za-z_][A-Za-z0-9_]*=.*/;

  for (const part of parts) {
    if (!envVarPattern.test(part.trim())) {
      return { valid: false, error: t('settings.variablesSettings.envVarsInvalidFormat') };
    }
  }
  return { valid: true, error: '' };
};

// ============================================================================
// Main Hook
// ============================================================================

export function useSettings(options: UseSettingsOptions) {
  const { t, i18n } = useTranslation();
  const { accentColor, accentTextColor, setAccentColor: setAccentColorContext } = useAccentColor();

  // Tab state
  const [activeTab, setActiveTab] = useState<SettingsTab>('general');

  // Dropdown states
  const [isLanguageOpen, setIsLanguageOpen] = useState(false);
  const [isBranchOpen, setIsBranchOpen] = useState(false);
  const languageDropdownRef = useRef<HTMLDivElement>(null);
  const branchDropdownRef = useRef<HTMLDivElement>(null);

  // Launcher branch
  const [selectedLauncherBranch, setSelectedLauncherBranch] = useState(options.launcherBranch);

  // General settings
  const [hasOfficialAccount, setHasOfficialAccount] = useState(false);
  const [isActiveProfileOfficial, setIsActiveProfileOfficial] = useState(false);
  const [profileLoaded, setProfileLoaded] = useState(false);
  const [closeAfterLaunch, setCloseAfterLaunch] = useState(false);
  const [showAlphaMods, setShowAlphaMods] = useState(false);
  const [devModeEnabled, setDevModeEnabled] = useState(false);
  const [onlineMode, setOnlineMode] = useState(true);
  const [useDualAuth, setUseDualAuth] = useState(true);
  const [launchAfterDownload, setLaunchAfterDownload] = useState(true);

  // Java settings
  const [javaArguments, setJavaArguments] = useState('');
  const [javaRamMb, setJavaRamMb] = useState(4096);
  const [javaInitialRamMb, setJavaInitialRamMb] = useState(1024);
  const [javaGcMode, setJavaGcMode] = useState<'auto' | 'g1'>('auto');
  const [javaRuntimeMode, setJavaRuntimeMode] = useState<'bundled' | 'custom'>('bundled');
  const [customJavaPath, setCustomJavaPath] = useState('');
  const [javaCustomPathError, setJavaCustomPathError] = useState('');
  const [javaArgumentsError, setJavaArgumentsError] = useState('');
  const [systemMemoryMb, setSystemMemoryMb] = useState(8192);

  // Visual settings
  const [backgroundMode, setBackgroundModeState] = useState('slideshow');
  const [showAllBackgrounds, setShowAllBackgrounds] = useState(false);

  // Graphics settings
  const [gpuPreference, setGpuPreferenceState] = useState<string>('dedicated');
  const [gpuAdapters, setGpuAdapters] = useState<Array<{ name: string; vendor: string; type: string }>>([]);
  const [hasSingleGpu, setHasSingleGpu] = useState(false);

  // Network settings
  const [authDomain, setAuthDomain] = useState('sessions.sanasol.ws');
  const [authMode, setAuthModeState] = useState<'default' | 'official' | 'custom'>('default');
  const [customAuthDomain, setCustomAuthDomain] = useState('');

  // Environment variables (Linux)
  const [gameEnvVars, setGameEnvVars] = useState('');
  const [gameEnvVarsError, setGameEnvVarsError] = useState('');
  const [gameEnvVarsFocus, setGameEnvVarsFocus] = useState(false);
  const [envForceX11, setEnvForceX11] = useState(false);
  const [envDisableVkLayers, setEnvDisableVkLayers] = useState(false);
  const [isLinux, setIsLinux] = useState(false);

  // Data settings
  const [launcherFolderPath, setLauncherFolderPath] = useState('');
  const [instanceDir, setInstanceDir] = useState('');
  const [launcherDataDir, setLauncherDataDir] = useState('');
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);

  // Data move progress
  const [isMovingData, setIsMovingData] = useState(false);
  const [moveProgress, setMoveProgress] = useState(0);
  const [moveCurrentFile, setMoveCurrentFile] = useState('');

  // Instances state
  const [installedInstances, setInstalledInstances] = useState<InstalledVersionInfo[]>([]);
  const [isLoadingInstances, setIsLoadingInstances] = useState(false);

  // About tab state
  const [contributors, setContributors] = useState<Contributor[]>([]);
  const [isLoadingContributors, setIsLoadingContributors] = useState(false);
  const [contributorsError, setContributorsError] = useState<string | null>(null);

  // Computed values
  const detectedSystemRamMb = Math.max(4096, systemMemoryMb);
  const minJavaRamMb = 1024;
  const maxJavaRamMb = Math.max(minJavaRamMb, Math.floor((detectedSystemRamMb * 0.75) / 256) * 256);

  // Glass control class
  const gc = 'glass-control-solid';

  // ============================================================================
  // Effects
  // ============================================================================

  // Notify parent about moving state change
  useEffect(() => {
    options.onMovingDataChange?.(isMovingData);
  }, [isMovingData, options.onMovingDataChange]);

  // Load initial settings
  useEffect(() => {
    const loadSettings = async () => {
      try {
        // Platform detection
        try {
          const platformInfo = await ipc.system.platform();
          setIsLinux(platformInfo.isLinux);
        } catch (err) {
          console.error('Failed to load platform info:', err);
        }

        const closeAfter = await GetCloseAfterLaunch();
        setCloseAfterLaunch(closeAfter);

        const alphaMods = await GetShowAlphaMods();
        setShowAlphaMods(alphaMods);

        const folderPath = await GetLauncherFolderPath();
        setLauncherFolderPath(folderPath);

        const customDir = await GetCustomInstanceDir();
        const defaultInstanceDir = await GetDefaultInstanceDir();
        setInstanceDir(customDir || defaultInstanceDir);

        const settingsSnapshot = await ipc.settings.get();
        const online = settingsSnapshot.onlineMode ?? true;
        setOnlineMode(online);

        setUseDualAuth(settingsSnapshot.useDualAuth ?? true);
        setLaunchAfterDownload(settingsSnapshot.launchAfterDownload ?? true);

        const loadedJavaArgs = settingsSnapshot.javaArguments;
        const normalizedJavaArgs = typeof loadedJavaArgs === 'string' ? loadedJavaArgs : '';
        setJavaArguments(normalizedJavaArgs);

        const loadedSystemMemoryMb = typeof settingsSnapshot.systemMemoryMb === 'number' && settingsSnapshot.systemMemoryMb > 0
          ? settingsSnapshot.systemMemoryMb
          : systemMemoryMb;
        setSystemMemoryMb(loadedSystemMemoryMb);
        const loadedDetectedSystemRamMb = Math.max(4096, loadedSystemMemoryMb);
        const loadedMaxJavaRamMb = Math.max(minJavaRamMb, Math.floor((loadedDetectedSystemRamMb * 0.75) / 256) * 256);

        const parsedJavaRamMb = parseJavaHeapMb(normalizedJavaArgs, 'xmx');
        const targetJavaRamMb = parsedJavaRamMb ?? 4096;
        const clampedJavaRamMb = Math.min(loadedMaxJavaRamMb, Math.max(minJavaRamMb, Math.round(targetJavaRamMb / 256) * 256));
        setJavaRamMb(clampedJavaRamMb);

        const parsedJavaInitialRamMb = parseJavaHeapMb(normalizedJavaArgs, 'xms');
        const fallbackInitial = Math.max(minJavaRamMb, Math.min(clampedJavaRamMb, Math.floor(clampedJavaRamMb / 2 / 256) * 256 || minJavaRamMb));
        const initialRamTarget = parsedJavaInitialRamMb ?? fallbackInitial;
        const clampedInitial = Math.min(clampedJavaRamMb, Math.max(minJavaRamMb, Math.round(initialRamTarget / 256) * 256));
        setJavaInitialRamMb(clampedInitial);

        setJavaGcMode(detectJavaGcMode(normalizedJavaArgs));
        setJavaRuntimeMode(settingsSnapshot.useCustomJava ? 'custom' : 'bundled');
        setCustomJavaPath(typeof settingsSnapshot.customJavaPath === 'string' ? settingsSnapshot.customJavaPath : '');

        const bgMode = await GetBackgroundMode();
        setBackgroundModeState(bgMode);

        setLauncherDataDir(folderPath);

        // GPU settings
        const gpu = settingsSnapshot.gpuPreference ?? 'dedicated';
        setGpuPreferenceState(gpu);

        // Environment variables
        const envVars = settingsSnapshot.gameEnvironmentVariables ?? '';
        const envVarsStr = typeof envVars === 'string' ? envVars : '';
        setGameEnvVars(envVarsStr);
        setEnvForceX11(envVarsStr.includes('SDL_VIDEODRIVER=x11'));
        setEnvDisableVkLayers(envVarsStr.includes('VK_LOADER_LAYERS_DISABLE=all'));

        try {
          const adapters = await ipc.system.gpuAdapters();
          setGpuAdapters(adapters || []);
          const singleGpu = !adapters || adapters.length <= 1;
          setHasSingleGpu(singleGpu);
          if (singleGpu && gpu !== 'auto') {
            setGpuPreferenceState('auto');
            await ipc.settings.update({ gpuPreference: 'auto' });
          }
        } catch (err) {
          console.error('Failed to load GPU adapters:', err);
        }

        await GetNick();

        const domain = await GetAuthDomain();
        if (domain) {
          setAuthDomain(domain);
          if (domain === 'sessions.hytale.com' || domain === 'official') {
            setAuthModeState('official');
          } else if (domain === 'sessions.sanasol.ws' || domain === '' || !domain) {
            setAuthModeState('default');
          } else {
            setAuthModeState('custom');
            setCustomAuthDomain(domain);
          }
        }

        try {
          await GetAvatarPreview();
        } catch { /* ignore */ }

        try {
          const profiles = await ipc.profile.list();
          const hasOfficial = profiles.some(p => p.isOfficial === true);
          setHasOfficialAccount(hasOfficial);
          
          // Check if active profile is official
          const activeProfile = await ipc.profile.get();
          const activeUuid = activeProfile.uuid;
          const matchedProfile = (profiles as any[])?.find((p: any) => p.uuid === activeUuid || p.UUID === activeUuid);
          const isOfficial = matchedProfile?.isOfficial === true || matchedProfile?.IsOfficial === true;
          setIsActiveProfileOfficial(isOfficial);
        } catch { /* ignore */ }

        const savedDevMode = localStorage.getItem('hyprism_dev_mode');
        setDevModeEnabled(savedDevMode === 'true');
        setProfileLoaded(true);
      } catch (err) {
        console.error('Failed to load settings:', err);
        setProfileLoaded(true);
      }
    };
    loadSettings();
  }, []);

  // Subscribe to data move progress events
  useEffect(() => {
    const unsub = on('hyprism:game:progress', (data: unknown) => {
      const progressData = data as { state?: string; progress?: number; args?: unknown[] };
      if (progressData.state === 'moving-instances') {
        setIsMovingData(true);
        setMoveProgress(progressData.progress ?? 0);
        if (Array.isArray(progressData.args) && progressData.args.length > 0) {
          setMoveCurrentFile(String(progressData.args[0]));
        }
      } else if (progressData.state === 'moving-instances-complete' && isMovingData) {
        setMoveProgress(100);
        setTimeout(() => {
          setIsMovingData(false);
          setMoveProgress(0);
          setMoveCurrentFile('');
        }, 1500);
      }
    });
    return unsub;
  }, [isMovingData]);

  // Load contributors when About tab is active
  useEffect(() => {
    if (activeTab === 'about' && contributors.length === 0 && !isLoadingContributors) {
      setIsLoadingContributors(true);
      setContributorsError(null);
      fetch('https://api.github.com/repos/hyprismteam/HyPrism/contributors')
        .then(res => res.json())
        .then(data => {
          if (Array.isArray(data)) {
            setContributors(data);
          } else {
            const msg = (data && typeof data === 'object' && 'message' in data)
              ? String((data as { message: string }).message)
              : 'Failed to load contributors';
            setContributorsError(msg);
          }
        })
        .catch(err => {
          console.error('Failed to load contributors:', err);
          setContributorsError(err instanceof Error ? err.message : 'Failed to load contributors');
        })
        .finally(() => setIsLoadingContributors(false));
    }
  }, [activeTab, contributors.length, isLoadingContributors]);

  // Click outside handler for dropdowns
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (languageDropdownRef.current && !languageDropdownRef.current.contains(e.target as Node)) {
        setIsLanguageOpen(false);
      }
      if (branchDropdownRef.current && !branchDropdownRef.current.contains(e.target as Node)) {
        setIsBranchOpen(false);
      }
    };

    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        if (showAllBackgrounds) {
          setShowAllBackgrounds(false);
        } else {
          options.onClose?.();
        }
      }
    };

    document.addEventListener('mousedown', handleClickOutside);
    document.addEventListener('keydown', handleEscape);
    return () => {
      document.removeEventListener('mousedown', handleClickOutside);
      document.removeEventListener('keydown', handleEscape);
    };
  }, [options.onClose, showAllBackgrounds]);

  // ============================================================================
  // Handlers
  // ============================================================================

  const handleLanguageSelect = useCallback(async (langCode: Language) => {
    setIsLanguageOpen(false);
    try {
      await changeLanguage(langCode);
    } catch (error) {
      console.warn('Failed to change language:', error);
    }
  }, []);

  const handleLauncherBranchChange = useCallback(async (branch: string) => {
    setSelectedLauncherBranch(branch);
    setIsBranchOpen(false);
    options.onLauncherBranchChange(branch);
  }, [options.onLauncherBranchChange]);

  const handleCloseAfterLaunchChange = useCallback(async () => {
    const newValue = !closeAfterLaunch;
    setCloseAfterLaunch(newValue);
    await SetCloseAfterLaunch(newValue);
  }, [closeAfterLaunch]);

  const handleShowAlphaModsChange = useCallback(async () => {
    const newValue = !showAlphaMods;
    setShowAlphaMods(newValue);
    await SetShowAlphaMods(newValue);
  }, [showAlphaMods]);

  const handleUseDualAuthChange = useCallback(async () => {
    const newValue = !useDualAuth;
    setUseDualAuth(newValue);
    await SetUseDualAuth(newValue);
  }, [useDualAuth]);

  const handleSaveJavaArguments = useCallback(async () => {
    const { sanitized, blocked } = sanitizeAdvancedJavaArguments(javaArguments);

    if (blocked) {
      setJavaArgumentsError(t('settings.javaSettings.jvmArgumentsBlocked'));
    } else {
      setJavaArgumentsError('');
    }

    setJavaArguments(sanitized);

    try {
      await ipc.settings.update({ javaArguments: sanitized });
      const parsedRam = parseJavaHeapMb(sanitized, 'xmx');
      if (parsedRam != null) {
        const clampedRam = Math.min(maxJavaRamMb, Math.max(minJavaRamMb, Math.round(parsedRam / 256) * 256));
        setJavaRamMb(clampedRam);

        const parsedInitial = parseJavaHeapMb(sanitized, 'xms');
        if (parsedInitial != null) {
          const clampedInitial = Math.min(clampedRam, Math.max(minJavaRamMb, Math.round(parsedInitial / 256) * 256));
          setJavaInitialRamMb(clampedInitial);
        }
      }
      setJavaGcMode(detectJavaGcMode(sanitized));
    } catch (err) {
      console.error('Failed to update Java arguments:', err);
    }
  }, [javaArguments, maxJavaRamMb, t]);

  const handleJavaRamChange = useCallback(async (value: number) => {
    const clampedRam = Math.min(maxJavaRamMb, Math.max(minJavaRamMb, value));
    setJavaRamMb(clampedRam);

    const clampedInitial = Math.min(clampedRam, javaInitialRamMb);
    if (clampedInitial !== javaInitialRamMb) {
      setJavaInitialRamMb(clampedInitial);
    }

    const withMaxHeap = upsertJavaHeapArgument(javaArguments, 'Xmx', clampedRam);
    const updatedArgs = upsertJavaHeapArgument(withMaxHeap, 'Xms', clampedInitial);
    setJavaArguments(updatedArgs);

    try {
      await ipc.settings.update({ javaArguments: updatedArgs });
    } catch (err) {
      console.error('Failed to update Java RAM arguments:', err);
    }
  }, [javaArguments, javaInitialRamMb, maxJavaRamMb]);

  const handleJavaInitialRamChange = useCallback(async (value: number) => {
    const clampedInitial = Math.min(javaRamMb, Math.max(minJavaRamMb, value));
    setJavaInitialRamMb(clampedInitial);

    const updatedArgs = upsertJavaHeapArgument(javaArguments, 'Xms', clampedInitial);
    setJavaArguments(updatedArgs);

    try {
      await ipc.settings.update({ javaArguments: updatedArgs });
    } catch (err) {
      console.error('Failed to update Java initial RAM arguments:', err);
    }
  }, [javaArguments, javaRamMb]);

  const handleJavaGcModeChange = useCallback(async (mode: 'auto' | 'g1') => {
    setJavaGcMode(mode);
    const updatedArgs = upsertJavaGcMode(javaArguments, mode);
    setJavaArguments(updatedArgs);

    try {
      await ipc.settings.update({ javaArguments: updatedArgs });
    } catch (err) {
      console.error('Failed to update Java GC mode:', err);
    }
  }, [javaArguments]);

  const handleJavaRuntimeModeChange = useCallback(async (mode: 'bundled' | 'custom') => {
    setJavaRuntimeMode(mode);
    setJavaCustomPathError('');

    try {
      await ipc.settings.update({ useCustomJava: mode === 'custom' });
    } catch (err) {
      console.error('Failed to update Java runtime mode:', err);
    }
  }, []);

  const handleCustomJavaPathSave = useCallback(async () => {
    const normalizedPath = customJavaPath.trim();
    setCustomJavaPath(normalizedPath);

    if (!normalizedPath) {
      setJavaCustomPathError(t('settings.javaSettings.customJavaPathRequired'));
      return;
    }

    const exists = await FileExists(normalizedPath);
    if (!exists) {
      setJavaCustomPathError(t('settings.javaSettings.customJavaPathNotFound'));
      return;
    }

    setJavaCustomPathError('');

    try {
      await ipc.settings.update({ customJavaPath: normalizedPath, useCustomJava: true });
      setJavaRuntimeMode('custom');
    } catch (err) {
      console.error('Failed to save custom Java path:', err);
    }
  }, [customJavaPath, t]);

  const handleBrowseCustomJavaPath = useCallback(async () => {
    try {
      const picked = await BrowseJavaExecutable();
      if (!picked) return;
      setCustomJavaPath(picked);
      setJavaCustomPathError('');
    } catch (err) {
      console.error('Failed to browse Java executable:', err);
    }
  }, []);

  const handleBackgroundModeChange = useCallback(async (mode: string) => {
    setBackgroundModeState(mode);
    try {
      await SetBackgroundMode(mode);
      options.onBackgroundModeChange?.(mode);
    } catch (err) {
      console.error('Failed to set background mode:', err);
    }
  }, [options.onBackgroundModeChange]);

  const handleGpuPreferenceChange = useCallback(async (preference: string) => {
    setGpuPreferenceState(preference);
    try {
      await ipc.settings.update({ gpuPreference: preference });
    } catch (err) {
      console.error('Failed to update GPU preference:', err);
    }
  }, []);

  const handleEnvPresetToggle = useCallback(async (presetKey: keyof typeof ENV_PRESETS, enabled: boolean) => {
    const preset = ENV_PRESETS[presetKey];
    const newEnvVars = toggleEnvPreset(gameEnvVars, preset, enabled);
    setGameEnvVars(newEnvVars);

    if (presetKey === 'forceX11') setEnvForceX11(enabled);
    if (presetKey === 'disableVkLayers') setEnvDisableVkLayers(enabled);

    try {
      await ipc.settings.update({ gameEnvironmentVariables: newEnvVars });
    } catch (err) {
      console.error('Failed to save environment variables:', err);
    }
  }, [gameEnvVars]);

  const handleSaveGameEnvVars = useCallback(async () => {
    const { valid, error } = validateEnvVars(gameEnvVars, t);
    if (!valid) {
      setGameEnvVarsError(error);
      return;
    }
    setGameEnvVarsError('');

    setEnvForceX11(gameEnvVars.includes('SDL_VIDEODRIVER=x11'));
    setEnvDisableVkLayers(gameEnvVars.includes('VK_LOADER_LAYERS_DISABLE=all'));

    try {
      await ipc.settings.update({ gameEnvironmentVariables: gameEnvVars });
    } catch (err) {
      console.error('Failed to save game environment variables:', err);
    }
  }, [gameEnvVars, t]);

  const handleBrowseInstanceDir = useCallback(async () => {
    try {
      const selectedPath = await BrowseFolder(instanceDir || launcherFolderPath);
      if (selectedPath) {
        setIsMovingData(true);
        setMoveProgress(0);
        setMoveCurrentFile('');

        const result = await SetInstanceDirectory(selectedPath);
        if (result.success) {
          setInstanceDir(result.path || selectedPath);
        } else {
          setIsMovingData(false);
        }
      }
    } catch (err) {
      console.error('Failed to set instance directory:', err);
      setIsMovingData(false);
    }
  }, [instanceDir, launcherFolderPath]);

  const handleResetInstanceDir = useCallback(async () => {
    try {
      const defaultDir = await GetDefaultInstanceDir();
      setIsMovingData(true);
      setMoveProgress(0);
      setMoveCurrentFile('');

      const result = await SetInstanceDirectory('');
      if (result.success) {
        setInstanceDir(result.path || defaultDir);
      } else {
        setIsMovingData(false);
      }
    } catch (err) {
      console.error('Failed to reset instance directory:', err);
      setIsMovingData(false);
    }
  }, []);

  const handleDeleteLauncherData = useCallback(async () => {
    const success = await DeleteLauncherData();
    if (success) {
      setShowDeleteConfirm(false);
      options.onClose?.();
    }
  }, [options.onClose]);

  const handleAccentColorChange = useCallback(async (color: string) => {
    await setAccentColorContext(color);
  }, [setAccentColorContext]);

  const handleDevModeToggle = useCallback(() => {
    const newValue = !devModeEnabled;
    setDevModeEnabled(newValue);
    localStorage.setItem('hyprism_dev_mode', newValue ? 'true' : 'false');
    window.dispatchEvent(new CustomEvent('hyprism:devmode-changed', { detail: { enabled: newValue } }));
  }, [devModeEnabled]);

  const loadInstances = useCallback(async () => {
    setIsLoadingInstances(true);
    try {
      const instances = await GetInstalledVersionsDetailed();
      setInstalledInstances(instances || []);
    } catch (err) {
      console.error('Failed to load instances:', err);
    }
    setIsLoadingInstances(false);
  }, []);

  const openGitHub = useCallback(() => {
    import('@/utils/openUrl').then(({ openUrl }) => openUrl('https://github.com/hyprismteam/HyPrism'));
  }, []);

  const openBugReport = useCallback(() => {
    import('@/utils/openUrl').then(({ openUrl }) => openUrl('https://github.com/hyprismteam/HyPrism/issues/new'));
  }, []);

  const openDiscord = useCallback(async () => {
    const link = await GetDiscordLink();
    import('@/utils/openUrl').then(({ openUrl }) => openUrl(link));
  }, []);

  const resetOnboarding = useCallback(async () => {
    await ResetOnboarding();
    localStorage.removeItem('hyprism_onboarding_done');
  }, []);

  return {
    // State
    activeTab,
    setActiveTab,
    isLanguageOpen,
    setIsLanguageOpen,
    isBranchOpen,
    setIsBranchOpen,
    languageDropdownRef,
    branchDropdownRef,
    selectedLauncherBranch,
    hasOfficialAccount,
    isActiveProfileOfficial,
    closeAfterLaunch,
    showAlphaMods,
    devModeEnabled,
    onlineMode,
    setOnlineMode,
    useDualAuth,
    setUseDualAuth,
    handleUseDualAuthChange,
    profileLoaded,
    launchAfterDownload,
    setLaunchAfterDownload,
    javaArguments,
    setJavaArguments,
    javaRamMb,
    javaInitialRamMb,
    javaGcMode,
    javaRuntimeMode,
    customJavaPath,
    setCustomJavaPath,
    javaCustomPathError,
    javaArgumentsError,
    setJavaArgumentsError,
    systemMemoryMb,
    detectedSystemRamMb,
    minJavaRamMb,
    maxJavaRamMb,
    backgroundMode,
    showAllBackgrounds,
    setShowAllBackgrounds,
    gpuPreference,
    gpuAdapters,
    hasSingleGpu,
    authDomain,
    setAuthDomain,
    authMode,
    setAuthModeState,
    customAuthDomain,
    setCustomAuthDomain,
    gameEnvVars,
    setGameEnvVars,
    gameEnvVarsError,
    gameEnvVarsFocus,
    setGameEnvVarsFocus,
    envForceX11,
    envDisableVkLayers,
    isLinux,
    launcherFolderPath,
    instanceDir,
    launcherDataDir,
    showDeleteConfirm,
    setShowDeleteConfirm,
    isMovingData,
    moveProgress,
    moveCurrentFile,
    installedInstances,
    isLoadingInstances,
    contributors,
    isLoadingContributors,
    contributorsError,
    gc,
    accentColor,
    accentTextColor,

    // Handlers
    handleLanguageSelect,
    handleLauncherBranchChange,
    handleCloseAfterLaunchChange,
    handleShowAlphaModsChange,
    handleSaveJavaArguments,
    handleJavaRamChange,
    handleJavaInitialRamChange,
    handleJavaGcModeChange,
    handleJavaRuntimeModeChange,
    handleCustomJavaPathSave,
    handleBrowseCustomJavaPath,
    handleBackgroundModeChange,
    handleGpuPreferenceChange,
    handleEnvPresetToggle,
    handleSaveGameEnvVars,
    handleBrowseInstanceDir,
    handleResetInstanceDir,
    handleDeleteLauncherData,
    handleAccentColorChange,
    handleDevModeToggle,
    loadInstances,
    openGitHub,
    openBugReport,
    openDiscord,
    resetOnboarding,

    // Translation
    t,
    i18n,
  };
}
