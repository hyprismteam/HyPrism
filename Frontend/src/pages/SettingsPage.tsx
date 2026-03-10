import React, { Suspense, lazy } from 'react';
import { motion } from 'framer-motion';
import { PageContainer } from '@/components/ui/PageContainer';
import { LoadingSpinner } from '@/components/ui/LoadingSpinner';
import { pageVariants } from '@/constants/animations';

const SettingsPageContent = lazy(() => import('./settings/SettingsPage').then(m => ({ default: m.SettingsPage })));

interface SettingsPageProps {
  launcherBranch: string;
  onLauncherBranchChange: (branch: string) => void;
  rosettaWarning?: { message: string; command: string; tutorialUrl?: string } | null;
  onBackgroundModeChange?: (mode: string) => void;
  onInstanceDeleted?: () => void;
  onNavigateToMods?: () => void;
  onAuthSettingsChange?: () => void;
  isGameRunning?: boolean;
  onMovingDataChange?: (isMoving: boolean) => void;
}

export const SettingsPage: React.FC<SettingsPageProps> = (props) => {
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
          <div className="flex-1 min-h-0">
            <Suspense fallback={
              <div className="flex items-center justify-center h-full">
                <LoadingSpinner />
              </div>
            }>
              <SettingsPageContent
                launcherBranch={props.launcherBranch}
                onLauncherBranchChange={props.onLauncherBranchChange}
                rosettaWarning={props.rosettaWarning}
                onBackgroundModeChange={props.onBackgroundModeChange}
                onInstanceDeleted={props.onInstanceDeleted}
                onAuthSettingsChange={props.onAuthSettingsChange}
                isGameRunning={props.isGameRunning}
                onMovingDataChange={props.onMovingDataChange}
              />
            </Suspense>
          </div>
        </div>
      </PageContainer>
    </motion.div>
  );
};
