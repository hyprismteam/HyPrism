import { useCallback } from 'react';
import { ipc, invoke, type SaveInfo } from '@/lib/ipc';
import type { InstalledVersionInfo } from '@/types';

/**
 * Exports an instance archive to a user-chosen location.
 * @param instanceId - The ID of the instance to export.
 * @returns The path of the exported archive, or an empty string on failure.
 */
export const exportInstance = async (instanceId: string): Promise<string> => {
  try {
    return await ipc.instance.export({ instanceId });
  } catch (e) {
    console.warn('[IPC] ExportInstance:', e);
    return '';
  }
};

/**
 * Permanently deletes a game instance and its files.
 * @param instanceId - The ID of the instance to delete.
 * @returns `true` on success, `false` on failure.
 */
export const deleteInstance = async (instanceId: string): Promise<boolean> => {
  try {
    return await ipc.instance.delete({ instanceId });
  } catch (e) {
    console.warn('[IPC] DeleteGame:', e);
    return false;
  }
};

/**
 * Opens the instance root folder in the native file explorer.
 * @param instanceId - The ID of the instance whose folder to open.
 */
export const openInstanceFolder = (instanceId: string): void => {
  ipc.instance.openFolder({ instanceId });
};

/**
 * Prompts the user to select a `.zip` archive and imports it as a new instance.
 * @returns `true` if the import succeeded, `false` otherwise.
 */
export const importInstanceFromZip = async (): Promise<boolean> => {
  try {
    return await ipc.instance.import();
  } catch (e) {
    console.warn('[IPC] ImportInstanceFromZip:', e);
    return false;
  }
};

/**
 * Retrieves the user-configured custom instances directory.
 * @returns The directory path, or an empty string if not configured.
 */
export const getCustomInstanceDir = async (): Promise<string> => {
  return (await ipc.settings.get()).dataDirectory ?? '';
};

/**
 * Lists all mods installed within the specified instance.
 * @param instanceId - The target instance ID.
 * @returns An array of raw mod objects, or an empty array on failure.
 */
export const getInstanceInstalledMods = async (instanceId: string): Promise<unknown[]> => {
  try {
    return await ipc.mods.installed({ instanceId });
  } catch (e) {
    console.warn('[IPC] GetInstanceInstalledMods:', e);
    return [];
  }
};

/**
 * Removes a single mod from the specified instance.
 * @param modId - The mod identifier.
 * @param instanceId - The target instance ID.
 * @returns `true` on success, `false` otherwise.
 */
export const uninstallInstanceMod = async (modId: string, instanceId: string): Promise<boolean> => {
  try {
    return await ipc.mods.uninstall({ modId, instanceId });
  } catch (e) {
    console.warn('[IPC] UninstallInstanceMod:', e);
    return false;
  }
};

/**
 * Opens the `UserData/Mods` folder of the specified instance in the native file explorer.
 * @param instanceId - The ID of the instance.
 */
export const openInstanceModsFolder = (instanceId: string): void => {
  ipc.instance.openModsFolder({ instanceId });
};

/**
 * Checks for available updates for all mods installed in the given instance.
 * @param instanceId - The target instance ID.
 * @returns An array of mod objects that have updates available, or an empty array on failure.
 */
export const checkInstanceModUpdates = async (instanceId: string): Promise<unknown[]> => {
  try {
    return await ipc.mods.checkUpdates({ instanceId });
  } catch (e) {
    console.warn('[IPC] CheckInstanceModUpdates:', e);
    return [];
  }
};

/**
 * Retrieves the list of world save folders for the given instance.
 * @param instanceId - The target instance ID.
 * @returns An array of {@link SaveInfo} objects, or an empty array on failure.
 */
export const getInstanceSaves = async (instanceId: string): Promise<SaveInfo[]> => {
  try {
    return await ipc.instance.saves({ instanceId });
  } catch (e) {
    console.warn('[IPC] GetInstanceSaves:', e);
    return [];
  }
};

/**
 * Opens a specific world save folder in the native file explorer.
 * @param instanceId - The instance that owns the save.
 * @param saveName - The name of the save folder.
 */
export const openSaveFolder = (instanceId: string, saveName: string): void => {
  ipc.instance.openSaveFolder({ instanceId, saveName });
};

/**
 * Permanently deletes a world save folder.
 * @param instanceId - The instance that owns the save.
 * @param saveName - The name of the save folder to delete.
 * @returns `true` on success, `false` otherwise.
 */
export const deleteSaveFolder = async (instanceId: string, saveName: string): Promise<boolean> => {
  try {
    return await invoke<boolean>('hyprism:instance:deleteSave', { instanceId, saveName });
  } catch (e) {
    console.warn('[IPC] DeleteSaveFolder:', e);
    return false;
  }
};

/**
 * Retrieves the URL of the custom icon image for the given instance.
 * @param instanceId - The target instance ID.
 * @returns A `file://` URL with a cache-busting query parameter, or `null` if no icon is set.
 */
export const getInstanceIcon = async (instanceId: string): Promise<string | null> => {
  try {
    return await ipc.instance.getIcon({ instanceId });
  } catch (e) {
    console.warn('[IPC] GetInstanceIcon:', e);
    return null;
  }
};

/**
 * Hook that provides instance management action handlers with integrated
 * message-state management and instance-list refresh.
 *
 * @param setMessage - Callback to display a status message to the user.
 * @param loadInstances - Callback that reloads the instance list from the backend.
 * @param t - i18next translation function.
 * @returns Action handlers for export, delete, import, and folder-open operations.
 */
export function useInstanceActions(
  setMessage: (msg: { type: 'success' | 'error'; text: string } | null) => void,
  loadInstances: () => Promise<void>,
  t: (key: string, params?: Record<string, unknown>) => string
) {
  const handleExport = useCallback(async (inst: InstalledVersionInfo, setExportingInstance: (id: string | null) => void) => {
    setExportingInstance(inst.id);
    try {
      const result = await exportInstance(inst.id);
      if (result) {
        setMessage({ type: 'success', text: t('instances.exportedSuccess') });
      }
    } catch {
      setMessage({ type: 'error', text: t('instances.exportFailed') });
    }
    setExportingInstance(null);
    setTimeout(() => setMessage(null), 3000);
  }, [setMessage, t]);

  const handleDelete = useCallback(async (
    inst: InstalledVersionInfo,
    selectedInstance: InstalledVersionInfo | null,
    setSelectedInstance: (inst: InstalledVersionInfo | null) => void,
    setInstanceToDelete: (inst: InstalledVersionInfo | null) => void,
    onInstanceDeleted?: () => void
  ) => {
    try {
      await deleteInstance(inst.id);
      setInstanceToDelete(null);
      if (selectedInstance?.id === inst.id) {
        setSelectedInstance(null);
      }
      await loadInstances();
      onInstanceDeleted?.();
      setMessage({ type: 'success', text: t('instances.deleted') });
      setTimeout(() => setMessage(null), 3000);
    } catch {
      setMessage({ type: 'error', text: t('instances.deleteFailed') });
    }
  }, [loadInstances, setMessage, t]);

  const handleImport = useCallback(async (setIsImporting: (v: boolean) => void) => {
    setIsImporting(true);
    try {
      const result = await importInstanceFromZip();
      if (result) {
        setMessage({ type: 'success', text: t('instances.importedSuccess') });
        await loadInstances();
      }
    } catch {
      setMessage({ type: 'error', text: t('instances.importFailed') });
    }
    setIsImporting(false);
    setTimeout(() => setMessage(null), 3000);
  }, [loadInstances, setMessage, t]);

  return {
    handleExport,
    handleDelete,
    handleImport,
    openFolder: openInstanceFolder,
    openModsFolder: openInstanceModsFolder,
  };
}
