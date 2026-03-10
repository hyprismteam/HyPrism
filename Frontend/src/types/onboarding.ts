/** The top-level phase of the first-run onboarding wizard. */
export type OnboardingPhase = 'splash' | 'auth' | 'warning' | 'setup';

/** An individual step within the setup phase of the onboarding wizard. */
export type OnboardingStep = 'language' | 'profile' | 'hardware' | 'visual' | 'about';

/** Partial onboarding state persisted to `localStorage` to survive page reloads mid-flow. */
export interface OnboardingState {
  /** Current top-level wizard phase. */
  phase: OnboardingPhase;
  /** ID of the currently active setup step. */
  currentStep: string;
  /** Offline username entered by the user. */
  username: string;
  /** Background mode selected during onboarding (e.g. `"slideshow"` or a background ID). */
  backgroundMode: string;
  /** BCP 47 language code selected during onboarding. */
  selectedLanguage: string;
  /** Whether the user has authenticated with a Hytale account. */
  isAuthenticated: boolean;
}
