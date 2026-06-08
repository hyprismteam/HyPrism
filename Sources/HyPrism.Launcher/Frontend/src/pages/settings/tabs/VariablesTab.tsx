import React from 'react';
import { useTranslation } from 'react-i18next';
import { Terminal } from 'lucide-react';
import { SettingsToggleCard } from '@/components/ui/Controls';
import { ENV_PRESETS } from '@/hooks/useSettings';

interface VariablesTabProps {
  accentColor: string;
  envForceX11: boolean;
  envDisableVkLayers: boolean;
  handleEnvPresetToggle: (presetKey: keyof typeof ENV_PRESETS, enabled: boolean) => void;
  gameEnvVars: string;
  setGameEnvVars: (v: string) => void;
  gameEnvVarsError: string;
  gameEnvVarsFocus: boolean;
  setGameEnvVarsFocus: (v: boolean) => void;
  handleSaveGameEnvVars: () => void;
}

export const VariablesTab: React.FC<VariablesTabProps> = ({
  accentColor,
  envForceX11,
  envDisableVkLayers,
  handleEnvPresetToggle,
  gameEnvVars,
  setGameEnvVars,
  gameEnvVarsError,
  gameEnvVarsFocus,
  setGameEnvVarsFocus,
  handleSaveGameEnvVars,
}) => {
  const { t } = useTranslation();

  return (
    <div className="space-y-6">
      {/* Common Presets */}
      <div>
        <label className="block text-sm text-white/60 mb-2">{t('settings.variablesSettings.commonPresets')}</label>
        <p className="text-xs text-white/40 mb-4">{t('settings.variablesSettings.commonPresetsHint')}</p>
        <div className="space-y-2">
          {/* Force X11 */}
          <SettingsToggleCard
            icon={<Terminal size={16} className="text-white opacity-70" />}
            title={t('settings.variablesSettings.forceX11')}
            description={<>{t('settings.variablesSettings.forceX11Hint')}<code className="text-[10px] text-white/30 mt-1 block font-mono">SDL_VIDEODRIVER=x11</code></>}
            checked={envForceX11}
            onCheckedChange={(v) => handleEnvPresetToggle('forceX11', v)}
          />

          {/* Disable Vulkan Layers */}
          <SettingsToggleCard
            icon={<Terminal size={16} className="text-white opacity-70" />}
            title={t('settings.variablesSettings.disableVkLayers')}
            description={<>{t('settings.variablesSettings.disableVkLayersHint')}<code className="text-[10px] text-white/30 mt-1 block font-mono">VK_LOADER_LAYERS_DISABLE=all</code></>}
            checked={envDisableVkLayers}
            onCheckedChange={(v) => handleEnvPresetToggle('disableVkLayers', v)}
          />
        </div>
      </div>

      {/* Custom Environment Variables */}
      <div>
        <label className="block text-sm text-white/60 mb-2">{t('settings.variablesSettings.customEnvVars')}</label>
        <p className="text-xs text-white/40 mb-3">{t('settings.variablesSettings.customEnvVarsHint')}</p>
        <div className={`p-1 rounded-xl border transition-all flex items-center bg-[#151515] ${gameEnvVarsFocus
          ? 'border-white/20'
          : 'border-white/[0.06] hover:border-white/[0.12]'
          }`}
          style={gameEnvVarsFocus ? { borderColor: `${accentColor}50`, backgroundColor: `${accentColor}08` } : undefined}
        >
          <input
            type="text"
            value={gameEnvVars}
            onChange={(e) => setGameEnvVars(e.target.value)}
            onBlur={() => {
              setGameEnvVarsFocus(false);
              handleSaveGameEnvVars();
            }}
            onFocus={() => setGameEnvVarsFocus(true)}
            placeholder="VAR1=value VAR2=value"
            className="w-full bg-transparent border-0 text-sm text-white px-3 py-2.5 outline-none placeholder:text-white/20 font-mono"
            spellCheck={false}
          />
        </div>
        {gameEnvVarsError && (
          <p className="mt-2 text-xs text-yellow-300">{gameEnvVarsError}</p>
        )}
      </div>
    </div>
  );
};
