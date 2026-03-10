import React from 'react';
import { motion } from 'framer-motion';
import { AlertTriangle, X, Copy, RefreshCw, Bug } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { ipc } from '@/lib/ipc';
import { Button, IconButton } from '@/components/ui/Controls';

import { ModalOverlay } from './ModalOverlay';

interface ErrorModalProps {
  error: {
    type: string;
    message: string;
    technical?: string;
    timestamp?: string;
    launcherVersion?: string;
  };
  onClose: () => void;
}

export const ErrorModal: React.FC<ErrorModalProps> = ({ error, onClose }) => {
  const { t } = useTranslation();

  const [copied, setCopied] = React.useState(false);

  const copyError = () => {
    const errorText = `Error Type: ${error.type}\nMessage: ${error.message}\nTechnical: ${error.technical || 'N/A'}\nTimestamp: ${error.timestamp || new Date().toISOString()}\nLauncher Version: ${error.launcherVersion || 'Unknown'}`;
    navigator.clipboard.writeText(errorText);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  const reportIssue = () => {
    const title = encodeURIComponent(`[Bug] ${error.type}: ${error.message}`);
    const body = encodeURIComponent(
`## Description
<!-- Please describe what you were doing when the error occurred -->

## Error Details
- **Type:** ${error.type}
- **Message:** ${error.message}
- **Technical:** ${error.technical || 'N/A'}
- **Timestamp:** ${error.timestamp || new Date().toISOString()}
- **Launcher Version:** ${error.launcherVersion || 'Unknown'}

## System Info
- **Platform:** ${navigator.platform}
- **User Agent:** ${navigator.userAgent}

## Steps to Reproduce
1. 
2. 
3. 

## Additional Context
<!-- Add any other context about the problem here -->
`
    );
    const url = `https://github.com/yyyumeniku/HyPrism/issues/new?title=${title}&body=${body}&labels=bug`;
    ipc.browser.open(url);
  };

  const getErrorColor = (type: string) => {
    switch (type) {
      case 'NETWORK': return 'text-blue-400';
      case 'FILESYSTEM': return 'text-yellow-400';
      case 'VALIDATION': return 'text-orange-400';
      case 'GAME': return 'text-red-400';
      case 'UPDATE': return 'text-purple-400';
      default: return 'text-red-400';
    }
  };

  return (
    <ModalOverlay zClass="z-50" onClick={onClose}>
      <motion.div
        initial={{ scale: 0.9, opacity: 0 }}
        animate={{ scale: 1, opacity: 1 }}
        exit={{ scale: 0.9, opacity: 0 }}
        className={`w-full max-w-lg h-[700px] flex flex-col overflow-hidden glass-panel-static-solid !border-red-500/20`}
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="flex items-center justify-between p-5 border-b border-white/10 bg-red-500/5">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-xl bg-red-500/20 flex items-center justify-center">
              <AlertTriangle size={20} className="text-red-400" />
            </div>
            <div>
              <h2 className="text-lg font-bold text-white">{t('error.title')}</h2>
              <span className={`text-xs font-medium ${getErrorColor(error.type)}`}>
                {error.type}
              </span>
            </div>
          </div>
          <IconButton variant="ghost" onClick={onClose} title={t('common.close')}>
            <X size={20} />
          </IconButton>
        </div>

        {/* Content */}
        <div className="p-5 space-y-4 flex-1 overflow-y-auto">
          <div>
            <h3 className="text-white font-medium mb-1">{error.message}</h3>
            {error.technical && (
              <div className="mt-3 p-3 bg-black/50 rounded-lg border border-white/5">
                <p className="text-xs text-gray-400 font-mono break-all">
                  {error.technical}
                </p>
              </div>
            )}
          </div>

          <div className="flex items-center justify-between text-xs text-gray-500">
            {error.timestamp && (
              <p>
                {t('error.occurredAt')} {new Date(error.timestamp).toLocaleString()}
              </p>
            )}
            {error.launcherVersion && (
              <p className="text-gray-600">
                v{error.launcherVersion}
              </p>
            )}
          </div>
        </div>

        {/* Footer */}
        <div className="flex items-center justify-between p-5 border-t border-white/10 bg-black/30">
          <div className="flex gap-2">
            <IconButton
              onClick={copyError}
              title={copied ? t('common.copied') : t('error.copyError')}
              className="w-9 h-9"
            >
              <Copy size={16} />
            </IconButton>
            <IconButton
              onClick={reportIssue}
              title={t('error.reportIssue')}
              className="w-9 h-9 hover:text-orange-400"
            >
              <Bug size={16} />
            </IconButton>
            <IconButton
              onClick={() => window.location.reload()}
              title={t('common.reload')}
              className="w-9 h-9"
            >
              <RefreshCw size={16} />
            </IconButton>
          </div>

          <Button variant="danger" onClick={onClose}>
            {t('common.dismiss')}
          </Button>
        </div>
      </motion.div>
    </ModalOverlay>
  );
};
