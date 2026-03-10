import { useState, useCallback, useEffect } from 'react';
import { ipc, MirrorInfo, MirrorSpeedTestResult } from '@/lib/ipc';

/** Re-export of {@link MirrorSpeedTestResult} under a shorter alias for convenience. */
export type { MirrorSpeedTestResult as MirrorSpeedResult };

/**
 * Per-mirror testing state: the last speed-test result and whether a test is currently running.
 */
interface MirrorState {
  result: MirrorSpeedTestResult | null;
  isTesting: boolean;
}

/**
 * Manages the dynamic mirror list and per-mirror speed-test state.
 * Supports adding, removing, toggling, and testing mirrors against the backend.
 *
 * @returns Mirror list, speed-test state, and management/test handlers.
 */
export function useMirrorSpeedTests() {
  const [mirrors, setMirrors] = useState<MirrorInfo[]>([]);
  const [mirrorStates, setMirrorStates] = useState<Record<string, MirrorState>>({});
  const [officialState, setOfficialState] = useState<MirrorState>({ result: null, isTesting: false });
  const [isLoading, setIsLoading] = useState(true);
  const [isAdding, setIsAdding] = useState(false);
  const [addError, setAddError] = useState<string | null>(null);

  // Load mirrors from backend
  const loadMirrors = useCallback(async () => {
    try {
      setIsLoading(true);
      const loadedMirrors = await ipc.settings.getMirrors();
      setMirrors(loadedMirrors);
      
      // Initialize states for new mirrors
      setMirrorStates(prev => {
        const newStates: Record<string, MirrorState> = {};
        for (const mirror of loadedMirrors) {
          newStates[mirror.id] = prev[mirror.id] || { result: null, isTesting: false };
        }
        return newStates;
      });
    } catch (err) {
      console.error('Failed to load mirrors:', err);
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    loadMirrors();
  }, [loadMirrors]);

  const createErrorResult = (mirrorId: string, mirrorName: string): MirrorSpeedTestResult => ({
    mirrorId,
    mirrorUrl: '',
    mirrorName,
    pingMs: -1,
    speedMBps: 0,
    isAvailable: false,
    testedAt: new Date().toISOString(),
  });

  const testMirror = useCallback(async (mirrorId: string, forceRefresh = false) => {
    setMirrorStates(prev => ({
      ...prev,
      [mirrorId]: { ...prev[mirrorId], isTesting: true },
    }));

    try {
      const result = await ipc.settings.testMirrorSpeed({ mirrorId, forceRefresh });
      setMirrorStates(prev => ({
        ...prev,
        [mirrorId]: { result, isTesting: false },
      }));
    } catch (err) {
      console.error(`${mirrorId} speed test failed:`, err);
      const mirror = mirrors.find(m => m.id === mirrorId);
      setMirrorStates(prev => ({
        ...prev,
        [mirrorId]: { result: createErrorResult(mirrorId, mirror?.name || mirrorId), isTesting: false },
      }));
    }
  }, [mirrors]);

  const testOfficial = useCallback(async (forceRefresh = false) => {
    setOfficialState(prev => ({ ...prev, isTesting: true }));
    try {
      const result = await ipc.settings.testOfficialSpeed({ forceRefresh });
      setOfficialState({ result, isTesting: false });
    } catch (err) {
      console.error('Official speed test failed:', err);
      setOfficialState({ result: createErrorResult('official', 'Hytale'), isTesting: false });
    }
  }, []);

  const addMirror = useCallback(async (url: string, headers?: string): Promise<boolean> => {
    setIsAdding(true);
    setAddError(null);
    try {
      const result = await ipc.settings.addMirror({ url, headers });
      if (result.success) {
        await loadMirrors();
        return true;
      } else {
        setAddError(result.error || 'Failed to add mirror');
        return false;
      }
    } catch (err) {
      console.error('Failed to add mirror:', err);
      setAddError(err instanceof Error ? err.message : 'Unknown error');
      return false;
    } finally {
      setIsAdding(false);
    }
  }, [loadMirrors]);

  const deleteMirror = useCallback(async (mirrorId: string): Promise<boolean> => {
    try {
      const result = await ipc.settings.deleteMirror({ mirrorId });
      if (result.success) {
        await loadMirrors();
        return true;
      }
      return false;
    } catch (err) {
      console.error('Failed to delete mirror:', err);
      return false;
    }
  }, [loadMirrors]);

  const toggleMirror = useCallback(async (mirrorId: string, enabled: boolean): Promise<boolean> => {
    try {
      const result = await ipc.settings.toggleMirror({ mirrorId, enabled });
      if (result.success) {
        await loadMirrors();
        return true;
      }
      return false;
    } catch (err) {
      console.error('Failed to toggle mirror:', err);
      return false;
    }
  }, [loadMirrors]);

  return {
    // Dynamic mirror list
    mirrors,
    mirrorStates,
    isLoading,
    
    // Official CDN
    officialResult: officialState.result,
    isOfficialTesting: officialState.isTesting,
    testOfficial,
    
    // Test functions
    testMirror,
    
    // Mirror management
    addMirror,
    deleteMirror,
    toggleMirror,
    isAdding,
    addError,
    setAddError,
    
    // Refresh
    refresh: loadMirrors,
  };
}
