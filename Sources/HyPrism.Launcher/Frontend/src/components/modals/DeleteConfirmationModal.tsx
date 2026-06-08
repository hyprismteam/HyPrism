import React from 'react';
import { motion } from 'framer-motion';
import { Trash2, AlertTriangle } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { ModalOverlay } from './ModalOverlay';
import { Button, ModalFooterActions } from '@/components/ui/Controls';

interface DeleteConfirmationModalProps {
  onConfirm: () => void;
  onCancel: () => void;
}

export const DeleteConfirmationModal: React.FC<DeleteConfirmationModalProps> = ({
  onConfirm,
  onCancel
}) => {
  const { t } = useTranslation();

  return (
    <ModalOverlay zClass="z-50" onClick={onCancel}>
      <motion.div
        initial={{ scale: 0.9, opacity: 0 }}
        animate={{ scale: 1, opacity: 1 }}
        exit={{ scale: 0.9, opacity: 0 }}
        className={`w-full max-w-md overflow-hidden glass-panel-static-solid !border-red-500/20`}
        onClick={(e) => e.stopPropagation()}
      >
        {/* Icon */}
        <div className="flex flex-col items-center pt-8 pb-4">
          <div className="w-16 h-16 rounded-full bg-red-500/10 flex items-center justify-center mb-4">
            <Trash2 size={28} className="text-red-400" />
          </div>
          <h2 className="text-xl font-bold text-white">{t('deleteGame.title')}</h2>
        </div>

        {/* Content */}
        <div className="px-6 pb-6">
          <div className="p-4 bg-red-500/5 border border-red-500/20 rounded-xl">
            <div className="flex gap-3">
              <AlertTriangle size={18} className="text-red-400 flex-shrink-0 mt-0.5" />
              <div>
                <p className="text-sm text-gray-300">
                  {t('deleteGame.warning')}
                </p>
                <ul className="mt-2 text-xs text-gray-400 space-y-1 list-disc list-inside">
                  <li>{t('deleteGame.gameInstallation')}</li>
                  <li>{t('deleteGame.downloadedPatches')}</li>
                  <li>{t('deleteGame.cacheFiles')}</li>
                </ul>
                <p className="mt-3 text-xs text-gray-400">
                  {t('deleteGame.preserveNote')}
                </p>
              </div>
            </div>
          </div>
        </div>

        {/* Footer */}
        <ModalFooterActions>
          <Button onClick={onCancel} className="flex-1">
            {t('common.cancel')}
          </Button>
          <Button variant="danger" onClick={onConfirm} className="flex-1 bg-red-500 text-white font-bold hover:bg-red-600">
            <Trash2 size={16} />
            {t('common.delete')}
          </Button>
        </ModalFooterActions>
      </motion.div>
    </ModalOverlay>
  );
};
