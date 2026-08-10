// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

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
            <ChevronDown size={16} className={`text-white opacity-40 transition-transform ${isLanguageOpen ? 'rotate-180' : ''}`} />
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

      {/* Toggle Settings */}
      <div className="space-y-3">
        <SettingsToggleCard
          icon={<Power size={16} className="text-white opacity-70" />}
          title={t('settings.generalSettings.closeLauncher')}
          description={t('settings.generalSettings.closeLauncherHint')}
          checked={closeAfterLaunch}
          onCheckedChange={() => handleCloseAfterLaunchChange()}
        />

        <SettingsToggleCard
          icon={<FlaskConical size={16} className="text-white opacity-70" />}
          title={t('settings.generalSettings.showAlphaMods')}
          description={t('settings.generalSettings.showAlphaModsHint')}
          checked={showAlphaMods}
          onCheckedChange={() => handleShowAlphaModsChange()}
        />

        {onlineMode && profileLoaded && !isActiveProfileOfficial && authMode !== 'official' && (
          <SettingsToggleCard
            icon={<Server size={16} className="text-white opacity-70" />}
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
