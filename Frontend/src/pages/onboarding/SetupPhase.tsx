import React from 'react';
import { Check, ChevronRight, ArrowRight, SkipForward, Loader2 } from 'lucide-react';
import { Button, LinkButton } from '@/components/ui/Controls';
import type { UseOnboardingReturn } from '@/hooks/useOnboarding';
import appIcon from '@/assets/images/logo.png';

// Step components
import { LanguageStep } from './steps/LanguageStep';
import { ProfileStep } from './steps/ProfileStep';
import { HardwareStep } from './steps/HardwareStep';
import { VisualStep } from './steps/VisualStep';
import { AboutStep } from './steps/AboutStep';

interface SetupPhaseProps {
  onboarding: UseOnboardingReturn;
}

const CONTENT_HEIGHT = 420;

export const SetupPhase: React.FC<SetupPhaseProps> = ({ onboarding }) => {
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
      <div className="relative z-10 w-full max-w-3xl mx-4 overflow-hidden shadow-2xl glass-panel-static-solid">
        {/* Header */}
        <div className="p-6 border-b border-white/10 bg-gradient-to-r from-[#151515]/80 to-[#111111]/80">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-4">
              <img src={appIcon} alt="HyPrism" className="w-12 h-12 rounded-xl" />
              <div>
                <h1 className="text-2xl font-bold text-white">{onboarding.t('onboarding.welcome')}</h1>
                <p className="text-sm text-white/60">
                  {onboarding.isAuthenticated && onboarding.authenticatedUsername 
                    ? onboarding.t('onboarding.welcomeBack', { name: onboarding.authenticatedUsername })
                    : onboarding.t('onboarding.letsSetUp')
                  }
                </p>
              </div>
            </div>
            <p className="text-xs text-white/40">v{onboarding.launcherVersion}</p>
          </div>
          
          {/* Step indicator */}
          <div className="flex items-center gap-1 mt-6 overflow-x-auto pb-1">
            {onboarding.steps.map((step, index) => (
              <React.Fragment key={step.id}>
                <button
                  onClick={() => onboarding.setCurrentStep(step.id)}
                  className={`flex items-center gap-2 px-3 py-1.5 rounded-full transition-all whitespace-nowrap ${
                    index === onboarding.currentStepIndex 
                      ? 'bg-white/10' 
                      : index < onboarding.currentStepIndex 
                        ? 'opacity-100 hover:bg-white/5' 
                        : 'opacity-40 hover:opacity-60'
                  }`}
                  style={index === onboarding.currentStepIndex ? { backgroundColor: `${onboarding.accentColor}20`, borderColor: `${onboarding.accentColor}50` } : {}}
                >
                  <div 
                    className="w-6 h-6 rounded-full flex items-center justify-center text-xs font-medium"
                    style={{ 
                      backgroundColor: index <= onboarding.currentStepIndex ? onboarding.accentColor : 'rgba(255,255,255,0.1)',
                      color: index <= onboarding.currentStepIndex ? onboarding.accentTextColor : 'white'
                    }}
                  >
                    {index < onboarding.currentStepIndex ? <Check size={12} strokeWidth={3} /> : index + 1}
                  </div>
                  <span className={`text-sm ${index === onboarding.currentStepIndex ? 'text-white' : 'text-white/60'}`}>
                    {step.label}
                  </span>
                </button>
                {index < onboarding.steps.length - 1 && (
                  <ChevronRight size={14} className="text-white opacity-20 flex-shrink-0" />
                )}
              </React.Fragment>
            ))}
          </div>
        </div>
        
        {/* Content */}
        <div 
          className="p-6 overflow-y-auto"
          style={{ height: CONTENT_HEIGHT }}
        >
          {onboarding.currentStep === 'language' && <LanguageStep onboarding={onboarding} />}
          {onboarding.currentStep === 'profile' && <ProfileStep onboarding={onboarding} />}
          {onboarding.currentStep === 'hardware' && <HardwareStep onboarding={onboarding} />}
          {onboarding.currentStep === 'visual' && <VisualStep onboarding={onboarding} />}
          {onboarding.currentStep === 'about' && <AboutStep onboarding={onboarding} />}
        </div>
        
        {/* Footer */}
        <div className="p-6 border-t border-white/10 bg-[#0a0a0a]/80">
          <div className="flex justify-end items-center">
            <div className="flex items-center gap-3">
              {/* Back button */}
              {onboarding.currentStepIndex > 0 && (
                <Button onClick={onboarding.handlePrevStep}>
                  {onboarding.t('common.back')}
                </Button>
              )}
              
              {/* Next/Finish button */}
              {onboarding.currentStep !== 'about' ? (
                <Button
                  variant="primary"
                  onClick={onboarding.handleNextStep}
                  disabled={onboarding.currentStep === 'profile' && (!onboarding.username.trim() || onboarding.username.trim().length < 1)}
                >
                  {onboarding.t('common.continue')}
                  <ChevronRight size={18} />
                </Button>
              ) : (
                <Button
                  variant="primary"
                  onClick={onboarding.handleComplete}
                  disabled={onboarding.isLoading}
                >
                  {onboarding.isLoading ? (
                    <Loader2 size={18} className="animate-spin" />
                  ) : (
                    <>
                      <ArrowRight size={18} />
                      {onboarding.t('onboarding.enterLauncher')}
                    </>
                  )}
                </Button>
              )}
            </div>
          </div>
        </div>
      </div>
      
      {/* Skip button */}
      <LinkButton
        onClick={onboarding.handleSkip}
        disabled={onboarding.isLoading}
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
        @keyframes fadeIn {
          from { opacity: 0; }
          to { opacity: 1; }
        }
      `}</style>
    </div>
  );
};
