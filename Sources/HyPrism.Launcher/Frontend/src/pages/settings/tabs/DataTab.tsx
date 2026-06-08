import React from 'react';
import { useTranslation } from 'react-i18next';
import { FolderOpen, Trash2, RotateCcw, ExternalLink, AlertTriangle } from 'lucide-react';
import { Button } from '@/components/ui/Controls';
import { openUrl } from '@/utils/openUrl';

interface DataTabProps {
  gc: string;
  isGameRunning?: boolean;
  instanceDir: string;
  launcherDataDir: string;
  handleBrowseInstanceDir: () => void;
  handleResetInstanceDir: () => void;
  handleOpenLauncherFolder: () => void;
  setShowDeleteConfirm: (v: boolean) => void;
}

export const DataTab: React.FC<DataTabProps> = ({
  gc,
  isGameRunning = false,
  instanceDir,
  launcherDataDir,
  handleBrowseInstanceDir,
  handleResetInstanceDir,
  handleOpenLauncherFolder,
  setShowDeleteConfirm,
}) => {
  const { t } = useTranslation();

  return (
    <div className="space-y-6">
      {/* Game Running Warning */}
      {isGameRunning && (
        <div className="p-4 rounded-xl bg-yellow-500/10 border border-yellow-500/20 flex items-center gap-3">
          <AlertTriangle size={20} className="text-yellow-400 flex-shrink-0" />
          <p className="text-sm text-yellow-400">{t('settings.dataSettings.gameRunningWarning')}</p>
        </div>
      )}

      {/* Instance Folder */}
      <div>
        <label className="block text-sm text-white/60 mb-2">{t('settings.dataSettings.instanceFolder')}</label>
        <div className="flex gap-2">
          <input
            type="text"
            value={instanceDir}
            readOnly
            className={`flex-1 h-12 px-4 rounded-xl ${gc} text-white text-sm focus:outline-none cursor-default`}
          />
          <div className={`flex rounded-full overflow-hidden ${gc}`}>
            <button
              onClick={handleResetInstanceDir}
              disabled={isGameRunning}
              className="h-12 px-4 flex items-center justify-center text-white/60 hover:text-white hover:bg-white/5 transition-colors disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-transparent disabled:hover:text-white/60"
              title={t('settings.dataSettings.resetToDefault')}
            >
              <RotateCcw size={18} />
              <span className="ml-2 text-sm">{t('common.reset')}</span>
            </button>
            <div className="w-px bg-white/10" />
            <button
              onClick={handleBrowseInstanceDir}
              disabled={isGameRunning}
              className="h-12 px-4 flex items-center justify-center text-white/60 hover:text-white hover:bg-white/5 transition-colors disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-transparent disabled:hover:text-white/60"
              title={t('common.browse')}
            >
              <FolderOpen size={18} />
              <span className="ml-2 text-sm">{t('common.select')}</span>
            </button>
            <div className="w-px bg-white/10" />
            <button
              onClick={() => openUrl(`file://${instanceDir}`)}
              className="h-12 px-4 flex items-center justify-center text-white/60 hover:text-white hover:bg-white/5 transition-colors"
              title={t('common.openFolder')}
            >
              <ExternalLink size={18} />
              <span className="ml-2 text-sm">{t('settings.dataSettings.open')}</span>
            </button>
          </div>
        </div>
      </div>

      {/* Launcher Data Folder */}
      <div>
        <label className="block text-sm text-white/60 mb-2">{t('settings.dataSettings.launcherDataFolder')}</label>
        <div className="flex gap-2">
          <input
            type="text"
            value={launcherDataDir}
            readOnly
            className={`flex-1 h-12 px-4 rounded-xl ${gc} text-white text-sm focus:outline-none cursor-default`}
          />
          <div className={`flex rounded-full overflow-hidden ${gc}`}>
            <button
              onClick={() => openUrl(`file://${launcherDataDir}`)}
              className="h-12 px-4 flex items-center justify-center text-white/60 hover:text-white hover:bg-white/5 transition-colors"
              title={t('common.openFolder')}
            >
              <ExternalLink size={18} />
              <span className="ml-2 text-sm">{t('settings.dataSettings.open')}</span>
            </button>
          </div>
        </div>
      </div>

      {/* Launcher Folder Actions */}
      <div className="space-y-3">
        <Button onClick={handleOpenLauncherFolder} className="w-full h-12 justify-start">
          <FolderOpen size={18} />
          {t('settings.dataSettings.openLauncherFolder')}
        </Button>

        <Button variant="danger" onClick={() => setShowDeleteConfirm(true)} className="w-full h-12 justify-start">
          <Trash2 size={18} />
          {t('settings.dataSettings.deleteAllData')}
        </Button>
      </div>
    </div>
  );
};
