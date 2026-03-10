import React from 'react';
import { useTranslation } from 'react-i18next';
import { Coffee, HardDrive, FolderOpen, Check } from 'lucide-react';
import { RadioOptionCard } from '@/components/ui/Controls';
import { formatRamLabel } from '@/hooks/useJavaSettings';

interface JavaTabProps {
  gc: string;
  accentColor: string;
  accentTextColor: string;
  // Runtime mode
  javaRuntimeMode: 'bundled' | 'custom';
  handleJavaRuntimeModeChange: (mode: 'bundled' | 'custom') => void;
  customJavaPath: string;
  setCustomJavaPath: (v: string) => void;
  javaCustomPathError: string;
  handleBrowseCustomJavaPath: () => void;
  handleCustomJavaPathSave: () => void;
  // RAM
  detectedSystemRamMb: number;
  minJavaRamMb: number;
  maxJavaRamMb: number;
  javaRamMb: number;
  javaInitialRamMb: number;
  handleJavaRamChange: (v: number) => void;
  handleJavaInitialRamChange: (v: number) => void;
  // GC mode
  javaGcMode: 'auto' | 'g1';
  handleJavaGcModeChange: (mode: 'auto' | 'g1') => void;
  // JVM arguments
  javaArguments: string;
  setJavaArguments: (v: string) => void;
  javaArgumentsError: string;
  setJavaArgumentsError: (v: string) => void;
  handleSaveJavaArguments: () => void;
}

export const JavaTab: React.FC<JavaTabProps> = ({
  gc,
  accentColor,
  accentTextColor,
  javaRuntimeMode,
  handleJavaRuntimeModeChange,
  customJavaPath,
  setCustomJavaPath,
  javaCustomPathError,
  handleBrowseCustomJavaPath,
  handleCustomJavaPathSave,
  detectedSystemRamMb,
  minJavaRamMb,
  maxJavaRamMb,
  javaRamMb,
  javaInitialRamMb,
  handleJavaRamChange,
  handleJavaInitialRamChange,
  javaGcMode,
  handleJavaGcModeChange,
  javaArguments,
  setJavaArguments,
  javaArgumentsError,
  setJavaArgumentsError,
  handleSaveJavaArguments,
}) => {
  const { t } = useTranslation();

  return (
    <div className="space-y-6">
      {/* Java Runtime */}
      <div>
        <label className="block text-sm text-white/60 mb-2">{t('settings.javaSettings.javaRuntime')}</label>
        <p className="text-xs text-white/40 mb-4">{t('settings.javaSettings.javaRuntimeHint')}</p>

        <div className="space-y-2">
          <RadioOptionCard
            icon={<Coffee size={16} />}
            title={t('settings.javaSettings.useBundledJava')}
            description={t('settings.javaSettings.useBundledJavaHint')}
            selected={javaRuntimeMode === 'bundled'}
            onClick={() => handleJavaRuntimeModeChange('bundled')}
          />

          <RadioOptionCard
            icon={<Coffee size={16} />}
            title={t('settings.javaSettings.useCustomJava')}
            description={t('settings.javaSettings.useCustomJavaHint')}
            selected={javaRuntimeMode === 'custom'}
            onClick={() => handleJavaRuntimeModeChange('custom')}
          >
            <div className="flex gap-2">
              <input
                type="text"
                value={customJavaPath}
                onChange={(event) => setCustomJavaPath(event.target.value)}
                onKeyDown={async (event) => {
                  if (event.key === 'Enter' && customJavaPath.trim()) {
                    await handleCustomJavaPathSave();
                  }
                }}
                placeholder={t('settings.javaSettings.customJavaPathPlaceholder')}
                className={`flex-1 h-12 px-4 rounded-xl ${gc} text-white text-sm placeholder-white/30 focus:outline-none`}
              />
              <div className={`flex rounded-full overflow-hidden ${gc}`}>
                <button
                  onClick={handleBrowseCustomJavaPath}
                  className="h-12 px-4 flex items-center justify-center text-white/60 hover:text-white hover:bg-white/5 transition-colors"
                  title={t('common.select')}
                >
                  <FolderOpen size={18} />
                  <span className="ml-2 text-sm">{t('common.select')}</span>
                </button>
                <div className="w-px bg-white/10" />
                <button
                  onClick={handleCustomJavaPathSave}
                  disabled={!customJavaPath.trim()}
                  className="h-12 px-4 flex items-center justify-center text-white/60 hover:text-white hover:bg-white/5 transition-colors disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-transparent disabled:hover:text-white/60"
                  title={t('common.save')}
                >
                  <Check size={18} />
                  <span className="ml-2 text-sm">{t('common.save')}</span>
                </button>
              </div>
            </div>
            {javaCustomPathError && (
              <p className="mt-2 text-xs text-red-300">{javaCustomPathError}</p>
            )}
          </RadioOptionCard>
        </div>
      </div>

      {/* RAM Allocation */}
      <div className={`p-4 rounded-2xl ${gc}`}>
        <div className="flex items-center gap-3 mb-3">
          <div className="w-8 h-8 rounded-lg bg-white/[0.06] flex items-center justify-center">
            <HardDrive size={16} className="text-white opacity-70" />
          </div>
          <div>
            <span className="text-white text-sm font-medium">{t('settings.javaSettings.ramAllocation')}</span>
            <p className="text-xs text-white/40">{t('settings.javaSettings.ramAllocationHint')}</p>
          </div>
        </div>

        <div className="flex items-center justify-between mb-2 text-xs text-white/60">
          <span>{t('settings.javaSettings.detectedMemory', { value: formatRamLabel(detectedSystemRamMb) })}</span>
          <span>{t('settings.javaSettings.maxRecommended', { value: formatRamLabel(maxJavaRamMb) })}</span>
        </div>

        <div className="grid grid-cols-2 gap-4">
          <div className={`p-3 rounded-xl ${gc}`}>
            <div className="flex items-center justify-between mb-2 text-xs text-white/50">
              <span>{t('settings.javaSettings.maxRam')}</span>
              <span>{formatRamLabel(javaRamMb)}</span>
            </div>
            <input
              type="range"
              min={minJavaRamMb}
              max={maxJavaRamMb}
              step={256}
              value={javaRamMb}
              onChange={(event) => handleJavaRamChange(Number.parseInt(event.target.value, 10) || minJavaRamMb)}
              className="java-range w-full accent-current appearance-none border-0 outline-none focus:outline-none focus:ring-0"
              style={{ color: accentColor }}
            />
          </div>

          <div className={`p-3 rounded-xl ${gc}`}>
            <div className="flex items-center justify-between mb-2 text-xs text-white/50">
              <span>{t('settings.javaSettings.initialRam')}</span>
              <span>{formatRamLabel(javaInitialRamMb)}</span>
            </div>
            <input
              type="range"
              min={minJavaRamMb}
              max={javaRamMb}
              step={256}
              value={javaInitialRamMb}
              onChange={(event) => handleJavaInitialRamChange(Number.parseInt(event.target.value, 10) || minJavaRamMb)}
              className="java-range w-full accent-current appearance-none border-0 outline-none focus:outline-none focus:ring-0"
              style={{ color: accentColor }}
            />
          </div>
        </div>
      </div>

      {/* GC Mode */}
      <div>
        <label className="block text-sm text-white/60 mb-2">{t('settings.javaSettings.gcMode')}</label>
        <p className="text-xs text-white/40 mb-3">{t('settings.javaSettings.gcModeHint')}</p>
        <div className="space-y-2">
          {(['auto', 'g1'] as const).map((mode) => (
            <button
              key={mode}
              onClick={() => handleJavaGcModeChange(mode)}
              className="w-full p-3 rounded-xl border transition-all text-left"
              style={{
                backgroundColor: javaGcMode === mode ? `${accentColor}15` : '#151515',
                borderColor: javaGcMode === mode ? `${accentColor}50` : 'rgba(255,255,255,0.08)'
              }}
            >
              <div className="flex items-center justify-between">
                <div>
                  <div className="text-white text-sm font-medium">{t(`settings.javaSettings.gc_${mode}`)}</div>
                  <div className="text-[11px] text-white/35 mt-0.5">{t(`settings.javaSettings.gc_${mode}Hint`)}</div>
                </div>
                {javaGcMode === mode && (
                  <div className="w-5 h-5 rounded-full flex items-center justify-center" style={{ backgroundColor: accentColor }}>
                    <Check size={12} style={{ color: accentTextColor }} strokeWidth={3} />
                  </div>
                )}
              </div>
            </button>
          ))}
        </div>
      </div>

      {/* JVM Arguments */}
      <div>
        <label className="block text-sm text-white/60 mb-2">{t('settings.javaSettings.jvmArguments')}</label>
        <div className="flex gap-2">
          <input
            type="text"
            value={javaArguments}
            onChange={(event) => {
              setJavaArguments(event.target.value);
              if (javaArgumentsError) setJavaArgumentsError('');
            }}
            placeholder={t('settings.javaSettings.jvmArgumentsPlaceholder')}
            className={`flex-1 h-12 px-4 rounded-xl ${gc} text-white text-sm placeholder-white/35 focus:outline-none`}
          />
          <div className={`flex rounded-full overflow-hidden ${gc}`}>
            <button
              onClick={handleSaveJavaArguments}
              className="h-12 px-4 flex items-center justify-center text-white/60 hover:text-white hover:bg-white/5 transition-colors"
              title={t('common.save')}
            >
              <Check size={18} />
              <span className="ml-2 text-sm">{t('common.save')}</span>
            </button>
          </div>
        </div>
        <p className="mt-2 text-xs text-white/40">{t('settings.javaSettings.jvmArgumentsHint')}</p>
        {javaArgumentsError && (
          <p className="mt-2 text-xs text-yellow-300">{javaArgumentsError}</p>
        )}
      </div>
    </div>
  );
};
