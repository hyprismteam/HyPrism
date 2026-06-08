import React from 'react';
import { FolderOpen } from 'lucide-react';
import { Button } from '@/components/ui/Controls';
import type { UseOnboardingReturn } from '@/hooks/useOnboarding';

interface LocationStepProps {
  onboarding: UseOnboardingReturn;
}

export const LocationStep: React.FC<LocationStepProps> = ({ onboarding }) => {
  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-xl font-semibold text-white mb-2">{onboarding.t('onboarding.chooseLocation')}</h2>
        <p className="text-sm text-white/60">{onboarding.t('onboarding.locationHint')}</p>
      </div>
      
      <div className="p-4 rounded-xl bg-[#1a1a1a]/80 border border-white/10">
        <label className="block text-sm text-white/60 mb-3">{onboarding.t('onboarding.instanceFolder')}</label>
        <div className="flex gap-2">
          <input
            type="text"
            value={onboarding.instanceDir}
            onChange={(e) => onboarding.setInstanceDir(e.target.value)}
            className="flex-1 h-12 px-4 rounded-xl bg-[#0a0a0a]/80 border border-white/10 text-white text-sm focus:outline-none focus:border-white/30 truncate"
          />
          <Button
            onClick={onboarding.handleBrowseInstanceDir}
            className="h-12 px-4 rounded-xl"
          >
            <FolderOpen size={18} />
            <span className="text-sm">{onboarding.t('common.browse')}</span>
          </Button>
        </div>
        <p className="text-xs text-white/40 mt-2">{onboarding.t('onboarding.filesStoredHere')}</p>
      </div>
      
      {/* Quick info */}
      <div className="p-4 rounded-xl bg-[#1a1a1a]/50 border border-white/5">
        <p className="text-xs text-white/40 leading-relaxed">
          {onboarding.t('onboarding.changeInSettings')}
        </p>
      </div>
    </div>
  );
};
