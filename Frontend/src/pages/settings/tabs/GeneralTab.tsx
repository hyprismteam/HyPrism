import React from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import { Check, ChevronDown, Power, FlaskConical, Server } from 'lucide-react';
import { SettingsToggleCard } from '@/components/ui/Controls';
import { LANGUAGE_CONFIG } from '@/constants/languages';
import { Language } from '@/constants/enums';

interface GeneralTabProps {
  gc: string;
  accentColor: string;
  // Language
  isLanguageOpen: boolean;
  setIsLanguageOpen: (v: boolean) => void;
  languageDropdownRef: React.RefObject<HTMLDivElement>;
  handleLanguageSelect: (lang: Language) => void;
  // Branch
  isBranchOpen: boolean;
  setIsBranchOpen: (v: boolean) => void;
  branchDropdownRef: React.RefObject<HTMLDivElement>;
  selectedLauncherBranch: string;
  handleLauncherBranchChange: (branch: string) => void;
  // Toggles
  closeAfterLaunch: boolean;
  handleCloseAfterLaunchChange: () => void;
  showAlphaMods: boolean;
  handleShowAlphaModsChange: () => void;
  onlineMode: boolean;
  authMode: 'default' | 'official' | 'custom';
  useDualAuth: boolean;
  handleUseDualAuthChange: () => void;
  isActiveProfileOfficial?: boolean;
  profileLoaded?: boolean;
}

export const GeneralTab: React.FC<GeneralTabProps> = ({
  gc,
  accentColor,
  isLanguageOpen,
  setIsLanguageOpen,
  languageDropdownRef,
  handleLanguageSelect,
  isBranchOpen,
  setIsBranchOpen,
  branchDropdownRef,
  selectedLauncherBranch,
  handleLauncherBranchChange,
  closeAfterLaunch,
  handleCloseAfterLaunchChange,
  showAlphaMods,
  handleShowAlphaModsChange,
  onlineMode,
  authMode,
  useDualAuth,
  handleUseDualAuthChange,
  isActiveProfileOfficial = false,
  profileLoaded = true,
}) => {
  const { i18n, t } = useTranslation();
  const currentLangConfig = LANGUAGE_CONFIG[i18n.language as Language] || LANGUAGE_CONFIG[Language.ENGLISH];

  return (
    <div className="space-y-6">
      {/* Language Selector */}
      <div>
        <label className="block text-sm text-white/60 mb-2">{t('settings.language')}</label>
        <div ref={languageDropdownRef} className="relative">
          <button
            onClick={() => {
              setIsLanguageOpen(!isLanguageOpen);
              setIsBranchOpen(false);
            }}
            className={`w-full h-12 px-4 rounded-xl ${gc} flex items-center justify-between text-white transition-colors hover:border-white/[0.12]`}
            style={{ borderColor: isLanguageOpen ? `${accentColor}50` : undefined }}
          >
            <div className="flex items-center gap-3">
              <span className={`fi fi-${currentLangConfig.flagCode} text-lg rounded-sm`}></span>
              <div className="flex items-center gap-2">
                <span className="font-medium">{currentLangConfig.nativeName}</span>
                <span className="text-white/50 text-sm">({currentLangConfig.name})</span>
              </div>
            </div>
            <ChevronDown size={16} className={`text-white/40 transition-transform ${isLanguageOpen ? 'rotate-180' : ''}`} />
          </button>

          <AnimatePresence>
            {isLanguageOpen && (
              <motion.div
                initial={{ opacity: 0, y: -8, scale: 0.96 }}
                animate={{ opacity: 1, y: 0, scale: 1 }}
                exit={{ opacity: 0, y: -8, scale: 0.96 }}
                transition={{ duration: 0.15, ease: [0.4, 0, 0.2, 1] }}
                className={`absolute top-full left-0 right-0 mt-2 z-10 max-h-60 overflow-y-auto ${gc} rounded-xl shadow-xl shadow-black/50`}
              >
                {Object.values(LANGUAGE_CONFIG).map((lang) => (
                  <button
                    key={lang.code}
                    onClick={() => handleLanguageSelect(lang.code)}
                    className={`w-full px-4 py-3 flex items-center gap-3 text-sm ${i18n.language === lang.code
                      ? 'text-white'
                      : 'text-white/70 hover:bg-white/10 hover:text-white'
                    }`}
                    style={i18n.language === lang.code ? { backgroundColor: `${accentColor}20`, color: accentColor } : {}}
                  >
                    {i18n.language === lang.code && <Check size={14} style={{ color: accentColor }} strokeWidth={3} />}
                    <span className={`fi fi-${lang.flagCode} text-lg rounded-sm ${i18n.language === lang.code ? '' : 'ml-[22px]'}`}></span>
                    <div className="flex flex-col items-start">
                      <span className="font-medium">{lang.nativeName}</span>
                      <span className="text-xs opacity-50">{lang.name}</span>
                    </div>
                  </button>
                ))}
              </motion.div>
            )}
          </AnimatePresence>
        </div>
      </div>

      {/* Launcher Branch Selector */}
      <div>
        <label className="block text-sm text-white/60 mb-2">{t('settings.generalSettings.updateChannel')}</label>
        <div ref={branchDropdownRef} className="relative">
          <button
            onClick={() => {
              setIsBranchOpen(!isBranchOpen);
              setIsLanguageOpen(false);
            }}
            className={`w-full h-12 px-4 rounded-xl ${gc} flex items-center justify-between text-white transition-colors hover:border-white/[0.12]`}
            style={{ borderColor: isBranchOpen ? `${accentColor}50` : undefined }}
          >
            <div className="flex items-center gap-2">
              <span className="font-medium">
                {selectedLauncherBranch === 'beta' ? t('settings.generalSettings.updateChannelBeta') : t('settings.generalSettings.updateChannelStable')}
              </span>
              {selectedLauncherBranch === 'beta' && (
                <span className="text-xs px-2 py-0.5 rounded bg-yellow-500/20 text-yellow-500">
                  {t('settings.generalSettings.updateChannelExperimental')}
                </span>
              )}
            </div>
            <ChevronDown size={16} className={`text-white/40 transition-transform ${isBranchOpen ? 'rotate-180' : ''}`} />
          </button>

          <AnimatePresence>
            {isBranchOpen && (
              <motion.div
                initial={{ opacity: 0, y: -8, scale: 0.96 }}
                animate={{ opacity: 1, y: 0, scale: 1 }}
                exit={{ opacity: 0, y: -8, scale: 0.96 }}
                transition={{ duration: 0.15, ease: [0.4, 0, 0.2, 1] }}
                className={`absolute top-full left-0 right-0 mt-2 z-10 ${gc} rounded-xl shadow-xl shadow-black/50 overflow-hidden`}
              >
                <button
                  onClick={() => handleLauncherBranchChange('release')}
                  className={`w-full px-4 py-3 flex items-center gap-2 text-sm ${selectedLauncherBranch === 'release'
                    ? 'text-white'
                    : 'text-white/70 hover:bg-white/10 hover:text-white'
                  }`}
                  style={selectedLauncherBranch === 'release' ? { backgroundColor: `${accentColor}20`, color: accentColor } : {}}
                >
                  {selectedLauncherBranch === 'release' && <Check size={14} style={{ color: accentColor }} strokeWidth={3} />}
                  <div className={`flex flex-col items-start ${selectedLauncherBranch === 'release' ? '' : 'ml-[22px]'}`}>
                    <span className="font-medium">{t('settings.generalSettings.updateChannelStable')}</span>
                    <span className="text-xs opacity-50">{t('settings.generalSettings.updateChannelStableHint')}</span>
                  </div>
                </button>
                <button
                  onClick={() => handleLauncherBranchChange('beta')}
                  className={`w-full px-4 py-3 flex items-center gap-2 text-sm ${selectedLauncherBranch === 'beta'
                    ? 'text-white'
                    : 'text-white/70 hover:bg-white/10 hover:text-white'
                  }`}
                  style={selectedLauncherBranch === 'beta' ? { backgroundColor: `${accentColor}20`, color: accentColor } : {}}
                >
                  {selectedLauncherBranch === 'beta' && <Check size={14} style={{ color: accentColor }} strokeWidth={3} />}
                  <div className={`flex flex-col items-start ${selectedLauncherBranch === 'beta' ? '' : 'ml-[22px]'}`}>
                    <span className="font-medium">{t('settings.generalSettings.updateChannelBeta')}</span>
                    <span className="text-xs opacity-50">{t('settings.generalSettings.updateChannelBetaHint')}</span>
                  </div>
                </button>
              </motion.div>
            )}
          </AnimatePresence>
        </div>
        <p className="mt-2 text-xs text-white/40">
          {selectedLauncherBranch === 'beta'
            ? t('settings.generalSettings.updateChannelBetaWarning')
            : t('settings.generalSettings.updateChannelHint')}
        </p>
      </div>

      {/* Toggle Settings */}
      <div className="space-y-3">
        <SettingsToggleCard
          icon={<Power size={16} className="text-white/70" />}
          title={t('settings.generalSettings.closeLauncher')}
          description={t('settings.generalSettings.closeLauncherHint')}
          checked={closeAfterLaunch}
          onCheckedChange={() => handleCloseAfterLaunchChange()}
        />

        <SettingsToggleCard
          icon={<FlaskConical size={16} className="text-white/70" />}
          title={t('settings.generalSettings.showAlphaMods')}
          description={t('settings.generalSettings.showAlphaModsHint')}
          checked={showAlphaMods}
          onCheckedChange={() => handleShowAlphaModsChange()}
        />

        {onlineMode && profileLoaded && !isActiveProfileOfficial && authMode !== 'official' && (
          <SettingsToggleCard
            icon={<Server size={16} className="text-white/70" />}
            title={t('settings.generalSettings.legacyPatching')}
            description={t('settings.generalSettings.legacyPatchingHint')}
            checked={!useDualAuth}
            onCheckedChange={() => handleUseDualAuthChange()}
          />
        )}
      </div>
    </div>
  );
};
