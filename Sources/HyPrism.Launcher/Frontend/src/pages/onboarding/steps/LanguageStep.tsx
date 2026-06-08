import React from 'react';
import { Check } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { LANGUAGE_CONFIG } from '@/constants/languages';
import type { UseOnboardingReturn } from '@/hooks/useOnboarding';

interface LanguageStepProps {
  onboarding: UseOnboardingReturn;
}

export const LanguageStep: React.FC<LanguageStepProps> = ({ onboarding }) => {
  const { i18n } = useTranslation();

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-xl font-semibold text-white mb-2">{onboarding.t('onboarding.chooseLanguage')}</h2>
        <p className="text-sm text-white/60">{onboarding.t('onboarding.selectLanguageHint')}</p>
      </div>
      
      <div className="grid grid-cols-2 sm:grid-cols-3 gap-2 max-h-[280px] overflow-y-auto pr-2">
        {Object.values(LANGUAGE_CONFIG).map((lang) => (
          <button
            key={lang.code}
            onClick={() => onboarding.handleLanguageChange(lang.code)}
            className="p-3 rounded-xl border transition-all text-left hover:border-white/20"
            style={{
              backgroundColor: i18n.language === lang.code ? `${onboarding.accentColor}20` : 'rgba(26,26,26,0.8)',
              borderColor: i18n.language === lang.code ? `${onboarding.accentColor}50` : 'rgba(255,255,255,0.1)'
            }}
          >
            <div className="flex items-center justify-between gap-2">
              <div className="flex items-center gap-2">
                {i18n.language === lang.code && (
                  <div 
                    className="w-5 h-5 rounded-full flex items-center justify-center flex-shrink-0"
                    style={{ backgroundColor: onboarding.accentColor }}
                  >
                    <Check size={12} style={{ color: onboarding.accentTextColor }} strokeWidth={3} />
                  </div>
                )}
                <div className={i18n.language !== lang.code ? 'ml-7' : ''}>
                  <span className="text-white text-sm font-medium block">{lang.nativeName}</span>
                  <span className="text-xs text-white/40">{lang.name}</span>
                </div>
              </div>
              <span className={`fi fi-${lang.flagCode} text-xl rounded-sm`}></span>
            </div>
          </button>
        ))}
      </div>
    </div>
  );
};
