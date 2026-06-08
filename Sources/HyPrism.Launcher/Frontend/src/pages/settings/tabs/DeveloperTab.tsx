import React from 'react';
import { useTranslation } from 'react-i18next';
import { AlertTriangle } from 'lucide-react';
import { Button } from '@/components/ui/Controls';
import { DeveloperUiCatalog } from '@/components/dev/DeveloperUiCatalog';

interface DeveloperTabProps {
  gc: string;
  accentColor: string;
  activeTab: string;
  selectedLauncherBranch: string;
  resetOnboarding: () => Promise<void>;
}

export const DeveloperTab: React.FC<DeveloperTabProps> = ({
  gc,
  accentColor,
  activeTab,
  selectedLauncherBranch,
  resetOnboarding,
}) => {
  const { t } = useTranslation();

  return (
    <div className="space-y-6">
      <div className="p-4 bg-yellow-500/10 border border-yellow-500/30 rounded-xl">
        <div className="flex items-center gap-2 text-yellow-500 text-sm font-medium">
          <AlertTriangle size={16} />
          {t('settings.developerSettings.warning')}
        </div>
      </div>

      <DeveloperUiCatalog />

      {/* Show Intro on Next Launch */}
      <div className={`p-4 rounded-2xl ${gc} space-y-4`}>
        <h3 className="text-white font-medium text-sm">{t('settings.developerSettings.onboarding')}</h3>
        <Button
          onClick={async () => {
            await resetOnboarding();
            alert(t('settings.developerSettings.introRestart'));
          }}
          className="w-full"
        >
          {t('settings.developerSettings.showIntro')}
        </Button>
      </div>

      <div className={`p-4 rounded-2xl ${gc}`}>
        <p className="text-white/40 text-xs">
          {t('settings.developerSettings.debugInfo')} Tab={activeTab}, Branch={selectedLauncherBranch}, Accent={accentColor}
        </p>
      </div>
    </div>
  );
};
