import { HardDrive, Loader2, Wifi } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';
import { useAccentColor } from '@/contexts/AccentColorContext';

/**
 * A mirror / download-source speed-test card.
 *
 * Shows the mirror name, description, hostname, and a speed-test button
 * with animated result badge.
 */
export interface SpeedTestResult {
  mirrorId: string;
  mirrorUrl: string;
  mirrorName: string;
  pingMs: number;
  speedMBps: number;
  isAvailable: boolean;
  testedAt: string;
}

export interface MirrorSpeedCardProps {
  /** Display name (e.g. "EstroGen Mirror") */
  name: string;
  /** Secondary description */
  description?: string;
  /** Hostname shown in monospace (e.g. "licdn.estrogen.cat") */
  hostname: string;
  /** Current speed-test result, or null */
  speedTest: SpeedTestResult | null;
  /** Whether a test is currently running */
  isTesting: boolean;
  /** Called when the user clicks the test button */
  onTest: () => void;
  /** Label for the test button (e.g. "Test speed") */
  testLabel: string;
  /** Label shown while testing (e.g. "Testing...") */
  testingLabel: string;
  /** Label for unavailable result */
  unavailableLabel: string;
  /** Whether the mirror is disabled */
  disabled?: boolean;
  className?: string;
}

export function MirrorSpeedCard({
  name,
  description,
  hostname,
  speedTest,
  isTesting,
  onTest,
  testLabel,
  testingLabel,
  unavailableLabel,
  disabled = false,
  className = '',
}: MirrorSpeedCardProps) {
  const { accentColor } = useAccentColor();

  return (
    <div
      className={`p-3 rounded-xl border cursor-default transition-colors ${disabled ? 'opacity-50' : ''} ${className}`.trim()}
      style={{
        backgroundColor: '#151515',
        borderColor: 'rgba(255,255,255,0.08)',
      }}
    >
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <div
            className="w-10 h-10 rounded-lg flex items-center justify-center flex-shrink-0"
            style={{ backgroundColor: `${accentColor}20` }}
          >
            <HardDrive size={20} style={{ color: accentColor }} />
          </div>
          <div>
            <div className="text-white text-sm font-medium">{name}</div>
            {description && <div className="text-[11px] text-white/40 mt-0.5">{description}</div>}
            <code className="text-[10px] text-white/30 mt-1 block font-mono">{hostname}</code>
          </div>
        </div>
        <div className="flex items-center gap-2 relative">
          <AnimatePresence mode="wait">
            {speedTest && !isTesting && (
              <motion.div
                key={`speed-badge-${name}`}
                initial={{ opacity: 0, x: 20, scale: 0.9 }}
                animate={{ opacity: 1, x: 0, scale: 1 }}
                exit={{ opacity: 0, x: 20, scale: 0.9 }}
                transition={{ duration: 0.2, ease: 'easeOut' }}
                className={`flex items-center gap-2 px-3 h-10 rounded-full text-xs ${
                  speedTest.isAvailable
                    ? 'bg-green-500/20 text-green-400'
                    : 'bg-red-500/20 text-red-400'
                }`}
              >
                {speedTest.isAvailable ? (
                  <>
                    <span>{speedTest.pingMs}ms</span>
                    <span>•</span>
                    <span>{speedTest.speedMBps.toFixed(1)} MB/s</span>
                  </>
                ) : (
                  <span>{unavailableLabel}</span>
                )}
              </motion.div>
            )}
          </AnimatePresence>
          <div className="flex rounded-full overflow-hidden glass-control-solid">
            <button
              onClick={onTest}
              disabled={isTesting}
              className="h-10 px-4 flex items-center justify-center text-white/60 hover:text-white hover:bg-white/5 transition-colors disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-transparent disabled:hover:text-white/60"
            >
              {isTesting ? <Loader2 size={16} className="animate-spin" /> : <Wifi size={16} />}
              <span className="ml-2 text-sm">{isTesting ? testingLabel : testLabel}</span>
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
