import React from 'react';
import { useOnboarding } from '@/hooks/useOnboarding';
import { SplashPhase } from './SplashPhase';
import { AuthPhase } from './AuthPhase';
import { WarningPhase } from './WarningPhase';
import { SetupPhase } from './SetupPhase';

interface OnboardingPageProps {
  onComplete: () => void;
}

export const OnboardingPage: React.FC<OnboardingPageProps> = ({ onComplete }) => {
  const onboarding = useOnboarding({ onComplete });

  // Don't render anything until ready to prevent flash
  if (!onboarding.isReady) {
    return (
      <div className="fixed inset-0 z-[100] bg-[#0a0a0a]" />
    );
  }

  // Render appropriate phase
  if (onboarding.phase === 'splash') {
    return <SplashPhase onboarding={onboarding} />;
  }

  if (onboarding.phase === 'auth') {
    return <AuthPhase onboarding={onboarding} />;
  }

  if (onboarding.phase === 'warning') {
    return <WarningPhase onboarding={onboarding} />;
  }

  // Setup phase
  return <SetupPhase onboarding={onboarding} />;
};

// Re-export as OnboardingModal for backwards compatibility
export const OnboardingModal = OnboardingPage;

export default OnboardingPage;
