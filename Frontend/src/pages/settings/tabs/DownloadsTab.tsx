import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Info, Power, Plus, Trash2, Loader2, Globe, AlertCircle, RefreshCw, X, Check } from 'lucide-react';
import { SettingsToggleCard, MirrorSpeedCard } from '@/components/ui/Controls';
import { MirrorInfo } from '@/lib/ipc';
import type { MirrorSpeedResult } from '@/hooks/useMirrorSpeedTests';
import { ipc } from '@/lib/ipc';
import { motion, AnimatePresence } from 'framer-motion';

interface MirrorState {
  result: MirrorSpeedResult | null;
  isTesting: boolean;
}

interface DownloadsTabProps {
  hasOfficialAccount: boolean;
  launchAfterDownload: boolean;
  setLaunchAfterDownload: (v: boolean) => void;
  // Official CDN
  officialSpeedTest: MirrorSpeedResult | null;
  isOfficialTesting: boolean;
  handleTestOfficialSpeed: (force?: boolean) => void;
  // Dynamic mirrors
  mirrors: MirrorInfo[];
  mirrorStates: Record<string, MirrorState>;
  isLoading: boolean;
  testMirror: (mirrorId: string, forceRefresh?: boolean) => void;
  // Mirror management
  addMirror: (url: string, headers?: string) => Promise<boolean>;
  deleteMirror: (mirrorId: string) => Promise<boolean>;
  toggleMirror: (mirrorId: string, enabled: boolean) => Promise<boolean>;
  refreshMirrors: () => void;
  isAdding: boolean;
  addError: string | null;
  setAddError: (error: string | null) => void;
}

export const DownloadsTab: React.FC<DownloadsTabProps> = ({
  hasOfficialAccount,
  launchAfterDownload,
  setLaunchAfterDownload,
  officialSpeedTest,
  isOfficialTesting,
  handleTestOfficialSpeed,
  mirrors,
  mirrorStates,
  isLoading,
  testMirror,
  addMirror,
  deleteMirror,
  toggleMirror,
  refreshMirrors,
  isAdding,
  addError,
  setAddError,
}) => {
  const { t } = useTranslation();
  const [showAddForm, setShowAddForm] = useState(false);
  const [newMirrorUrl, setNewMirrorUrl] = useState('');
  const [newMirrorHeaders, setNewMirrorHeaders] = useState('');

  const handleAddMirror = async () => {
    if (!newMirrorUrl.trim()) return;
    
    const success = await addMirror(newMirrorUrl.trim(), newMirrorHeaders.trim() || undefined);
    if (success) {
      setNewMirrorUrl('');
      setNewMirrorHeaders('');
      setShowAddForm(false);
    }
  };

  const handleCloseForm = () => {
    setShowAddForm(false);
    setNewMirrorUrl('');
    setNewMirrorHeaders('');
    setAddError(null);
  };

  return (
    <div className="space-y-6">
      {/* Info Note */}
      <div className="p-4 rounded-xl bg-blue-500/10 border border-blue-500/20 flex items-start gap-3">
        <Info size={18} className="text-blue-400 flex-shrink-0 mt-0.5" />
        <div>
          <p className="text-sm text-blue-400 font-medium">{t('settings.downloads.howDownloadsWork')}</p>
          <p className="text-xs text-blue-400/70 mt-1">{t('settings.downloads.howDownloadsWorkDescription')}</p>
        </div>
      </div>

      {/* Launch After Download */}
      <SettingsToggleCard
        icon={<Power size={16} className="text-white opacity-70" />}
        title={t('settings.downloads.launchAfterDownload')}
        description={t('settings.downloads.launchAfterDownloadHint')}
        checked={launchAfterDownload}
        onCheckedChange={async (v) => {
          setLaunchAfterDownload(v);
          await ipc.settings.update({ launchAfterDownload: v });
        }}
      />

      {/* Official Source Card */}
      {hasOfficialAccount && (
        <MirrorSpeedCard
          name="Hytale Official"
          description={t('settings.downloads.officialSourceHint')}
          hostname="cdn.hytale.com"
          speedTest={officialSpeedTest}
          isTesting={isOfficialTesting}
          onTest={() => handleTestOfficialSpeed(true)}
          testLabel={t('settings.downloads.testSpeed')}
          testingLabel={t('settings.downloads.testing')}
          unavailableLabel={t('settings.downloads.unavailable')}
        />
      )}

      {/* Community Mirrors Section */}
      <div className="rounded-2xl glass-control-solid overflow-hidden">
        {/* Section Header */}
        <div className="flex items-center justify-between p-4 border-b border-white/[0.06]">
          <div className="flex items-center gap-3">
            <div className="w-8 h-8 rounded-lg bg-white/[0.06] flex items-center justify-center">
              <Globe size={16} className="text-white opacity-70" />
            </div>
            <div>
              <h3 className="text-sm font-medium text-white">{t('settings.downloads.communityMirrors', 'Community Mirrors')}</h3>
              <p className="text-xs text-white/40">{t('settings.downloads.mirrorsDescription', 'Alternative download sources for game files')}</p>
            </div>
          </div>
          
          {/* Button Group */}
          <div className="flex rounded-full overflow-hidden glass-control-solid border border-white/[0.06]">
            <button
              onClick={() => { setShowAddForm(true); setAddError(null); }}
              disabled={showAddForm}
              title={t('settings.downloads.addMirror', 'Add Mirror')}
              className="h-9 w-9 flex items-center justify-center text-white/60 hover:text-white hover:bg-white/[0.06] transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
            >
              <Plus size={16} />
            </button>
            <div className="w-px bg-white/[0.08]" />
            <button
              onClick={refreshMirrors}
              disabled={isLoading}
              title={t('settings.downloads.refreshMirrors', 'Refresh mirrors')}
              className="h-9 w-9 flex items-center justify-center text-white/60 hover:text-white hover:bg-white/[0.06] transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
            >
              <RefreshCw size={16} className={isLoading ? 'animate-spin' : ''} />
            </button>
          </div>
        </div>

        {/* Add Mirror Form - Animated */}
        <AnimatePresence>
          {showAddForm && (
            <motion.div
              initial={{ height: 0, opacity: 0 }}
              animate={{ height: 'auto', opacity: 1 }}
              exit={{ height: 0, opacity: 0 }}
              transition={{ duration: 0.2, ease: 'easeInOut' }}
              className="overflow-hidden"
            >
              <div className="p-4 bg-white/[0.02] border-b border-white/[0.06] space-y-3">
                {/* URL Input with Buttons */}
                <div className="flex items-center gap-2">
                  <input
                    type="text"
                    value={newMirrorUrl}
                    onChange={(e) => { setNewMirrorUrl(e.target.value); setAddError(null); }}
                    placeholder="https://example.com"
                    className="flex-1 h-10 px-4 text-sm rounded-xl bg-[#1c1c1e] border border-white/[0.08] text-white placeholder-white/30 focus:border-white/20 focus:outline-none transition-colors"
                    disabled={isAdding}
                    onKeyDown={(e) => e.key === 'Enter' && !isAdding && newMirrorUrl.trim() && handleAddMirror()}
                    autoFocus
                  />
                  
                  {/* Action Button Group */}
                  <div className="flex rounded-full overflow-hidden glass-control-solid flex-shrink-0">
                    <button
                      onClick={handleCloseForm}
                      disabled={isAdding}
                      className="h-10 w-10 flex items-center justify-center text-white/60 hover:text-white hover:bg-white/5 transition-colors disabled:opacity-40 disabled:cursor-not-allowed border-r border-white/[0.08]"
                      title={t('common.cancel', 'Cancel')}
                    >
                      <X size={16} />
                    </button>
                    <button
                      onClick={handleAddMirror}
                      disabled={isAdding || !newMirrorUrl.trim()}
                      className="h-10 w-10 flex items-center justify-center text-emerald-400 hover:bg-emerald-500/10 transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
                      title={t('settings.downloads.addMirrorBtn', 'Add Mirror')}
                    >
                      {isAdding ? (
                        <Loader2 size={16} className="animate-spin" />
                      ) : (
                        <Check size={16} />
                      )}
                    </button>
                  </div>
                </div>

                {/* Custom Headers Input */}
                <div className="space-y-1.5">
                  <input
                    type="text"
                    value={newMirrorHeaders}
                    onChange={(e) => setNewMirrorHeaders(e.target.value)}
                    placeholder={t('settings.downloads.headersPlaceholder', 'Custom headers (optional): Authorization="Bearer token" User-Agent="{hytaleAgent}"')}
                    className="w-full h-10 px-4 rounded-xl bg-[#1c1c1e] border border-white/[0.08] text-white placeholder-white/30 focus:border-white/20 focus:outline-none transition-colors font-mono text-xs"
                    disabled={isAdding}
                  />
                  <p className="text-[10px] text-white/30 px-1">
                    {t('settings.downloads.headersHint', 'Format: header=value or header="value with spaces". Use {hytaleAgent} for official User-Agent.')}
                  </p>
                </div>
                
                {/* Error Message */}
                <AnimatePresence>
                  {addError && (
                    <motion.div
                      initial={{ height: 0, opacity: 0 }}
                      animate={{ height: 'auto', opacity: 1 }}
                      exit={{ height: 0, opacity: 0 }}
                      transition={{ duration: 0.15 }}
                      className="overflow-hidden"
                    >
                      <div className="flex items-start gap-2 p-3 rounded-xl bg-red-500/10 border border-red-500/20">
                        <AlertCircle size={14} className="text-red-400 flex-shrink-0 mt-0.5" />
                        <p className="text-xs text-red-400">{addError}</p>
                      </div>
                    </motion.div>
                  )}
                </AnimatePresence>
                
                {/* Hint */}
                <p className="text-xs text-white/40 mt-2">
                  {t('settings.downloads.mirrorUrlHint', 'Enter the base URL of a Hytale mirror. The launcher will attempt to auto-detect the configuration.')}
                </p>
              </div>
            </motion.div>
          )}
        </AnimatePresence>

        {/* Mirrors Content */}
        <div className="p-4">
          {/* Loading State */}
          {isLoading && (
            <div className="flex items-center justify-center py-8">
              <Loader2 size={24} className="animate-spin text-white opacity-40" />
            </div>
          )}

          {/* No Mirrors State */}
          {!isLoading && mirrors.length === 0 && !showAddForm && (
            <div className="py-8 text-center">
              <Globe size={32} className="mx-auto text-white opacity-20 mb-3" />
              <p className="text-sm text-white/60 mb-1">
                {t('settings.downloads.noMirrors', 'No mirrors configured')}
              </p>
              <p className="text-xs text-white/40">
                {t('settings.downloads.noMirrorsHint', 'Add a mirror to download game files without an official account')}
              </p>
            </div>
          )}

          {/* Mirror Cards */}
          {!isLoading && mirrors.length > 0 && (
            <div className="space-y-3">
              {mirrors.map((mirror) => {
                const state = mirrorStates[mirror.id] || { result: null, isTesting: false };
                return (
                  <div key={mirror.id} className="relative">
                    <div
                      className={`p-3 rounded-xl border transition-colors ${!mirror.enabled ? 'opacity-50' : ''}`}
                      style={{
                        backgroundColor: '#151515',
                        borderColor: 'rgba(255,255,255,0.08)',
                      }}
                    >
                      <div className="flex items-center justify-between">
                        <div className="flex items-center gap-3">
                          <div
                            className="w-10 h-10 rounded-lg flex items-center justify-center flex-shrink-0"
                            style={{ backgroundColor: 'rgba(255,255,255,0.06)' }}
                          >
                            <Globe size={20} className="text-white opacity-60" />
                          </div>
                          <div>
                            <div className="text-white text-sm font-medium">{mirror.name}</div>
                            {mirror.description && <div className="text-[11px] text-white/40 mt-0.5">{mirror.description}</div>}
                            <code className="text-[10px] text-white/30 mt-1 block font-mono">{mirror.hostname}</code>
                          </div>
                        </div>
                        
                        <div className="flex items-center gap-2">
                          {/* Speed Test Result Badge */}
                          {state.result && !state.isTesting && (
                            <div
                              className={`flex items-center gap-2 px-3 h-9 rounded-full text-xs ${
                                state.result.isAvailable
                                  ? 'bg-green-500/20 text-green-400'
                                  : 'bg-red-500/20 text-red-400'
                              }`}
                            >
                              {state.result.isAvailable ? (
                                <>
                                  <span>{state.result.pingMs}ms</span>
                                  <span>•</span>
                                  <span>{state.result.speedMBps > 0 ? `${state.result.speedMBps.toFixed(1)} MB/s` : '—'}</span>
                                </>
                              ) : (
                                <span>{t('settings.downloads.unavailable')}</span>
                              )}
                            </div>
                          )}
                          
                          {/* Unified Button Group */}
                          <div className="flex rounded-full overflow-hidden glass-control-solid">
                            {/* Test Speed Button */}
                            <button
                              onClick={() => testMirror(mirror.id, true)}
                              disabled={state.isTesting}
                              className="h-9 px-3 flex items-center justify-center text-white/60 hover:text-white hover:bg-white/5 transition-colors disabled:opacity-40 disabled:cursor-not-allowed border-r border-white/[0.08]"
                              title={t('settings.downloads.testSpeed')}
                            >
                              {state.isTesting ? (
                                <Loader2 size={14} className="animate-spin" />
                              ) : (
                                <Info size={14} />
                              )}
                            </button>
                            
                            {/* Toggle Enable/Disable Button */}
                            <button
                              onClick={() => toggleMirror(mirror.id, !mirror.enabled)}
                              className={`h-9 px-3 flex items-center justify-center transition-colors border-r border-white/[0.08] ${
                                mirror.enabled 
                                  ? 'text-amber-400 hover:bg-amber-500/10' 
                                  : 'text-emerald-400 hover:bg-emerald-500/10'
                              }`}
                              title={mirror.enabled ? t('settings.downloads.disable', 'Disable') : t('settings.downloads.enable', 'Enable')}
                            >
                              <Power size={14} />
                            </button>
                            
                            {/* Delete Button */}
                            <button
                              onClick={() => deleteMirror(mirror.id)}
                              className="h-9 px-3 flex items-center justify-center text-red-400 hover:bg-red-500/10 transition-colors"
                              title={t('settings.downloads.deleteMirror', 'Delete mirror')}
                            >
                              <Trash2 size={14} />
                            </button>
                          </div>
                        </div>
                      </div>
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </div>
    </div>
  );
};
