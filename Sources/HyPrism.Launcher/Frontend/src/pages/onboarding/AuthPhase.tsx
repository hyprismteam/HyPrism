import React from 'react';
import { LogIn, SkipForward, Loader2 } from 'lucide-react';
import { Button, LinkButton } from '@/components/ui/Controls';
import type { UseOnboardingReturn } from '@/hooks/useOnboarding';
import appIcon from '@/assets/images/logo.png';

interface AuthPhaseProps {
  onboarding: UseOnboardingReturn;
}

export const AuthPhase: React.FC<AuthPhaseProps> = ({ onboarding }) => {
  const bgImage = onboarding.getCurrentBackground();

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
          <img src={appIcon} alt="HyPrism" className="w-20 h-20 mb-6" />
          
          <h1 className="text-2xl font-bold text-white mb-2">
            {onboarding.t('onboarding.auth.title')}
          </h1>
          <p className="text-sm text-white/60 mb-8 max-w-sm">
            {onboarding.t('onboarding.auth.description')}
          </p>
          
          {/* Error message */}
          {onboarding.authError && (
            <div 
              className={`w-full p-3 rounded-xl mb-6 text-sm ${
                onboarding.authErrorType === 'warning' 
                  ? 'bg-yellow-500/20 border border-yellow-500/30 text-yellow-200' 
                  : 'bg-red-500/20 border border-red-500/30 text-red-200'
              }`}
            >
              {onboarding.authError}
            </div>
          )}
          
          {/* Buttons */}
          <div className="w-full space-y-3">
            <Button
              variant="primary"
              onClick={onboarding.handleLogin}
              disabled={onboarding.isAuthenticating}
              className="w-full px-6 py-4 rounded-xl font-semibold hover:opacity-90"
            >
              {onboarding.isAuthenticating ? (
                <Loader2 size={20} className="animate-spin" />
              ) : (
                <LogIn size={20} />
              )}
              {onboarding.isAuthenticating 
                ? onboarding.t('onboarding.auth.authenticating') 
                : onboarding.t('onboarding.auth.login')
              }
            </Button>
            
            <Button
              onClick={onboarding.handleSkipAuth}
              disabled={onboarding.isAuthenticating}
              className="w-full px-6 py-3 rounded-xl"
            >
              <SkipForward size={18} />
              {onboarding.t('onboarding.auth.skip')}
            </Button>
          </div>
        </div>
      </div>
      
      {/* Skip entire onboarding button */}
      <LinkButton
        onClick={onboarding.handleSkip}
        disabled={onboarding.isLoading || onboarding.isAuthenticating}
        className="absolute bottom-8 right-8 z-10 font-semibold text-sm hover:scale-[1.02] active:scale-[0.98] transition-all duration-150"
      >
        <SkipForward size={16} />
        {onboarding.t('onboarding.skip')}
      </LinkButton>
      
      {/* CSS animations */}
      <style>{`
        @keyframes fadeIn {
          from { opacity: 0; }
          to { opacity: 1; }
        }
      `}</style>
    </div>
  );
};
