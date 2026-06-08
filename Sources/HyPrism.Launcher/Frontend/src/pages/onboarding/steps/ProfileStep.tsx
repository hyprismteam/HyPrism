import React from 'react';
import { User, RefreshCw } from 'lucide-react';
import { IconButton } from '@/components/ui/Controls';
import type { UseOnboardingReturn } from '@/hooks/useOnboarding';

interface ProfileStepProps {
  onboarding: UseOnboardingReturn;
}

export const ProfileStep: React.FC<ProfileStepProps> = ({ onboarding }) => {
  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-xl font-semibold text-white mb-2">{onboarding.t('onboarding.setupProfile')}</h2>
        <p className="text-sm text-white/60">{onboarding.t('onboarding.chooseUsername')}</p>
      </div>
      
      {/* Username */}
      <div>
        <label className="block text-sm text-white/60 mb-2">{onboarding.t('onboarding.username')}</label>
        <div className="flex items-center gap-3">
          <div 
            className="w-12 h-12 rounded-xl flex items-center justify-center flex-shrink-0" 
            style={{ backgroundColor: `${onboarding.accentColor}20` }}
          >
            <User size={24} style={{ color: onboarding.accentColor }} />
          </div>
          <input
            type="text"
            value={onboarding.username}
            onChange={(e) => onboarding.setUsername(e.target.value.slice(0, 16))}
            placeholder={onboarding.t('onboarding.enterUsername')}
            className="flex-1 h-12 px-4 rounded-xl bg-[#1a1a1a]/80 border border-white/10 text-white text-sm focus:outline-none focus:border-white/30"
            maxLength={16}
          />
          <IconButton
            onClick={onboarding.handleGenerateUsername}
            disabled={onboarding.isGeneratingUsername}
            title={onboarding.t('onboarding.generateUsername')}
            size="lg"
            style={{ height: '3rem', width: '3rem', borderRadius: '0.75rem' }}
          >
            <RefreshCw size={18} className={onboarding.isGeneratingUsername ? 'animate-spin' : ''} />
          </IconButton>
        </div>
        <p className="text-xs text-white/40 mt-2">{onboarding.t('onboarding.usernameHint')}</p>
      </div>
    </div>
  );
};
