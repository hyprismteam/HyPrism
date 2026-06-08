import React from 'react';
import { AlertTriangle, ArrowRight, ArrowLeft } from 'lucide-react';
import { Button } from '@/components/ui/Controls';
import type { UseOnboardingReturn } from '@/hooks/useOnboarding';

interface WarningPhaseProps {
  onboarding: UseOnboardingReturn;
}

export const WarningPhase: React.FC<WarningPhaseProps> = ({ onboarding }) => {
  const bgImage = onboarding.getCurrentBackground();
  const enabledMirrorCount = onboarding.skipAuthEnabledMirrorCount ?? 0;
  const hasMirrors = enabledMirrorCount > 0;

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center overflow-hidden">
      {/* Background with blur */}
      <div 
        className="absolute inset-0 bg-cover bg-center transition-all duration-1000"
        style={{ 
          backgroundImage: bgImage ? `url(${bgImage})` : 'none',
          filter: 'blur(20px) brightness(0.3)',
          transform: 'scale(1.1)'
        }}
      />
      
      {/* Dark overlay */}
      <div className="absolute inset-0 bg-black/50" />
      
      {/* Modal */}
      <div 
        className="relative z-10 w-full max-w-md mx-4 overflow-hidden shadow-2xl glass-panel-static-solid"
        style={{ animation: 'fadeIn 0.3s ease-out forwards' }}
      >
        {/* Content */}
        <div className="p-8 flex flex-col items-center text-center">
          <div className="w-16 h-16 mb-6 rounded-full bg-amber-500/20 flex items-center justify-center">
            <AlertTriangle size={32} className="text-amber-400" />
          </div>
          
          <h1 className="text-2xl font-bold text-white mb-2">
            {hasMirrors
              ? onboarding.t('onboarding.warning.mirrorsTitle', 'Mirrors are enabled')
              : onboarding.t('onboarding.warning.title')}
          </h1>
          <p className="text-sm text-white/60 mb-6 max-w-sm">
            {hasMirrors
              ? onboarding.t(
                  'onboarding.warning.mirrorsDescription',
                  'You chose to continue without a Hytale account. Downloads will use your configured mirrors.'
                )
              : onboarding.t('onboarding.warning.description')}
          </p>
          
          {/* Warning box */}
          <div className="w-full p-4 rounded-xl bg-amber-500/10 border border-amber-500/30 mb-6">
            <div className="flex items-start gap-3">
              <AlertTriangle
                size={18}
                className="text-amber-400 flex-shrink-0 mt-0.5"
              />
              <div className="text-left">
                <p className="text-sm text-amber-200 font-medium mb-1">
                  {hasMirrors
                    ? onboarding.t(
                        'onboarding.warning.mirrorsActive',
                        `Mirrors detected (${enabledMirrorCount})`
                      )
                    : onboarding.t('onboarding.warning.noSources')}
                </p>
                <p className="text-xs text-amber-200/70">
                  {hasMirrors
                    ? onboarding.t(
                        'onboarding.warning.mirrorsHint',
                        'You can manage mirrors in Settings → Downloads.'
                      )
                    : onboarding.t('onboarding.warning.noSourcesHint')}
                </p>
              </div>
            </div>
          </div>
          
          {/* Buttons */}
          <div className="w-full space-y-3">
            <Button
              variant="primary"
              onClick={onboarding.handleContinueWithoutAuth}
              className="w-full px-6 py-4 rounded-xl font-semibold hover:opacity-90"
            >
              <ArrowRight size={20} />
              {onboarding.t('onboarding.warning.continue')}
            </Button>
            
            <Button
              onClick={onboarding.handleBackToAuth}
              className="w-full px-6 py-3 rounded-xl"
            >
              <ArrowLeft size={18} />
              {onboarding.t('onboarding.warning.back')}
            </Button>
          </div>
        </div>
      </div>
      
      {/* CSS animations */}
      <style>{`
        @keyframes fadeIn {
          from { opacity: 0; transform: translateY(10px); }
          to { opacity: 1; transform: translateY(0); }
        }
      `}</style>
    </div>
  );
};

export default WarningPhase;
