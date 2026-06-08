import React from 'react';
import { motion } from 'framer-motion';

interface ModalOverlayProps {
  /** z-index class, e.g. "z-50" or "z-[200]" */
  zClass?: string;
  /** Extra classes for the outer container */
  className?: string;
  /** Click handler for backdrop dismiss */
  onClick?: (e: React.MouseEvent) => void;
  children: React.ReactNode;
}

/**
 * Full-screen modal overlay with solid background.
 *
 * Replaces the repeated pattern:
 *   `<motion.div className="fixed inset-0 bg-black/60 ...">`
 */
export const ModalOverlay: React.FC<ModalOverlayProps> = ({
  zClass = 'z-50',
  className = '',
  onClick,
  children,
}) => {

  return (
    <motion.div
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0 }}
      transition={{ duration: 0.2 }}
      className={`fixed inset-0 ${zClass} flex items-center justify-center p-8 ${
        ''
      } ${className}`}
      style={{ background: 'rgba(0, 0, 0, 0.85)' }}
      onClick={onClick}
    >
      {children}
    </motion.div>
  );
};

export default ModalOverlay;
