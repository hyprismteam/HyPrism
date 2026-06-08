import React from 'react';
import { motion, AnimatePresence } from 'framer-motion';

export interface DropdownMenuProps {
  isOpen: boolean;
  children: React.ReactNode;
  className?: string;
  /** Position of the dropdown relative to trigger */
  position?: 'left' | 'right';
  /** Width of the dropdown */
  width?: string;
  /** Max height with scroll */
  maxHeight?: string;
}

/**
 * Animated dropdown menu container.
 * Wraps menu items with smooth open/close animations.
 */
export const DropdownMenu: React.FC<DropdownMenuProps> = ({
  isOpen,
  children,
  className = '',
  position = 'right',
  width = 'w-48',
  maxHeight,
}) => {
  const positionClass = position === 'right' ? 'right-0' : 'left-0';
  const maxHeightClass = maxHeight ? `max-h-[${maxHeight}] overflow-y-auto` : '';

  return (
    <AnimatePresence>
      {isOpen && (
        <motion.div
          initial={{ opacity: 0, y: -8, scale: 0.96 }}
          animate={{ opacity: 1, y: 0, scale: 1 }}
          exit={{ opacity: 0, y: -8, scale: 0.96 }}
          transition={{ 
            duration: 0.15, 
            ease: [0.4, 0, 0.2, 1]
          }}
          className={`absolute ${positionClass} top-full mt-1 ${width} bg-[#1a1a1a] border border-white/10 rounded-xl shadow-xl z-50 overflow-hidden ${maxHeightClass} ${className}`.trim()}
          style={maxHeight ? { maxHeight } : undefined}
        >
          {children}
        </motion.div>
      )}
    </AnimatePresence>
  );
};

export default DropdownMenu;
