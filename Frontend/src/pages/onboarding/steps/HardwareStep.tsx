import React from 'react';
import { Check, Cpu, Monitor, HardDrive, Settings } from 'lucide-react';
import type { UseOnboardingReturn } from '@/hooks/useOnboarding';

interface HardwareStepProps {
  onboarding: UseOnboardingReturn;
}

export const HardwareStep: React.FC<HardwareStepProps> = ({ onboarding }) => {
  const icons: Record<string, React.ReactNode> = {
    dedicated: <Monitor size={18} />,
    integrated: <HardDrive size={18} />,
    auto: <Settings size={18} />,
  };

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-xl font-semibold text-white mb-2">{onboarding.t('onboarding.hardwareSettings')}</h2>
        <p className="text-sm text-white/60">{onboarding.t('onboarding.gpuHint')}</p>
      </div>
      
      {/* GPU Preference */}
      <div>
        <label className="text-sm text-white/60 mb-3 flex items-center gap-2">
          <Cpu size={14} />
          {onboarding.t('onboarding.gpuPreference')}
        </label>
        <div className="space-y-2">
          {(['auto', 'dedicated', 'integrated'] as const).map((option) => {
            const isSelected = onboarding.gpuPreference === option;
            const matchingGpu = onboarding.gpuAdapters.find(g => g.type === option);
            const gpuModelName = option === 'auto'
              ? (onboarding.gpuAdapters.length > 0 ? onboarding.gpuAdapters.map(g => g.name).join(' / ') : undefined)
              : matchingGpu?.name;

            return (
              <button
                key={option}
                onClick={() => onboarding.handleGpuPreferenceChange(option)}
                className="w-full p-3 rounded-xl border transition-all text-left cursor-pointer"
                style={{
                  backgroundColor: isSelected ? `${onboarding.accentColor}15` : '#151515',
                  borderColor: isSelected ? `${onboarding.accentColor}50` : 'rgba(255,255,255,0.08)'
                }}
              >
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-3">
                    <div
                      className="w-9 h-9 rounded-lg flex items-center justify-center flex-shrink-0"
                      style={{ backgroundColor: isSelected ? `${onboarding.accentColor}25` : 'rgba(255,255,255,0.06)' }}
                    >
                      <span style={{ color: isSelected ? onboarding.accentColor : '#ffffff', opacity: isSelected ? 1 : 0.5 }}>
                        {icons[option]}
                      </span>
                    </div>
                    <div>
                      <div className="text-white text-sm font-medium">{onboarding.t(`onboarding.gpu_${option}`)}</div>
                      {gpuModelName ? (
                        <div className="text-[11px] text-white/40 mt-0.5">{gpuModelName}</div>
                      ) : (
                        <div className="text-[11px] text-white/30 mt-0.5">{onboarding.t(`onboarding.gpu_${option}Hint`)}</div>
                      )}
                    </div>
                  </div>
                  {isSelected && (
                    <div
                      className="w-5 h-5 rounded-full flex items-center justify-center flex-shrink-0"
                      style={{ backgroundColor: onboarding.accentColor }}
                    >
                      <Check size={12} style={{ color: onboarding.accentTextColor }} strokeWidth={3} />
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
