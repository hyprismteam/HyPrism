import React from 'react';
import { Check, Palette, Image } from 'lucide-react';
import { ACCENT_COLORS } from '@/constants/colors';
import { backgroundImages } from '@/constants/backgrounds';
import type { UseOnboardingReturn } from '@/hooks/useOnboarding';

interface VisualStepProps {
  onboarding: UseOnboardingReturn;
}

export const VisualStep: React.FC<VisualStepProps> = ({ onboarding }) => {
  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-xl font-semibold text-white mb-2">{onboarding.t('onboarding.customizeAppearance')}</h2>
        <p className="text-sm text-white/60">{onboarding.t('onboarding.chooseAccentAndBg')}</p>
      </div>
      
      {/* Accent Color */}
      <div>
        <label className="text-sm text-white/60 mb-3 flex items-center gap-2">
          <Palette size={14} />
          {onboarding.t('onboarding.accentColor')}
        </label>
        <div className="flex flex-wrap gap-3">
          {ACCENT_COLORS.map((color) => (
            <button
              key={color}
              onClick={() => onboarding.setAccentColor(color)}
              className={`w-10 h-10 rounded-full transition-all ${
                onboarding.accentColor === color 
                  ? 'ring-2 ring-white ring-offset-2 ring-offset-[#111111]' 
                  : 'hover:scale-110'
              }`}
              style={{ backgroundColor: color }}
            >
              {onboarding.accentColor === color && (
                <div className="w-full h-full flex items-center justify-center">
                  <Check 
                    size={18} 
                    className={color === '#FFFFFF' ? 'text-black' : 'text-white'} 
                    strokeWidth={3} 
                  />
                </div>
              )}
            </button>
          ))}
        </div>
      </div>
      
      {/* Background Chooser */}
      <div>
        <label className="text-sm text-white/60 mb-3 flex items-center gap-2">
          <Image size={14} />
          {onboarding.t('onboarding.background')}
        </label>
        
        {/* Slideshow option */}
        <div 
          className="p-3 rounded-xl border cursor-pointer transition-colors mb-3"
          style={{
            backgroundColor: onboarding.backgroundMode === 'slideshow' ? `${onboarding.accentColor}20` : 'rgba(26,26,26,0.8)',
            borderColor: onboarding.backgroundMode === 'slideshow' ? `${onboarding.accentColor}50` : 'rgba(255,255,255,0.1)'
          }}
          onClick={() => onboarding.handleBackgroundModeChange('slideshow')}
        >
          <div className="flex items-center gap-3">
            <div 
              className="w-5 h-5 rounded-full border-2 flex items-center justify-center"
              style={{ borderColor: onboarding.backgroundMode === 'slideshow' ? onboarding.accentColor : 'rgba(255,255,255,0.3)' }}
            >
              {onboarding.backgroundMode === 'slideshow' && (
                <div className="w-2.5 h-2.5 rounded-full" style={{ backgroundColor: onboarding.accentColor }} />
              )}
            </div>
            <div>
              <span className="text-white text-sm font-medium">{onboarding.t('onboarding.slideshow')}</span>
              <p className="text-xs text-white/40">{onboarding.t('onboarding.slideshowHint')}</p>
            </div>
          </div>
        </div>
        
        {/* Background grid */}
        <p className="text-xs text-white/40 mb-2">{onboarding.t('onboarding.staticBackground')}</p>
        <div className="grid grid-cols-4 gap-2">
          {backgroundImages.slice(0, 8).map((bg) => (
            <div
              key={bg.name}
              className="relative aspect-video rounded-lg overflow-hidden cursor-pointer border-2 transition-all hover:opacity-100"
              style={{
                borderColor: onboarding.backgroundMode === bg.name ? onboarding.accentColor : 'transparent',
                boxShadow: onboarding.backgroundMode === bg.name ? `0 0 0 2px ${onboarding.accentColor}30` : 'none',
                opacity: onboarding.backgroundMode === bg.name ? 1 : 0.7
              }}
              onClick={() => onboarding.handleBackgroundModeChange(bg.name)}
            >
              <img 
                src={bg.url} 
                alt={bg.name}
                className="w-full h-full object-cover"
              />
              {onboarding.backgroundMode === bg.name && (
                <div className="absolute inset-0 flex items-center justify-center bg-black/30">
                  <Check size={20} className="text-white" />
                </div>
              )}
            </div>
          ))}
        </div>
      </div>
    </div>
  );
};
