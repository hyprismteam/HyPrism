import React from 'react';
import { motion } from 'framer-motion';
import { Home, Newspaper, Users, HardDrive, Settings, Volume2, VolumeX, type LucideIcon } from 'lucide-react';
import { useAccentColor } from '../../contexts/AccentColorContext';

import { useTranslation } from 'react-i18next';

export type PageType = 'dashboard' | 'news' | 'profiles' | 'instances' | 'logs' | 'settings';

interface DockItem {
  id: PageType;
  icon: LucideIcon;
  labelKey: string;
}

const dockItems: DockItem[] = [
  { id: 'dashboard', icon: Home, labelKey: 'dock.dashboard' },
  { id: 'news', icon: Newspaper, labelKey: 'dock.news' },
  { id: 'profiles', icon: Users, labelKey: 'dock.profiles' },
  { id: 'instances', icon: HardDrive, labelKey: 'dock.instances' },
  { id: 'settings', icon: Settings, labelKey: 'dock.settings' },
];

interface DockMenuProps {
  activePage: PageType;
  onPageChange: (page: PageType) => void;
  isMuted?: boolean;
  onToggleMute?: () => void;
}

export const DockMenu: React.FC<DockMenuProps> = ({ activePage, onPageChange, isMuted = false, onToggleMute }) => {
  const { accentColor } = useAccentColor();

  const { t } = useTranslation();

  const dockStyle: React.CSSProperties = {
    background: 'rgba(28, 28, 30, 0.98)',
    border: '1px solid rgba(255,255,255,0.08)',
    boxShadow: '0 8px 32px rgba(0,0,0,0.5)',
  };

  return (
    <div className="absolute bottom-6 left-1/2 -translate-x-1/2 z-50 flex items-center gap-3">
      {/* Main Menu */}
      <motion.div
        initial={{ y: 60, opacity: 0 }}
        animate={{ y: 0, opacity: 1 }}
        transition={{ type: 'spring', damping: 22, stiffness: 180, delay: 0.3 }}
        className="flex items-center gap-0.5 px-2 py-1.5 rounded-2xl"
        style={dockStyle}
      >
        {dockItems.map((item) => {
          const isActive = activePage === item.id;
          const Icon = item.icon;
          return (
            <button
              key={item.id}
              onClick={() => onPageChange(item.id)}
              className="relative flex flex-col items-center gap-0.5 px-3.5 py-1.5 rounded-xl transition-colors duration-200 group cursor-pointer select-none"
            >
              {isActive && (
                <motion.div
                  layoutId="dock-active-bg"
                  className="absolute inset-0 rounded-xl"
                  style={{
                    background: `linear-gradient(135deg, ${accentColor}22 0%, ${accentColor}0d 100%)`,
                    border: `1px solid ${accentColor}40`,
                    boxShadow: `0 0 16px ${accentColor}18`,
                  }}
                  transition={{ type: 'spring', damping: 28, stiffness: 320 }}
                />
              )}
              <motion.div
                whileHover={{ scale: 1.15, y: -1 }}
                whileTap={{ scale: 0.9 }}
                className="relative z-10 transition-[color,opacity] duration-200"
                style={{ color: isActive ? accentColor : '#ffffff', opacity: isActive ? 1 : 0.45 }}
              >
                <Icon size={20} />
              </motion.div>
              <span
                className="text-[10px] font-medium relative z-10 transition-colors duration-200"
                style={{ color: isActive ? accentColor : 'rgba(255,255,255,0.35)' }}
              >
                {t(item.labelKey)}
              </span>
              {!isActive && (
                <div
                  className="absolute inset-0 rounded-xl opacity-0 group-hover:opacity-100 transition-opacity duration-150"
                  style={{ background: 'rgba(255,255,255,0.05)' }}
                />
              )}
            </button>
          );
        })}
      </motion.div>

      {/* Mute Button Block */}
      {onToggleMute && (
        <motion.div
          initial={{ y: 60, opacity: 0 }}
          animate={{ y: 0, opacity: 1 }}
          transition={{ type: 'spring', damping: 22, stiffness: 180, delay: 0.4 }}
          className="flex items-center px-1.5 py-1.5 rounded-2xl"
          style={dockStyle}
        >
          <button
            onClick={onToggleMute}
            className="relative flex flex-col items-center gap-0.5 px-3.5 py-1.5 rounded-xl transition-colors duration-200 group cursor-pointer select-none"
          >
            <motion.div
              whileHover={{ scale: 1.15, y: -1 }}
              whileTap={{ scale: 0.9 }}
              className="relative z-10 transition-colors duration-200"
            >
              {isMuted ? <VolumeX size={20} className="text-red-500" /> : <Volume2 size={20} className="text-green-500" />}
            </motion.div>
            <span
              className="text-[10px] font-medium relative z-10 transition-colors duration-200 text-white/35 group-hover:text-white/60"
            >
              {t('music.on')}
            </span>
            <div
              className="absolute inset-0 rounded-xl opacity-0 group-hover:opacity-100 transition-opacity duration-150"
              style={{ background: 'rgba(255,255,255,0.05)' }}
            />
          </button>
        </motion.div>
      )}
    </div>
  );
};
