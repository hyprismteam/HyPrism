import React, { Suspense, lazy } from 'react';
import { motion } from 'framer-motion';
import { PageContainer } from '@/components/ui/PageContainer';
import { pageVariants } from '@/constants/animations';

const ProfileEditor = lazy(() => import('../components/ProfileEditor'));

interface ProfilesPageProps {
  onProfileUpdate?: () => void;
}

export const ProfilesPage: React.FC<ProfilesPageProps> = ({ onProfileUpdate }) => {
  return (
    <motion.div
      variants={pageVariants}
      initial="initial"
      animate="animate"
      exit="exit"
      transition={{ duration: 0.3, ease: 'easeOut' }}
      className="h-full w-full"
    >
      <PageContainer contentClassName="h-full">
      <div className="h-full flex flex-col">
      {/* Content */}
      <div className="flex-1 min-h-0">
        <Suspense fallback={
          <div className="flex items-center justify-center h-full">
            <div className="w-8 h-8 border-2 border-white/20 border-t-white rounded-full animate-spin" />
          </div>
        }>
          <ProfileEditor
            isOpen={true}
            onClose={() => {}}
            onProfileUpdate={onProfileUpdate}
            pageMode={true}
          />
        </Suspense>
      </div>
      </div>
      </PageContainer>
    </motion.div>
  );
};
