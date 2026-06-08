import React from 'react';
import { ArrowRight, SkipForward } from 'lucide-react';
import { Button, LinkButton } from '@/components/ui/Controls';
import type { UseOnboardingReturn } from '@/hooks/useOnboarding';
import appIcon from '@/assets/images/logo.png';

interface SplashPhaseProps {
  onboarding: UseOnboardingReturn;
}

export const SplashPhase: React.FC<SplashPhaseProps> = ({ onboarding }) => {
  const bgImage = onboarding.getCurrentBackground();

  return (
    <div className="fixed inset-0 z-[100] flex flex-col items-center justify-center overflow-hidden">
      {/* Background with blur */}
      <div 
        className="absolute inset-0 bg-cover bg-center transition-all duration-1000"
        style={{ 
          backgroundImage: bgImage ? `url(${bgImage})` : 'none',
          filter: 'blur(16px) brightness(0.5)',
          transform: 'scale(1.1)'
        }}
      />
      
      {/* Dark overlay */}
      <div className="absolute inset-0 bg-black/40" />
      
      {/* Content */}
      <div className="relative z-10 flex flex-col items-center">
        {/* Animated logo */}
        <img 
          src={appIcon} 
          alt="HyPrism" 
          className="w-32 h-32 mb-6"
          style={{
            animation: 'bounceIn 1s ease-out forwards',
          }}
        />
        <h1 
          className="text-5xl font-bold text-white mb-2"
          style={{ 
            animation: 'slideUp 0.8s ease-out forwards',
            animationDelay: '0.3s',
            opacity: 0
          }}
        >
          HyPrism
        </h1>
        <p 
          className="text-lg text-white/60 mb-2"
          style={{ 
            animation: 'slideUp 0.8s ease-out forwards',
            animationDelay: '0.5s',
            opacity: 0
          }}
        >
          {onboarding.t('onboarding.unofficial')}
        </p>
        <p 
          className="text-sm text-white/40"
          style={{ 
            animation: 'slideUp 0.8s ease-out forwards',
            animationDelay: '0.7s',
            opacity: 0
          }}
        >
          v{onboarding.launcherVersion}
        </p>
        
        {/* Continue button */}
        <div 
          className="mt-12 flex flex-col items-center gap-4"
          style={{ 
            opacity: onboarding.splashAnimationComplete ? 1 : 0,
            transition: 'opacity 0.5s ease-out',
            pointerEvents: onboarding.splashAnimationComplete ? 'auto' : 'none'
          }}
        >
          <Button
            variant="primary"
            onClick={onboarding.handleEnterAuth}
            className="px-8 py-4 text-lg rounded-2xl font-semibold hover:scale-105 hover:shadow-lg"
          >
            {onboarding.t('common.continue')}
            <ArrowRight size={22} />
          </Button>
        </div>
      </div>
      
      {/* Skip button */}
      <LinkButton
        onClick={onboarding.handleSkip}
        className="absolute bottom-8 right-8 z-10 font-semibold text-sm hover:scale-[1.02] active:scale-[0.98] transition-all duration-150"
      >
        <SkipForward size={16} />
        {onboarding.t('onboarding.skip')}
      </LinkButton>
      
      {/* CSS animations */}
      <style>{`
        @keyframes bounceIn {
          0% { opacity: 0; transform: scale(0.3); }
          50% { opacity: 1; transform: scale(1.05); }
          70% { transform: scale(0.95); }
          100% { transform: scale(1); }
        }
        @keyframes slideUp {
          from { opacity: 0; transform: translateY(20px); }
          to { opacity: 1; transform: translateY(0); }
        }
      `}</style>
    </div>
  );
};
