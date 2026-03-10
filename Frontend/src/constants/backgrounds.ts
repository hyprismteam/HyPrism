/**
 * Background images for slideshow/wallpaper functionality.
 * Shared between SettingsModal, OnboardingModal, and other components.
 */

// Import all background images using Vite's glob import
const backgroundModulesJpg = import.meta.glob('../assets/backgrounds/bg_*.jpg', { query: '?url', import: 'default', eager: true });
const backgroundModulesPng = import.meta.glob('../assets/backgrounds/bg_*.png', { query: '?url', import: 'default', eager: true });

const allBackgrounds = { ...backgroundModulesJpg, ...backgroundModulesPng };

/** Represents a single background image entry with a display name and resolved URL. */
export interface BackgroundImage {
  /** Display name derived from the filename (e.g. `"bg_1"`). */
  name: string;
  /** Resolved asset URL suitable for use in CSS or `<img>` src. */
  url: string;
}

/**
 * Sorted array of background images
 */
export const backgroundImages: BackgroundImage[] = Object.entries(allBackgrounds)
  .sort(([a], [b]) => {
    const numA = parseInt(a.match(/bg_(\d+)/)?.[1] || '0');
    const numB = parseInt(b.match(/bg_(\d+)/)?.[1] || '0');
    return numA - numB;
  })
  .map(([path, url]) => ({
    name: path.match(/bg_(\d+)/)?.[0] || 'bg_1',
    url: url as string,
  }));

/**
 * Get a random background image from the available pool.
 * @returns A randomly selected {@link BackgroundImage}, or the first image as a fallback.
 */
export const getRandomBackground = (): BackgroundImage => {
  const idx = Math.floor(Math.random() * backgroundImages.length);
  return backgroundImages[idx] || backgroundImages[0];
};
