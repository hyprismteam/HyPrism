import { useState, useEffect } from 'react';
import { on } from '@/lib/ipc';

/**
 * Listens for launcher update availability events and exposes the pending
 * update asset object.
 *
 * @returns `{ updateAsset }` — the update asset payload when an update is available,
 *   or `null` if no update has been announced yet.
 */
export function useLauncherUpdate() {
  const [updateAsset, setUpdateAsset] = useState<any>(null);

  useEffect(() => {
    return on('hyprism:update:available', (asset: any) => {
      setUpdateAsset(asset);
    });
  }, []);

  return { updateAsset };
}
