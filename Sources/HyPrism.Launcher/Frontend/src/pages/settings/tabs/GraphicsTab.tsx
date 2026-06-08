import React from 'react';
import { useTranslation } from 'react-i18next';
import { Monitor, HardDrive, Settings, Check } from 'lucide-react';

interface GraphicsTabProps {
  accentColor: string;
  accentTextColor: string;
  gpuPreference: string;
  gpuAdapters: Array<{ name: string; vendor: string; type: string }>;
  hasSingleGpu: boolean;
  handleGpuPreferenceChange: (preference: string) => void;
}

export const GraphicsTab: React.FC<GraphicsTabProps> = ({
  accentColor,
  accentTextColor,
  gpuPreference,
  gpuAdapters,
  hasSingleGpu,
  handleGpuPreferenceChange,
}) => {
  const { t } = useTranslation();

  return (
    <div className="space-y-6">
      {/* GPU Preference */}
      <div>
        <label className="block text-sm text-white/60 mb-2">{t('settings.graphicsSettings.gpuPreference')}</label>
        <p className="text-xs text-white/40 mb-4">{t('settings.graphicsSettings.gpuPreferenceHint')}</p>
        {hasSingleGpu && (
          <div className="mb-3 p-2.5 rounded-lg bg-[#2c2c2e] border border-white/[0.08] text-xs text-white/50 flex items-center gap-2">
            <Settings size={14} className="flex-shrink-0 text-white opacity-40" />
            {t('settings.graphicsSettings.singleGpuNotice')}
          </div>
        )}
        <div className="space-y-2">
          {(['dedicated', 'integrated', 'auto'] as const).map((option) => {
            const icons: Record<string, React.ReactNode> = {
              dedicated: <Monitor size={18} />,
              integrated: <HardDrive size={18} />,
              auto: <Settings size={18} />,
            };
            const isSelected = gpuPreference === option;
            const isDisabled = hasSingleGpu && option !== 'auto';
            // Find GPU model name for this type
            const matchingGpu = gpuAdapters.find(g => g.type === option);
            const gpuModelName = option === 'auto'
              ? (gpuAdapters.length > 0 ? gpuAdapters.map(g => g.name).join(' / ') : undefined)
              : matchingGpu?.name;
            return (
              <button
                key={option}
                onClick={() => !isDisabled && handleGpuPreferenceChange(option)}
                disabled={isDisabled}
                className={`w-full p-3 rounded-xl border transition-all text-left ${isDisabled ? 'opacity-40 cursor-not-allowed' : 'cursor-pointer'}`}
                style={{
                  backgroundColor: isSelected ? `${accentColor}15` : '#151515',
                  borderColor: isSelected ? `${accentColor}50` : 'rgba(255,255,255,0.08)'
                }}
              >
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-3">
                    <div
                      className="w-9 h-9 rounded-lg flex items-center justify-center flex-shrink-0"
                      style={{ backgroundColor: isSelected ? `${accentColor}25` : 'rgba(255,255,255,0.06)' }}
                    >
                      <span style={{ color: isSelected ? accentColor : '#ffffff', opacity: isSelected ? 1 : 0.5 }}>{icons[option]}</span>
                    </div>
                    <div>
                      <div className="text-white text-sm font-medium">{t(`settings.graphicsSettings.gpu_${option}`)}</div>
                      {gpuModelName ? (
                        <div className="text-[11px] text-white/40 mt-0.5">{gpuModelName}</div>
                      ) : (
                        <div className="text-[11px] text-white/30 mt-0.5">{t(`settings.graphicsSettings.gpu_${option}Hint`)}</div>
                      )}
                    </div>
                  </div>
                  {isSelected && (
                    <div
                      className="w-5 h-5 rounded-full flex items-center justify-center flex-shrink-0"
                      style={{ backgroundColor: accentColor }}
                    >
                      <Check size={12} style={{ color: accentTextColor }} strokeWidth={3} />
                    </div>
                  )}
                </div>
              </button>
            );
          })}
        </div>
      </div>
    </div>
  );
};
