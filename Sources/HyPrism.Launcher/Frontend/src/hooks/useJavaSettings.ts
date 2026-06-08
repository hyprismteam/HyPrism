import { useState, useCallback } from 'react';
import { ipc } from '@/lib/ipc';
import { useTranslation } from 'react-i18next';
import {
  parseJavaHeapMb,
  upsertJavaHeapArgument,
  upsertJavaGcMode,
  detectJavaGcMode,
  sanitizeAdvancedJavaArguments,
  formatRamLabel,
} from '@/lib/java-utils';
import type { GcMode, RuntimeMode } from '@/types/java';

export { formatRamLabel };

// #region IPC Helpers
/**
 * Checks whether a file exists at the given path.
 * @param path - Absolute file system path to check.
 * @returns `true` if the file exists, `false` otherwise.
 */
async function FileExists(path: string): Promise<boolean> { return await ipc.file.exists(path); }
/**
 * Opens a native file picker pre-filtered to Java executables.
 * @returns The selected file path, or an empty string if the user cancelled.
 */
async function BrowseJavaExecutable(): Promise<string> { return (await ipc.file.browseJavaExecutable()) ?? ''; }
// #endregion

/**
 * Options accepted by the {@link useJavaSettings} hook.
 */
interface UseJavaSettingsOptions {
  systemMemoryMb: number;
}

/**
 * Manages all Java runtime settings: maximum/initial RAM allocation, GC mode,
 * runtime selection (bundled vs custom), and the custom Java path.
 *
 * @param options - System memory available, used to cap the maximum allocatable RAM.
 * @returns Java settings state, computed limits, setters, and action handlers.
 */
export function useJavaSettings({ systemMemoryMb }: UseJavaSettingsOptions) {
  const { t } = useTranslation();
  
  const minJavaRamMb = 1024;
  const detectedSystemRamMb = Math.max(4096, systemMemoryMb);
  const maxJavaRamMb = Math.max(minJavaRamMb, Math.floor((detectedSystemRamMb * 0.75) / 256) * 256);

  const [javaArguments, setJavaArguments] = useState('');
  const [javaRamMb, setJavaRamMb] = useState(4096);
  const [javaInitialRamMb, setJavaInitialRamMb] = useState(1024);
  const [javaGcMode, setJavaGcMode] = useState<GcMode>('auto');
  const [javaRuntimeMode, setJavaRuntimeMode] = useState<RuntimeMode>('bundled');
  const [customJavaPath, setCustomJavaPath] = useState('');
  const [javaCustomPathError, setJavaCustomPathError] = useState('');
  const [javaArgumentsError, setJavaArgumentsError] = useState('');

  /**
   * Initialises all Java settings state from a persisted settings snapshot.
   * Call this once after loading settings from the backend.
   *
   * @param settingsSnapshot - A subset of the persisted settings object.
   */
  const loadFromSettings = useCallback((settingsSnapshot: {
    javaArguments?: string;
    useCustomJava?: boolean;
    customJavaPath?: string;
  }) => {
    const loadedJavaArgs = settingsSnapshot.javaArguments;
    const normalizedJavaArgs = typeof loadedJavaArgs === 'string' ? loadedJavaArgs : '';
    setJavaArguments(normalizedJavaArgs);

    // Parse max heap
    const parsedJavaRamMb = parseJavaHeapMb(normalizedJavaArgs, 'xmx');
    const targetJavaRamMb = parsedJavaRamMb ?? 4096;
    const clampedJavaRamMb = Math.min(maxJavaRamMb, Math.max(minJavaRamMb, Math.round(targetJavaRamMb / 256) * 256));
    setJavaRamMb(clampedJavaRamMb);

    // Parse initial heap
    const parsedJavaInitialRamMb = parseJavaHeapMb(normalizedJavaArgs, 'xms');
    const fallbackInitial = Math.max(minJavaRamMb, Math.min(clampedJavaRamMb, Math.floor(clampedJavaRamMb / 2 / 256) * 256 || minJavaRamMb));
    const initialRamTarget = parsedJavaInitialRamMb ?? fallbackInitial;
    const clampedInitial = Math.min(clampedJavaRamMb, Math.max(minJavaRamMb, Math.round(initialRamTarget / 256) * 256));
    setJavaInitialRamMb(clampedInitial);

    setJavaGcMode(detectJavaGcMode(normalizedJavaArgs));
    setJavaRuntimeMode(settingsSnapshot.useCustomJava ? 'custom' : 'bundled');
    setCustomJavaPath(typeof settingsSnapshot.customJavaPath === 'string' ? settingsSnapshot.customJavaPath : '');
  }, [maxJavaRamMb, minJavaRamMb]);

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
      const parsedMax = parseJavaHeapMb(sanitized, 'xmx');
      if (parsedMax != null) {
        const clampedMax = Math.min(maxJavaRamMb, Math.max(minJavaRamMb, Math.round(parsedMax / 256) * 256));
        setJavaRamMb(clampedMax);

        const parsedInitial = parseJavaHeapMb(sanitized, 'xms');
        if (parsedInitial != null) {
          const clampedInitial = Math.min(clampedMax, Math.max(minJavaRamMb, Math.round(parsedInitial / 256) * 256));
          setJavaInitialRamMb(clampedInitial);
        }
      }
      setJavaGcMode(detectJavaGcMode(sanitized));
    } catch (err) {
      console.error('Failed to update Java arguments:', err);
    }
  }, [javaArguments, maxJavaRamMb, minJavaRamMb, t]);

  const handleJavaRamChange = useCallback(async (value: number) => {
    const clampedJavaRamMb = Math.min(maxJavaRamMb, Math.max(minJavaRamMb, value));
    setJavaRamMb(clampedJavaRamMb);

    const clampedInitial = Math.min(clampedJavaRamMb, javaInitialRamMb);
    if (clampedInitial !== javaInitialRamMb) {
      setJavaInitialRamMb(clampedInitial);
    }

    const withMaxHeap = upsertJavaHeapArgument(javaArguments, 'Xmx', clampedJavaRamMb);
    const updatedJavaArgs = upsertJavaHeapArgument(withMaxHeap, 'Xms', clampedInitial);
    setJavaArguments(updatedJavaArgs);

    try {
      await ipc.settings.update({ javaArguments: updatedJavaArgs });
    } catch (err) {
      console.error('Failed to update Java RAM arguments:', err);
    }
  }, [javaArguments, javaInitialRamMb, maxJavaRamMb, minJavaRamMb]);

  const handleJavaInitialRamChange = useCallback(async (value: number) => {
    const clampedInitial = Math.min(javaRamMb, Math.max(minJavaRamMb, value));
    setJavaInitialRamMb(clampedInitial);

    const updatedJavaArgs = upsertJavaHeapArgument(javaArguments, 'Xms', clampedInitial);
    setJavaArguments(updatedJavaArgs);

    try {
      await ipc.settings.update({ javaArguments: updatedJavaArgs });
    } catch (err) {
      console.error('Failed to update Java initial RAM arguments:', err);
    }
  }, [javaArguments, javaRamMb, minJavaRamMb]);

  const handleJavaGcModeChange = useCallback(async (mode: GcMode) => {
    setJavaGcMode(mode);
    const updatedJavaArgs = upsertJavaGcMode(javaArguments, mode);
    setJavaArguments(updatedJavaArgs);

    try {
      await ipc.settings.update({ javaArguments: updatedJavaArgs });
    } catch (err) {
      console.error('Failed to update Java GC mode:', err);
    }
  }, [javaArguments]);

  const handleJavaRuntimeModeChange = useCallback(async (mode: RuntimeMode) => {
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

  return {
    // State
    javaArguments,
    javaRamMb,
    javaInitialRamMb,
    javaGcMode,
    javaRuntimeMode,
    customJavaPath,
    javaCustomPathError,
    javaArgumentsError,
    // Computed
    minJavaRamMb,
    maxJavaRamMb,
    // Setters
    setJavaArguments,
    setCustomJavaPath,
    // Handlers
    loadFromSettings,
    handleSaveJavaArguments,
    handleJavaRamChange,
    handleJavaInitialRamChange,
    handleJavaGcModeChange,
    handleJavaRuntimeModeChange,
    handleCustomJavaPathSave,
    handleBrowseCustomJavaPath,
    // Utilities
    formatRamLabel,
  };
}
