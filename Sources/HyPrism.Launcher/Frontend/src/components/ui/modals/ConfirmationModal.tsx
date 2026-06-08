import React from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { Loader2 } from 'lucide-react';
import { Button } from '../controls/Button';

export interface ConfirmationModalProps {
  /** Whether the modal is open */
  isOpen: boolean;
  /** Called when the modal should close */
  onClose: () => void;
  /** Called when the user confirms the action */
  onConfirm: () => void;
  /** Modal title */
  title: string;
  /** Modal message/content */
  message: React.ReactNode;
  /** Text for the confirm button */
  confirmText?: string;
  /** Text for the cancel button */
  cancelText?: string;
  /** Visual variant for the confirm button */
  variant?: 'danger' | 'primary';
  /** Whether the confirm action is in progress */
  isLoading?: boolean;
  /** Icon to show next to the title */
  icon?: React.ReactNode;
  /** Maximum width class */
  maxWidth?: 'sm' | 'md' | 'lg' | '4xl';
}

const maxWidthClasses = {
  sm: 'max-w-sm',
  md: 'max-w-md',
  lg: 'max-w-lg',
  '4xl': 'max-w-4xl',
};

/**
 * A reusable confirmation modal component.
 * Replaces repetitive inline confirmation dialogs throughout the app.
 */
export const ConfirmationModal: React.FC<ConfirmationModalProps> = ({
  isOpen,
  onClose,
  onConfirm,
  title,
  message,
  confirmText = 'Confirm',
  cancelText = 'Cancel',
  variant = 'primary',
  isLoading = false,
  icon,
  maxWidth = 'sm',
}) => {
  return (
    <AnimatePresence>
      {isOpen && (
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          className="fixed inset-0 z-[300] flex items-center justify-center bg-[#0a0a0a]/90"
          onClick={(e) => e.target === e.currentTarget && !isLoading && onClose()}
        >
          <motion.div
            initial={{ scale: 0.95, opacity: 0 }}
            animate={{ scale: 1, opacity: 1 }}
            exit={{ scale: 0.95, opacity: 0 }}
            className={`p-6 ${maxWidthClasses[maxWidth]} w-full mx-4 shadow-2xl glass-panel-static-solid`}
          >
            <h3 className="text-white font-bold text-lg mb-2 flex items-center gap-2">
              {icon}
              {title}
            </h3>
            <div className="text-white/60 text-sm mb-4">
              {message}
            </div>
            <div className="flex gap-2 justify-end">
              <Button onClick={onClose} disabled={isLoading}>
                {cancelText}
              </Button>
              <Button
                variant={variant === 'danger' ? 'danger' : 'primary'}
                onClick={onConfirm}
                disabled={isLoading}
              >
                {isLoading && <Loader2 size={14} className="animate-spin" />}
                {confirmText}
              </Button>
            </div>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
};

export default ConfirmationModal;
