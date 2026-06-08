import { useEffect } from 'react';
import { createPortal } from 'react-dom';
import { ChevronLeft, ChevronRight } from 'lucide-react';
import { AnimatePresence, motion } from 'framer-motion';
import { useTranslation } from 'react-i18next';
import { IconButton } from './IconButton';

export function ImageLightbox({
  isOpen,
  title,
  images,
  index,
  onIndexChange,
  onClose,
}: {
  isOpen: boolean;
  title?: string;
  images: Array<{ url: string; title?: string }>;
  index: number;
  onIndexChange: (next: number) => void;
  onClose: () => void;
}) {
  const { t } = useTranslation();

  useEffect(() => {
    if (!isOpen) return;
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
      if (e.key === 'ArrowLeft') onIndexChange(Math.max(0, index - 1));
      if (e.key === 'ArrowRight') onIndexChange(Math.min(images.length - 1, index + 1));
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [isOpen, index, images.length, onClose, onIndexChange]);

  const total = images.length;
  const current = Math.min(Math.max(index, 0), Math.max(total - 1, 0));
  const currentImage = images[current];

  return createPortal(
    <AnimatePresence>
      {isOpen && (
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.2 }}
          className="fixed inset-0 z-[9999] bg-black/90 flex flex-col items-center justify-center"
          onClick={(e) => e.target === e.currentTarget && onClose()}
        >
          <motion.div
            initial={{ scale: 0.95, opacity: 0 }}
            animate={{ scale: 1, opacity: 1 }}
            exit={{ scale: 0.95, opacity: 0 }}
            transition={{ duration: 0.2 }}
            className="relative max-w-5xl max-h-[80vh] px-6"
          >
            <img
              src={currentImage?.url}
              alt={currentImage?.title ?? title ?? ''}
              className="block max-w-full max-h-[80vh] object-contain rounded-xl"
              draggable={false}
            />
          </motion.div>

          {total > 1 && (
            <div className="fixed bottom-8 left-1/2 -translate-x-1/2 flex items-center gap-3">
              <IconButton
                variant="overlay"
                size="sm"
                title={t('accessibility.previous')}
                onClick={() => onIndexChange(Math.max(0, current - 1))}
                disabled={current <= 0}
              >
                <ChevronLeft className="w-4 h-4" />
              </IconButton>

              <div className="px-3 py-2 rounded-2xl text-xs font-semibold bg-black/50 border border-white/10 text-white/80 min-w-[60px] text-center">
                {`${current + 1}/${total}`}
              </div>

              <IconButton
                variant="overlay"
                size="sm"
                title={t('accessibility.next')}
                onClick={() => onIndexChange(Math.min(total - 1, current + 1))}
                disabled={current >= total - 1}
              >
                <ChevronRight className="w-4 h-4" />
              </IconButton>
            </div>
          )}
        </motion.div>
      )}
    </AnimatePresence>,
    document.body
  );
}
