import React, { useState, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import { User, Shield, ArrowLeft, Loader2, Dices, Check, AlertCircle, AlertTriangle } from 'lucide-react';
import { useAccentColor } from '../contexts/AccentColorContext';
import { ipc, Profile } from '@/lib/ipc';
import { SelectionCard } from '@/components/ui/SelectionCard';
import { Button, LinkButton, IconButton } from '@/components/ui/Controls';
import { generateRandomNick } from '@/utils/randomNick';

type WizardStep = 'choose-type' | 'official-auth' | 'unofficial-name' | 'done';
type ErrorLevel = 'error' | 'warning';

/** Validation regex for offline usernames: 3–16 characters, ASCII letters, digits, `-`, and `_`. */
const NICK_REGEX = /^[a-zA-Z0-9_-]{3,16}$/;

/**
 * Props for the {@link ProfileCreationWizard} component.
 */
interface ProfileCreationWizardProps {
    onComplete: (profile: Profile) => void;
    onCancel: () => void;
    initialStep?: WizardStep;
}

/** @returns A randomly generated offline username. */
function generateRandomName(): string {
    return generateRandomNick();
}

/** Generates a random UUID v4 string. @returns A UUID v4 string in `xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx` format. */
function generateUUID(): string {
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
        const r = (Math.random() * 16) | 0;
        const v = c === 'x' ? r : (r & 0x3) | 0x8;
        return v.toString(16);
    });
}

/**
 * Multi-step wizard for creating a new launcher profile.
 * Supports official Hytale authentication and offline (unofficial) name-only profiles.
 *
 * @param props - See {@link ProfileCreationWizardProps}.
 */
export const ProfileCreationWizard: React.FC<ProfileCreationWizardProps> = ({ onComplete, onCancel, initialStep = 'choose-type' }) => {
    const { t } = useTranslation();
    const { accentColor } = useAccentColor();

    const [step, setStep] = useState<WizardStep>(initialStep);
    const [name, setName] = useState(generateRandomName());
    const [isLoading, setIsLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [errorLevel, setErrorLevel] = useState<ErrorLevel>('error');

    // #region Step: Choose Type
    const handleChooseOfficial = () => {
        setStep('official-auth');
        setError(null);
    };

    const handleChooseUnofficial = () => {
        setStep('unofficial-name');
        setError(null);
    };

    // #endregion

    // #region Step: Official Auth
    const handleStartAuth = async () => {
        setIsLoading(true);
        setError(null);
        setErrorLevel('error');
        try {
            // This will open the browser and wait for callback (up to 15 min)
            const result = await ipc.auth.login();
            if (result?.loggedIn && result.accountProfiles?.length) {
                let firstProfile: Awaited<ReturnType<typeof ipc.profile.create>> | null = null;
                for (const ap of result.accountProfiles) {
                    const profile = await ipc.profile.create({
                        name: ap.username,
                        uuid: ap.uuid,
                        isOfficial: true,
                    });
                    if (!firstProfile && profile?.id) firstProfile = profile;
                }
                if (firstProfile) {
                    onComplete(firstProfile);
                } else {
                    setError(t('profiles.wizard.createFailed'));
                    setErrorLevel('error');
                }
            } else if (result?.errorType === 'no_profile') {
                // Yellow warning — account works but has no game profiles
                setError(t('profiles.wizard.noHytaleProfile'));
                setErrorLevel('warning');
            } else {
                setError(t('profiles.wizard.authFailed'));
                setErrorLevel('error');
            }
        } catch (err) {
            console.error('[ProfileWizard] Auth failed:', err);
            setError(t('profiles.wizard.authError'));
            setErrorLevel('error');
        } finally {
            setIsLoading(false);
        }
    };

    // #endregion

    // #region Step: Unofficial Name
    const isNickValid = useMemo(() => NICK_REGEX.test(name.trim()), [name]);

    const handleCreateUnofficial = async () => {
        const trimmed = name.trim();
        if (!isNickValid) {
            setError(t('profiles.wizard.nickInvalid'));
            setErrorLevel('error');
            return;
        }

        setIsLoading(true);
        setError(null);
        try {
            const uuid = generateUUID();
            const profile = await ipc.profile.create({
                name: trimmed,
                uuid,
                isOfficial: false,
            });
            if (profile && profile.id) {
                onComplete(profile);
            } else {
                setError(t('profiles.wizard.createFailed'));
            }
        } catch (err) {
            console.error('[ProfileWizard] Create failed:', err);
            setError(t('profiles.wizard.createError'));
        } finally {
            setIsLoading(false);
        }
    };

    const handleNameKeyDown = (e: React.KeyboardEvent) => {
        if (e.key === 'Enter') handleCreateUnofficial();
        if (e.key === 'Escape') onCancel();
    };

    // Shared animation props
    const pageVariants = {
        initial: { opacity: 0, x: 40 },
        animate: { opacity: 1, x: 0 },
        exit: { opacity: 0, x: -40 },
    };

    // #endregion
    return (
        <div className="flex flex-col items-center justify-center h-full px-8 py-6">
            <AnimatePresence mode="wait">
                {/* ======== STEP: Choose Profile Type ======== */}
                {step === 'choose-type' && (
                    <motion.div
                        key="choose-type"
                        variants={pageVariants}
                        initial="initial"
                        animate="animate"
                        exit="exit"
                        transition={{ duration: 0.2 }}
                        className="flex flex-col items-center gap-6 max-w-md w-full"
                    >
                        <h2 className="text-xl font-bold text-white">
                            {t('profiles.wizard.title')}
                        </h2>
                        <p className="text-sm text-white/50 text-center">
                            {t('profiles.wizard.chooseType')}
                        </p>

                        <div className="flex flex-col gap-3 w-full">
                            {/* Official */}
                            <SelectionCard
                                onClick={handleChooseOfficial}
                                icon={<Shield size={20} style={{ color: accentColor }} />}
                                title={t('profiles.wizard.official')}
                                description={t('profiles.wizard.officialDesc')}
                            />

                            {/* Unofficial */}
                            <SelectionCard
                                onClick={handleChooseUnofficial}
                                icon={<User size={20} className="text-white opacity-70" />}
                                title={t('profiles.wizard.unofficial')}
                                description={t('profiles.wizard.unofficialDesc')}
                            />
                        </div>

                        <LinkButton
                            onClick={onCancel}
                            className="text-sm mt-2"
                        >
                            {t('common.cancel')}
                        </LinkButton>
                    </motion.div>
                )}

                {/* ======== STEP: Official Auth ======== */}
                {step === 'official-auth' && (
                    <motion.div
                        key="official-auth"
                        variants={pageVariants}
                        initial="initial"
                        animate="animate"
                        exit="exit"
                        transition={{ duration: 0.2 }}
                        className="flex flex-col items-center gap-6 max-w-md w-full"
                    >
                        <div
                            className="w-16 h-16 rounded-full flex items-center justify-center"
                            style={{ backgroundColor: `${accentColor}20` }}
                        >
                            <Shield size={32} style={{ color: accentColor }} />
                        </div>

                        <h2 className="text-xl font-bold text-white">
                            {t('profiles.wizard.authTitle')}
                        </h2>
                        <p className="text-sm text-white/50 text-center">
                            {t('profiles.wizard.authDesc')}
                        </p>

                        {error && (
                            <div className={`flex items-center gap-2 text-sm rounded-lg px-4 py-2 w-full ${
                                errorLevel === 'warning'
                                    ? 'text-yellow-400 bg-yellow-500/10 border border-yellow-500/20'
                                    : 'text-red-400 bg-red-500/10 border border-red-500/20'
                            }`}>
                                {errorLevel === 'warning' ? <AlertTriangle size={16} /> : <AlertCircle size={16} />}
                                {error}
                            </div>
                        )}

                        <Button
                            variant="primary"
                            onClick={handleStartAuth}
                            disabled={isLoading}
                            className="w-full py-3"
                        >
                            {isLoading ? (
                                <>
                                    <Loader2 size={18} className="animate-spin" />
                                    {t('profiles.wizard.waitingAuth')}
                                </>
                            ) : (
                                <>
                                    {t('profiles.wizard.loginHytale')}
                                </>
                            )}
                        </Button>

                        {isLoading && (
                            <p className="text-xs text-white/30 text-center">
                                {t('profiles.wizard.browserHint')}
                            </p>
                        )}

                        <LinkButton
                            onClick={() => { setStep('choose-type'); setError(null); }}
                            disabled={isLoading}
                            className="text-sm"
                        >
                            <ArrowLeft size={14} />
                            {t('common.back')}
                        </LinkButton>
                    </motion.div>
                )}

                {/* ======== STEP: Unofficial Name ======== */}
                {step === 'unofficial-name' && (
                    <motion.div
                        key="unofficial-name"
                        variants={pageVariants}
                        initial="initial"
                        animate="animate"
                        exit="exit"
                        transition={{ duration: 0.2 }}
                        className="flex flex-col items-center gap-6 max-w-md w-full"
                    >
                        <div className="w-16 h-16 rounded-full flex items-center justify-center bg-white/5">
                            <User size={32} className="text-white opacity-50" />
                        </div>

                        <h2 className="text-xl font-bold text-white">
                            {t('profiles.wizard.nameTitle')}
                        </h2>
                        <p className="text-sm text-white/50 text-center">
                            {t('profiles.wizard.nameDesc')}
                        </p>

                        <div className="flex items-center gap-2 w-full">
                            <input
                                type="text"
                                value={name}
                                onChange={(e) => { setName(e.target.value); setError(null); }}
                                onKeyDown={handleNameKeyDown}
                                maxLength={16}
                                autoFocus
                                placeholder={t('profiles.wizard.namePlaceholder')}
                                className="flex-1 bg-[#2c2c2e] text-white text-lg font-semibold px-4 py-3 rounded-xl border outline-none text-center"
                                style={{ borderColor: isNickValid || !name.trim() ? accentColor : '#ef4444' }}
                            />
                            <IconButton
                                onClick={() => setName(generateRandomName())}
                                title={t('profiles.generateName')}
                            >
                                <Dices size={20} />
                            </IconButton>
                        </div>

                        <p className={`text-xs ${isNickValid || !name.trim() ? 'text-white/30' : 'text-red-400/70'}`}>
                            {name.length}/16 {t('profiles.wizard.characters')} · {t('profiles.wizard.nickRules')}
                        </p>

                        {error && (
                            <div className="flex items-center gap-2 text-red-400 text-sm bg-red-500/10 border border-red-500/20 rounded-lg px-4 py-2 w-full">
                                <AlertCircle size={16} />
                                {error}
                            </div>
                        )}

                        <Button
                            variant="primary"
                            onClick={handleCreateUnofficial}
                            disabled={isLoading || !isNickValid}
                            className="w-full py-3"
                        >
                            {isLoading ? (
                                <Loader2 size={18} className="animate-spin" />
                            ) : (
                                <>
                                    <Check size={18} />
                                    {t('profiles.wizard.create')}
                                </>
                            )}
                        </Button>

                        <LinkButton
                            onClick={() => { setStep('choose-type'); setError(null); }}
                            disabled={isLoading}
                            className="text-sm"
                        >
                            <ArrowLeft size={14} />
                            {t('common.back')}
                        </LinkButton>
                    </motion.div>
                )}
            </AnimatePresence>
        </div>
    );
};

export default ProfileCreationWizard;
