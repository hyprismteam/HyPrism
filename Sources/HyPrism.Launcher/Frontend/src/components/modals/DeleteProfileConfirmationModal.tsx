import React from 'react';
import { motion } from 'framer-motion';
import { Trash2, AlertTriangle, X } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useAccentColor } from '../../contexts/AccentColorContext';
import { Button, IconButton, ModalFooterActions } from '@/components/ui/Controls';

import { ModalOverlay } from './ModalOverlay';

interface DeleteProfileConfirmationModalProps {
  profileName: string;
  onConfirm: () => void;
  onCancel: () => void;
}

export const DeleteProfileConfirmationModal: React.FC<DeleteProfileConfirmationModalProps> = ({
  profileName,
  onConfirm,
  onCancel
}) => {
  const { t } = useTranslation();
  const { accentColor } = useAccentColor();

  
  return (
    <ModalOverlay zClass="z-[250]" onClick={onCancel}>
      <motion.div
        initial={{ scale: 0.9, opacity: 0 }}
        animate={{ scale: 1, opacity: 1 }}
        exit={{ scale: 0.9, opacity: 0 }}
        className={`w-full max-w-md overflow-hidden glass-panel-static-solid !border-red-500/20`}
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="flex items-center justify-between p-4 border-b border-white/10">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-full bg-red-500/10 flex items-center justify-center">
              <Trash2 size={20} className="text-red-400" />
            </div>
            <h2 className="text-lg font-bold text-white">{t('deleteProfile.title')}</h2>
          </div>
          <IconButton variant="ghost" size="sm" onClick={onCancel} title={t('common.close')}>
            <X size={18} />
          </IconButton>
        </div>

        {/* Content */}
        <div className="p-6">
          <div className="flex items-center gap-3 mb-4">
            <span className="text-white/60">{t('deleteProfile.profile')}</span>
            <span className="text-white font-medium" style={{ color: accentColor }}>{profileName}</span>
          </div>
          
          <div className="p-4 bg-red-500/5 border border-red-500/20 rounded-xl">
            <div className="flex gap-3">
              <AlertTriangle size={18} className="text-red-400 flex-shrink-0 mt-0.5" />
              <div>
                <p className="text-sm text-gray-300">
                  {t('deleteProfile.warning')}
                </p>
                <ul className="mt-2 text-xs text-gray-400 space-y-1 list-disc list-inside">
                  <li>{t('deleteProfile.profileSettings')}</li>
                  <li>{t('deleteProfile.installedMods')}</li>
                  <li>{t('deleteProfile.savedGameData')}</li>
                  <li>{t('deleteProfile.skinAndAvatar')}</li>
                </ul>
                <p className="mt-3 text-xs text-red-400/80 font-medium">
                  {t('deleteProfile.cannotUndo')}
                </p>
              </div>
            </div>
          </div>
        </div>

        {/* Footer */}
        <ModalFooterActions>
          <Button onClick={onCancel} className="flex-1">
            {t('deleteProfile.cancel')}
          </Button>
          <Button variant="danger" onClick={onConfirm} className="flex-1 bg-red-500 text-white font-bold hover:bg-red-600">
            <Trash2 size={16} />
            {t('deleteProfile.delete')}
          </Button>
        </ModalFooterActions>
      </motion.div>
    </ModalOverlay>
  );
};

export default DeleteProfileConfirmationModal;
