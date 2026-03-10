import React from 'react';
import { useTranslation } from 'react-i18next';
import { 
  Globe, Map, Image, Clock, FolderOpen, Trash2, 
  RefreshCw, Loader2
} from 'lucide-react';
import { useAccentColor } from '@/contexts/AccentColorContext';
import { type SaveInfo } from '@/lib/ipc';
import { IconButton } from '@/components/ui/Controls';
import { formatBytes } from '@/utils/format';
import type { InstalledVersionInfo } from '@/types';
import { openSaveFolder, deleteSaveFolder } from '@/hooks';

export interface WorldsTabProps {
  selectedInstance: InstalledVersionInfo | null;
  saves: SaveInfo[];
  isLoadingSaves: boolean;
  loadSaves: () => Promise<void>;
  setMessage: (msg: { type: 'success' | 'error'; text: string } | null) => void;
}

export const WorldsTab: React.FC<WorldsTabProps> = ({
  selectedInstance,
  saves,
  isLoadingSaves,
  loadSaves,
  setMessage,
}) => {
  const { t } = useTranslation();
  const { accentColor } = useAccentColor();

  const handleOpenSaveFolder = (saveName: string) => {
    if (!selectedInstance) return;
    openSaveFolder(selectedInstance.id, saveName);
  };

  const handleDeleteSave = async (e: React.MouseEvent, saveName: string) => {
    e.preventDefault();
    e.stopPropagation();
    if (!selectedInstance) return;

    const ok = await deleteSaveFolder(selectedInstance.id, saveName);
    if (ok) {
      setMessage({ type: 'success', text: 'World deleted' });
      await loadSaves();
    } else {
      setMessage({ type: 'error', text: 'Failed to delete world' });
    }
    setTimeout(() => setMessage(null), 3000);
  };

  if (!selectedInstance) return null;

  return (
    <div className="absolute inset-0 flex flex-col">
      {/* Header */}
      <div className="p-4 border-b border-white/10 flex items-center justify-between">
        <h3 className="text-white font-medium flex items-center gap-2">
          <Globe size={18} />
          {t('instances.saves')}
        </h3>
        <div className="flex items-center gap-2">
          <IconButton onClick={loadSaves} disabled={isLoadingSaves} title={t('common.refresh')}>
            <RefreshCw size={16} className={isLoadingSaves ? 'animate-spin' : ''} />
          </IconButton>
        </div>
      </div>

      {/* Saves List */}
      <div className="flex-1 overflow-y-auto p-4">
        {isLoadingSaves ? (
          <div className="flex items-center justify-center py-12">
            <Loader2 size={32} className="animate-spin" style={{ color: accentColor }} />
          </div>
        ) : saves.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-12">
            <Map size={48} className="mb-4 text-white opacity-50" />
            <p className="text-lg font-medium text-white/30">{t('instances.noSaves')}</p>
            <p className="text-sm mt-1 text-white/30">{t('instances.noSavesHint')}</p>
          </div>
        ) : (
          <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-4">
            {saves.map((save) => (
              <div
                key={save.name}
                onClick={() => handleOpenSaveFolder(save.name)}
                className="group relative rounded-xl overflow-hidden border border-white/10 hover:border-white/20 transition-all bg-white/5 hover:bg-white/10 cursor-pointer"
              >
                {/* Preview Image */}
                <div className="aspect-video w-full bg-black/40 flex items-center justify-center overflow-hidden">
                  {save.previewPath ? (
                    <img
                      src={save.previewPath}
                      alt={save.name}
                      className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-300"
                      onError={(e) => {
                        (e.target as HTMLImageElement).style.display = 'none';
                        (e.target as HTMLImageElement).nextElementSibling?.classList.remove('hidden');
                      }}
                    />
                  ) : null}
                  <div className={`flex items-center justify-center ${save.previewPath ? 'hidden' : ''}`}>
                    <Image size={32} className="text-white opacity-20" />
                  </div>
                </div>

                {/* Save Info */}
                <div className="p-3">
                  <p className="text-white font-medium text-sm truncate">{save.name}</p>
                  <div className="flex items-center justify-between mt-1 text-xs text-white/40">
                    {save.lastModified && (
                      <span className="flex items-center gap-1">
                        <Clock size={10} />
                        {new Date(save.lastModified).toLocaleDateString()}
                      </span>
                    )}
                    {save.sizeBytes && (
                      <span>{formatBytes(save.sizeBytes)}</span>
                    )}
                  </div>
                </div>

                {/* Hover Actions - Small icons in top-right corner */}
                <div className="absolute top-2 right-2 flex gap-1.5 opacity-0 group-hover:opacity-100 transition-opacity">
                  <button
                    onClick={(e) => {
                      e.stopPropagation();
                      handleOpenSaveFolder(save.name);
                    }}
                    className="p-1.5 rounded-lg bg-black/60 hover:bg-black/80 border border-white/10 hover:border-white/20 text-white/70 hover:text-white transition-all"
                    title={t('common.openFolder')}
                  >
                    <FolderOpen size={14} />
                  </button>
                  <button
                    onClick={(e) => handleDeleteSave(e, save.name)}
                    className="p-1.5 rounded-lg bg-black/60 hover:bg-red-500/80 border border-white/10 hover:border-red-400/50 text-white/70 hover:text-white transition-all"
                    title={t('common.delete')}
                  >
                    <Trash2 size={14} />
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
};

export default WorldsTab;
