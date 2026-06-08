import React from 'react';
import { useTranslation } from 'react-i18next';
import { Check } from 'lucide-react';
import { ACCENT_COLORS } from '@/constants/colors';
import { backgroundImages } from '@/constants/backgrounds';

interface VisualTabProps {
  accentColor: string;
  handleAccentColorChange: (color: string) => void;
  backgroundMode: string;
  handleBackgroundModeChange: (mode: string) => void;
}

export const VisualTab: React.FC<VisualTabProps> = ({
  accentColor,
  handleAccentColorChange,
  backgroundMode,
  handleBackgroundModeChange,
}) => {
  const { t } = useTranslation();

  return (
    <div className="space-y-6">
      {/* Accent Color Chooser */}
      <div>
        <label className="block text-sm text-white/60 mb-3">{t('settings.visualSettings.accentColor')}</label>
        <div className="flex flex-wrap gap-2">
          {ACCENT_COLORS.map((color) => (
            <button
              key={color}
              onClick={() => handleAccentColorChange(color)}
              className={`w-8 h-8 rounded-full transition-all ${accentColor === color ? 'ring-2 ring-white ring-offset-2 ring-offset-[#1a1a1a]' : 'hover:scale-110'}`}
              style={{ backgroundColor: color }}
            />
          ))}
        </div>
      </div>

      {/* Background Chooser */}
      <div>
        <label className="block text-sm text-white/60 mb-3">{t('settings.visualSettings.background')}</label>

        {/* Slideshow option */}
        <div
          className="p-3 rounded-xl border cursor-pointer transition-colors mb-3"
          style={{
            backgroundColor: backgroundMode === 'slideshow' ? `${accentColor}20` : '#151515',
            borderColor: backgroundMode === 'slideshow' ? `${accentColor}50` : 'rgba(255,255,255,0.1)'
          }}
          onClick={() => handleBackgroundModeChange('slideshow')}
        >
          <div className="flex items-center gap-3">
            <div
              className="w-5 h-5 rounded-full border-2 flex items-center justify-center"
              style={{ borderColor: backgroundMode === 'slideshow' ? accentColor : 'rgba(255,255,255,0.3)' }}
            >
              {backgroundMode === 'slideshow' && <div className="w-2.5 h-2.5 rounded-full" style={{ backgroundColor: accentColor }} />}
            </div>
            <div>
              <span className="text-white text-sm font-medium">{t('settings.visualSettings.slideshow')}</span>
              <p className="text-xs text-white/40">{t('settings.visualSettings.slideshowHint')}</p>
            </div>
          </div>
        </div>

        {/* Background grid */}
        <p className="text-xs text-white/40 mb-2">{t('settings.visualSettings.staticBackground')}</p>
        <div className="max-h-[280px] overflow-y-auto rounded-xl">
          <div className="grid grid-cols-4 gap-2 pr-1">
            {backgroundImages.map((bg) => (
              <div
                key={bg.name}
                className="relative aspect-video rounded-lg overflow-hidden cursor-pointer border border-white/[0.06] transition-all hover:border-white/20"
                style={
                  backgroundMode === bg.name
                    ? { boxShadow: `inset 0 0 0 2px ${accentColor}` }
                    : undefined
                }
                onClick={() => handleBackgroundModeChange(bg.name)}
              >
                <img
                  src={bg.url}
                  alt={bg.name}
                  className="w-full h-full object-cover"
                />
                {backgroundMode === bg.name && (
                  <div className="absolute inset-0 flex items-center justify-center" style={{ backgroundColor: `${accentColor}30` }}>
                    <Check size={20} className="text-white drop-shadow-lg" />
                  </div>
                )}
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
};
