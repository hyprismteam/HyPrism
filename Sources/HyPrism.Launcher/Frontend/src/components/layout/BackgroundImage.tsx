import React, { useState, useEffect, useRef, memo } from 'react';

// Import all bg_*.jpg and bg_*.png backgrounds
const backgroundModulesJpg = import.meta.glob('../../assets/backgrounds/bg_*.jpg', { query: '?url', import: 'default', eager: true });
const backgroundModulesPng = import.meta.glob('../../assets/backgrounds/bg_*.png', { query: '?url', import: 'default', eager: true });

// Combine and sort by number
const allBackgrounds = { ...backgroundModulesJpg, ...backgroundModulesPng };
const backgroundImages = Object.entries(allBackgrounds)
  .sort(([a], [b]) => {
    const numA = parseInt(a.match(/bg_(\d+)/)?.[1] || '0');
    const numB = parseInt(b.match(/bg_(\d+)/)?.[1] || '0');
    return numA - numB;
  })
  .map(([path, url]) => ({ 
    name: path.match(/bg_(\d+)/)?.[0] || 'bg_1', 
    url: url as string 
  }));

// Create a map for quick lookup
const backgroundMap = Object.fromEntries(backgroundImages.map(bg => [bg.name, bg.url]));

// Configuration
const TRANSITION_DURATION = 1000; // 1 second crossfade
const IMAGE_DURATION = 15000; // 15 seconds per image

interface BackgroundImageProps {
  mode?: string; // 'slideshow', a specific background name like 'bg_1', or 'color:#hexcode'
}

// Simple, bulletproof "breathing" animation — scale only, no translate, no edge issues.
// Uses a single CSS @keyframes defined once, running forever via `infinite alternate`.
const BREATHE_STYLE = `
  @keyframes bg-breathe {
    0%   { transform: scale(1); }
    100% { transform: scale(1.06); }
  }
`;

export const BackgroundImage: React.FC<BackgroundImageProps> = memo(({ mode = 'slideshow' }) => {
  const [currentIndex, setCurrentIndex] = useState(() => 
    Math.floor(Math.random() * backgroundImages.length)
  );
  const [isVisible, setIsVisible] = useState(false);
  const [displayedMode, setDisplayedMode] = useState(mode);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const prevModeRef = useRef(mode);

  // Determine mode type
  const isStatic = displayedMode !== 'slideshow';
  const staticUrl = isStatic && backgroundMap[displayedMode] ? backgroundMap[displayedMode] : null;

  // Initial fade in
  useEffect(() => {
    const timer = setTimeout(() => setIsVisible(true), 100);
    return () => clearTimeout(timer);
  }, []);

  // Handle mode changes with smooth transition
  useEffect(() => {
    if (prevModeRef.current === mode) return;
    
    // Fade out current
    setIsVisible(false);
    
    const timer = setTimeout(() => {
      setDisplayedMode(mode);
      // If switching to slideshow, randomize index
      if (mode === 'slideshow') {
        setCurrentIndex(Math.floor(Math.random() * backgroundImages.length));
      }
      // Fade in new
      setTimeout(() => setIsVisible(true), 50);
    }, TRANSITION_DURATION);
    
    prevModeRef.current = mode;
    
    return () => clearTimeout(timer);
  }, [mode]);

  // Cycle through backgrounds (only in slideshow mode)
  useEffect(() => {
    if (isStatic || backgroundImages.length <= 1) return;

    const cycleBackground = () => {
      setIsVisible(false);
      
      timerRef.current = setTimeout(() => {
        setCurrentIndex(prev => (prev + 1) % backgroundImages.length);
        setIsVisible(true);
      }, TRANSITION_DURATION);
    };

    const interval = setTimeout(cycleBackground, IMAGE_DURATION);

    return () => {
      clearTimeout(interval);
      if (timerRef.current) clearTimeout(timerRef.current);
    };
  }, [currentIndex, isStatic]);

  const currentImageUrl = staticUrl || backgroundImages[currentIndex]?.url;

  return (
    <>
      {/* Single global keyframe — injected once */}
      <style>{BREATHE_STYLE}</style>

      {/* Background container */}
      <div className="absolute inset-0 overflow-hidden bg-black">
        {/* Background image with gentle breathing zoom */}
        <div
          className="absolute inset-0"
          style={{
            transition: `opacity ${TRANSITION_DURATION}ms ease-in-out`,
            opacity: isVisible ? 1 : 0,
          }}
        >
          <img
            src={currentImageUrl}
            alt=""
            className="w-full h-full object-cover"
            style={{
              transformOrigin: 'center center',
              animation: 'bg-breathe 25s ease-in-out infinite alternate',
            }}
          />
        </div>
        
        {/* Vignette effect */}
        <div 
          className="absolute inset-0 pointer-events-none"
          style={{
            background: 'radial-gradient(ellipse at center, transparent 0%, transparent 40%, rgba(0,0,0,0.5) 100%)',
          }}
        />
      </div>

      {/* Light overlay for readability */}
      <div className="absolute inset-0 bg-gradient-to-b from-black/20 via-transparent to-black/40 pointer-events-none" />
    </>
  );
});

BackgroundImage.displayName = 'BackgroundImage';
