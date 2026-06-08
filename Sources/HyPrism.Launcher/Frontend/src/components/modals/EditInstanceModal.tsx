import React, { useState, useEffect, useRef } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { useTranslation } from 'react-i18next';
import { X, Image, Loader2, ChevronDown, Check, GitBranch, AlertTriangle, Edit3, AlertCircle } from 'lucide-react';
import { useAccentColor } from '../../contexts/AccentColorContext';
import { Button, IconButton } from '@/components/ui/Controls';

import { ipc, invoke, VersionInfo } from '@/lib/ipc';
import { GameBranch } from '@/constants/enums';

interface EditInstanceModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSave?: () => void;
  // Instance data
  instanceId: string;
  initialName: string;
  initialIconUrl?: string;
  initialBranch?: string;
  initialVersion?: number;
}

export const EditInstanceModal: React.FC<EditInstanceModalProps> = ({
  isOpen,
  onClose,
  onSave,
  instanceId,
  initialName,
  initialIconUrl,
  initialBranch = GameBranch.RELEASE,
  initialVersion = 0,
}) => {
  const { t } = useTranslation();
  const { accentColor } = useAccentColor();

  // Form state
  const [customName, setCustomName] = useState<string>(initialName);
  const [iconFile, setIconFile] = useState<File | null>(null);
  const [iconPreview, setIconPreview] = useState<string | null>(initialIconUrl || null);
  const [isSaving, setIsSaving] = useState(false);

  // Version/Branch state
  const [selectedBranch, setSelectedBranch] = useState<string>(initialBranch);
  const [selectedVersion, setSelectedVersion] = useState<number>(initialVersion);
  const [isBranchOpen, setIsBranchOpen] = useState(false);
  const [isVersionOpen, setIsVersionOpen] = useState(false);
  const [availableVersions, setAvailableVersions] = useState<VersionInfo[]>([]);
  const [isLoadingVersions, setIsLoadingVersions] = useState(false);
  const [versionChanged, setVersionChanged] = useState(false);
  const [hasDownloadSources, setHasDownloadSources] = useState(true);

  const fileInputRef = useRef<HTMLInputElement>(null);
  const branchRef = useRef<HTMLDivElement>(null);
  const versionRef = useRef<HTMLDivElement>(null);
  const versionsRequestIdRef = useRef(0);

  // Reset state when modal opens
  useEffect(() => {
    if (isOpen) {
      setCustomName(initialName);
      setIconPreview(initialIconUrl || null);
      setIconFile(null);
      setIsSaving(false);
      setSelectedBranch(initialBranch);
      setSelectedVersion(initialVersion);
      setVersionChanged(false);
    }
  }, [isOpen, initialName, initialIconUrl, initialBranch, initialVersion]);

  // Load versions when branch changes
  useEffect(() => {
    const loadVersions = async () => {
      const requestId = ++versionsRequestIdRef.current;
      setIsLoadingVersions(true);
      setAvailableVersions([]);
      try {
        const response = await ipc.game.versionsWithSources({ branch: selectedBranch });
        if (requestId !== versionsRequestIdRef.current || !isOpen) return;

        // Track whether download sources are available (mirrors via versions list, or official account)
        setHasDownloadSources(!!response.hasOfficialAccount || (response.versions?.length ?? 0) > 0);

        // Filter out version 0 ("latest" placeholder) and keep only real versions.
        const versions = (response.versions || []).filter(v => v.version !== 0);
        setAvailableVersions(versions);

        // If current selection is "latest" (0) or no longer exists in this branch, select the newest real version.
        if (versions.length > 0) {
          setSelectedVersion((prev) => {
            if (prev === 0) return versions[0].version;
            if (!versions.some(v => v.version === prev)) return versions[0].version;
            return prev;
          });
        }
      } catch (err) {
        if (requestId !== versionsRequestIdRef.current || !isOpen) return;
        console.error('Failed to load versions:', err);
        setAvailableVersions([]);
        setHasDownloadSources(false);
      } finally {
        if (requestId === versionsRequestIdRef.current && isOpen) {
          setIsLoadingVersions(false);
        }
      }
    };

    if (isOpen) {
      loadVersions();
    }
  }, [selectedBranch, isOpen]);

  // Detect if version/branch was changed
  useEffect(() => {
    setVersionChanged(selectedBranch !== initialBranch || selectedVersion !== initialVersion);
  }, [selectedBranch, selectedVersion, initialBranch, initialVersion]);

  // Close dropdowns on outside click
  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (branchRef.current && !branchRef.current.contains(e.target as Node)) {
        setIsBranchOpen(false);
      }
      if (versionRef.current && !versionRef.current.contains(e.target as Node)) {
        setIsVersionOpen(false);
      }
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, []);

  const handleIconSelect = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) {
      setIconFile(file);
      const reader = new FileReader();
      reader.onloadend = () => {
        setIconPreview(reader.result as string);
      };
      reader.readAsDataURL(file);
    }
  };

  const handleSave = async () => {
    if (isSaving) return;
    setIsSaving(true);

    try {
      // Update name if changed
      if (customName !== initialName) {
        await invoke<boolean>('hyprism:instance:rename', {
          instanceId: instanceId,
          customName: customName?.trim() || null,
        });
      }

      // Handle icon upload if provided
      if (iconFile) {
        try {
          const base64 = await fileToBase64(iconFile);
          await invoke('hyprism:instance:setIcon', {
            instanceId: instanceId,
            iconBase64: base64
          });
        } catch (err) {
          console.warn('Failed to set icon:', err);
        }
      }

      // Handle version/branch change
      if (versionChanged) {
        await ipc.instance.changeVersion({
          instanceId: instanceId,
          branch: selectedBranch,
          version: selectedVersion,
        });
      }

      // Notify parent
      onSave?.();
      onClose();
    } catch (err) {
      console.error('Failed to save instance:', err);
      setIsSaving(false);
    }
  };

  const fileToBase64 = (file: File): Promise<string> => {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onloadend = () => {
        const base64 = (reader.result as string).split(',')[1];
        resolve(base64);
      };
      reader.onerror = reject;
      reader.readAsDataURL(file);
    });
  };

  if (!isOpen) return null;

  const branchLabel = selectedBranch === GameBranch.RELEASE
    ? t('main.release')
    : t('main.preRelease');

  return (
    <AnimatePresence>
      <motion.div
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        exit={{ opacity: 0 }}
        className="fixed inset-0 z-[200] flex items-center justify-center bg-[#0a0a0a]/90"
        onClick={(e) => e.target === e.currentTarget && onClose()}
      >
        <motion.div
          initial={{ opacity: 0, scale: 0.95, y: 20 }}
          animate={{ opacity: 1, scale: 1, y: 0 }}
          exit={{ opacity: 0, scale: 0.95, y: 20 }}
          className="w-full max-w-md mx-4 glass-panel-static-solid shadow-2xl"
        >
          {/* Header */}
          <div className="flex items-center justify-between p-5 border-b border-white/[0.06]">
            <div className="flex items-center gap-3">
              <div
                className="w-10 h-10 rounded-xl flex items-center justify-center"
                style={{ backgroundColor: `${accentColor}20` }}
              >
                <Edit3 size={20} style={{ color: accentColor }} />
              </div>
              <div>
                <h2 className="text-lg font-bold text-white">{t('instances.editInstance')}</h2>
                <p className="text-xs text-white/40">{t('instances.editInstanceHint')}</p>
              </div>
            </div>
            <IconButton variant="ghost" onClick={onClose}>
              <X size={20} />
            </IconButton>
          </div>

          {/* Content */}
          <div className="p-4 space-y-3">
            {/* Instance Name + Icon row */}
            <div className="flex items-end gap-3">
              {/* Icon Preview - cubic */}
              <div className="flex flex-col items-center gap-1 flex-shrink-0">
                <div
                  className="w-14 h-14 rounded-2xl border-2 border-dashed flex items-center justify-center overflow-hidden cursor-pointer hover:border-white/40 transition-colors"
                  style={{ borderColor: iconPreview ? accentColor : 'rgba(255,255,255,0.2)' }}
                  onClick={() => fileInputRef.current?.click()}
                >
                  {iconPreview ? (
                    <img src={iconPreview} alt="Instance icon" className="w-full h-full object-cover" />
                  ) : (
                    <Image size={22} className="text-white opacity-30" />
                  )}
                </div>
              </div>
              <input
                ref={fileInputRef}
                type="file"
                accept="image/png,image/jpeg,image/webp"
                className="hidden"
                onChange={handleIconSelect}
              />
              {/* Instance Name */}
              <div className="flex-1 space-y-1">
                <label className="text-xs text-white/50">{t('instances.instanceName')}</label>
                <input
                  type="text"
                  value={customName}
                  onChange={(e) => setCustomName(e.target.value)}
                  placeholder={t('instances.instanceNamePlaceholder')}
                  className="w-full h-10 px-3 rounded-xl bg-[#2c2c2e] border border-white/[0.06] text-white text-sm focus:outline-none focus:border-white/20 transition-colors"
                  maxLength={32}
                />
              </div>
            </div>

            {/* Branch & Version selectors side by side */}
            <div className="grid grid-cols-2 gap-3">
              {/* Branch Selector */}
              <div className="space-y-1">
                <label className="text-xs text-white/50">{t('common.branch')}</label>
                <div ref={branchRef} className="relative">
                  <button
                    onClick={() => {
                      setIsBranchOpen(!isBranchOpen);
                      setIsVersionOpen(false);
                    }}
                    className="w-full h-10 px-3 rounded-xl bg-[#2c2c2e] border border-white/[0.06] flex items-center justify-between text-white text-sm transition-colors hover:border-white/[0.12]"
                    style={{ borderColor: isBranchOpen ? `${accentColor}50` : undefined }}
                  >
                    <div className="flex items-center gap-2">
                      <GitBranch size={14} className="text-white opacity-40" />
                      <span className="font-medium">{branchLabel}</span>
                    </div>
                    <ChevronDown
                      size={14}
                      className={`text-white/40 transition-transform ${isBranchOpen ? 'rotate-180' : ''}`}
                    />
                  </button>

                  <AnimatePresence>
                    {isBranchOpen && (
                      <motion.div
                        initial={{ opacity: 0, y: -8, scale: 0.96 }}
                        animate={{ opacity: 1, y: 0, scale: 1 }}
                        exit={{ opacity: 0, y: -8, scale: 0.96 }}
                        transition={{ duration: 0.15, ease: [0.4, 0, 0.2, 1] }}
                        className="absolute top-full left-0 right-0 mt-1 z-10 bg-[#2c2c2e] border border-white/[0.08] rounded-xl shadow-xl shadow-black/50 overflow-hidden"
                      >
                        {[GameBranch.RELEASE, GameBranch.PRE_RELEASE].map((branch) => (
                          <button
                            key={branch}
                            onClick={() => {
                              setSelectedBranch(branch);
                              setIsBranchOpen(false);
                              // Reset to first available version when changing branch
                              setSelectedVersion(0);
                            }}
                            className={`w-full px-3 py-2 flex items-center gap-2 text-sm ${
                              selectedBranch === branch
                                ? 'text-white'
                                : 'text-white/70 hover:bg-white/10 hover:text-white'
                            }`}
                            style={selectedBranch === branch ? { backgroundColor: `${accentColor}20`, color: accentColor } : {}}
                          >
                            {selectedBranch === branch && <Check size={12} style={{ color: accentColor }} strokeWidth={3} />}
                            <span className={selectedBranch === branch ? '' : 'ml-[18px]'}>
                              {branch === GameBranch.RELEASE ? t('main.release') : t('main.preRelease')}
                            </span>
                          </button>
                        ))}
                      </motion.div>
                    )}
                  </AnimatePresence>
                </div>
              </div>

              {/* Version Selector */}
              <div className="space-y-1">
                <label className="text-xs text-white/50">{t('common.version')}</label>
                {!hasDownloadSources && !isLoadingVersions ? (
                  /* No sources warning - replaces version selector */
                  <div className="p-3 rounded-xl bg-amber-500/10 border border-amber-500/20">
                    <div className="flex items-start gap-2">
                      <AlertCircle size={14} className="text-amber-400 flex-shrink-0 mt-0.5" />
                      <div>
                        <p className="text-xs text-amber-400 font-medium">
                          {t('instances.noDownloadSources', 'No download sources available')}
                        </p>
                        <p className="text-[10px] text-amber-400/70 mt-0.5">
                          {t('instances.noDownloadSourcesHint', 'Add a mirror in Settings → Downloads or link your Hytale account to download game files.')}
                        </p>
                      </div>
                    </div>
                  </div>
                ) : (
                <div ref={versionRef} className="relative">
                  <button
                    onClick={() => {
                      setIsVersionOpen(!isVersionOpen);
                      setIsBranchOpen(false);
                    }}
                    disabled={isLoadingVersions}
                    className="w-full h-10 px-3 rounded-xl bg-[#2c2c2e] border border-white/[0.06] flex items-center justify-between text-white text-sm transition-colors hover:border-white/[0.12] disabled:opacity-50"
                    style={{ borderColor: isVersionOpen ? `${accentColor}50` : undefined }}
                  >
                    <span className="font-medium">
                      {isLoadingVersions ? (
                        <Loader2 size={14} className="animate-spin" />
                      ) : (
                        `v${selectedVersion}`
                      )}
                    </span>
                    <ChevronDown
                      size={14}
                      className={`text-white/40 transition-transform ${isVersionOpen ? 'rotate-180' : ''}`}
                    />
                  </button>

                  <AnimatePresence>
                    {isVersionOpen && !isLoadingVersions && (
                      <motion.div
                        initial={{ opacity: 0, y: -8, scale: 0.96 }}
                        animate={{ opacity: 1, y: 0, scale: 1 }}
                        exit={{ opacity: 0, y: -8, scale: 0.96 }}
                        transition={{ duration: 0.15, ease: [0.4, 0, 0.2, 1] }}
                        className="absolute top-full left-0 right-0 mt-1 z-10 max-h-48 overflow-y-auto bg-[#2c2c2e] border border-white/[0.08] rounded-xl shadow-xl shadow-black/50"
                      >
                        {availableVersions.map((versionInfo) => (
                          <button
                            key={versionInfo.version}
                            onClick={() => {
                              setSelectedVersion(versionInfo.version);
                              setIsVersionOpen(false);
                            }}
                            className={`w-full px-3 py-2 flex items-center justify-between text-sm ${
                              selectedVersion === versionInfo.version
                                ? 'text-white'
                                : 'text-white/70 hover:bg-white/10 hover:text-white'
                            }`}
                            style={selectedVersion === versionInfo.version ? { backgroundColor: `${accentColor}20`, color: accentColor } : {}}
                          >
                            <div className="flex items-center gap-2">
                              {selectedVersion === versionInfo.version && <Check size={12} style={{ color: accentColor }} strokeWidth={3} />}
                              <span className={selectedVersion === versionInfo.version ? '' : 'ml-[18px]'}>
                                v{versionInfo.version}
                              </span>
                            </div>
                            <span className="text-[10px] text-white/30 uppercase tracking-wider">
                              {versionInfo.source === 'Official' ? 'Hytale' : 'Mirror'}
                            </span>
                          </button>
                        ))}
                      </motion.div>
                    )}
                  </AnimatePresence>
                </div>
                )}
              </div>
            </div>

            {/* Warning when version is changed */}
            {versionChanged && hasDownloadSources && (
              <div
                className="p-3 rounded-xl border flex items-start gap-3"
                style={{ backgroundColor: `${accentColor}12`, borderColor: `${accentColor}35` }}
              >
                <AlertTriangle size={16} className="flex-shrink-0 mt-0.5" style={{ color: accentColor }} />
                <div>
                  <p className="text-xs font-medium" style={{ color: accentColor }}>
                    {t('instances.versionChangeWarning')}
                  </p>
                  <p className="text-[11px] text-white/50 mt-0.5">{t('instances.versionChangeWarningHint')}</p>
                </div>
              </div>
            )}
          </div>

          {/* Footer */}
          <div className="flex items-center justify-end gap-3 p-4 border-t border-white/[0.06]">
            <Button onClick={onClose}>
              {t('common.cancel')}
            </Button>
            <Button
              variant="primary"
              onClick={handleSave}
              disabled={isSaving}
            >
              {isSaving && <Loader2 size={14} className="animate-spin" />}
              {t('common.save')}
            </Button>
          </div>
        </motion.div>
      </motion.div>
    </AnimatePresence>
  );
};
