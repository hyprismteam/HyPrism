import React, { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { X, RefreshCw, Check, User, Edit3, Copy, CheckCircle, Plus, Trash2, Dices, FolderOpen, CopyPlus, Lock, Globe, Unplug } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';
import { useAccentColor } from '../contexts/AccentColorContext';
import { Button, IconButton, MenuItemButton } from '@/components/ui/Controls';

import { ipc, Profile } from '@/lib/ipc';
import { DeleteProfileConfirmationModal } from './modals/DeleteProfileConfirmationModal';
import { ProfileCreationWizard } from './ProfileCreationWizard';
import { generateRandomNick } from '@/utils/randomNick';

// ── IPC wrappers (all backed by real channels now) ──

async function GetNick(): Promise<string> {
  const p = await ipc.profile.get();
  return p.nick ?? 'HyPrism';
}
async function SetNick(name: string): Promise<boolean> {
  const r = await ipc.profile.setNick(name);
  return r.success;
}
async function GetUUID(): Promise<string> {
  const p = await ipc.profile.get();
  return p.uuid ?? '';
}
async function SetUUID(uuid: string): Promise<boolean> {
  const r = await ipc.profile.setUuid(uuid);
  return r.success;
}
async function GetAvatarPreview(): Promise<string | null> {
  const p = await ipc.profile.get();
  return p.avatarPath ?? null;
}
async function GetAvatarPreviewForUUID(uuid: string): Promise<string | null> {
  if (!uuid) return null;
  const path = await ipc.profile.avatarForUuid(uuid);
  return path || null;
}
async function GetProfiles(): Promise<Profile[]> {
  return ipc.profile.list();
}
async function GetActiveProfileIndex(): Promise<number> {
  return ipc.profile.activeIndex();
}
async function DeleteProfile(id: string): Promise<boolean> {
  const r = await ipc.profile.delete(id);
  return r.success;
}
async function SwitchProfile(id: string): Promise<boolean> {
  const r = await ipc.profile.switch({ id });
  return r.success;
}
async function SaveCurrentAsProfile(): Promise<void> {
  await ipc.profile.save();
}
async function OpenCurrentProfileFolder(): Promise<void> {
  ipc.profile.openFolder();
}
async function DuplicateProfileWithoutData(id: string): Promise<Profile | null> {
  const p = await ipc.profile.duplicate(id);
  return (p && p.id) ? p : null;
}

interface ProfileEditorProps {
    isOpen: boolean;
    onClose: () => void;
    onProfileUpdate?: () => void;
    pageMode?: boolean;
}

function generateRandomName(): string {
    return generateRandomNick();
}

export const ProfileEditor: React.FC<ProfileEditorProps> = ({ isOpen, onClose, onProfileUpdate, pageMode: isPageMode = false }) => {
    const { t } = useTranslation();
    const { accentColor } = useAccentColor();
  
    const [uuid, setUuid] = useState<string>('');
    const [username, setUsernameState] = useState<string>('');
    const [isLoading, setIsLoading] = useState(true);
    const [isEditingUsername, setIsEditingUsername] = useState(false);
    const [isEditingUuid, setIsEditingUuid] = useState(false);
    const [editUsername, setEditUsername] = useState('');
    const [editUuid, setEditUuid] = useState('');
    const [copiedUuid, setCopiedUuid] = useState(false);
    const [saveStatus, setSaveStatus] = useState<'idle' | 'saving' | 'saved'>('idle');
    const [localAvatar, setLocalAvatar] = useState<string | null>(null);
    
    // Profile management state
    const [profiles, setProfiles] = useState<Profile[]>([]);
    const [profileAvatars, setProfileAvatars] = useState<Record<string, string | null>>({});
    const [currentProfileIndex, setCurrentProfileIndex] = useState<number>(-1);
    
    // New profile creation flow - wizard mode
    const [isCreatingNewProfile, setIsCreatingNewProfile] = useState(false);
    
    // Wizard active = show wizard in right panel instead of profile details
    const [showWizard, setShowWizard] = useState(false);
    const [wizardInitialStep, setWizardInitialStep] = useState<'choose-type'|'official-auth' | 'unofficial-name'>('choose-type');
    const [isCreateMenuOpen, setIsCreateMenuOpen] = useState(false);

    // Delete confirmation modal state
    const [deleteConfirmation, setDeleteConfirmation] = useState<{ id: string; name: string } | null>(null);

    // Whether the current profile is an official Hytale account (locked editing)
    const isCurrentOfficial = profiles[currentProfileIndex]?.isOfficial === true;

    // Load profiles and their avatars
    const loadProfiles = useCallback(async () => {
        try {
            const [profileList, activeIndex] = await Promise.all([
                GetProfiles(),
                GetActiveProfileIndex()
            ]);
            setProfiles(profileList || []);
            setCurrentProfileIndex(activeIndex);
            
            // Load avatars for all profiles - explicitly set null for profiles without avatars
            const avatars: Record<string, string | null> = {};
            for (const profile of profileList || []) {
                try {
                    const avatar = await GetAvatarPreviewForUUID(profile.uuid ?? '');
                    // Explicitly set null if no avatar returned (new profiles have no avatar)
                    avatars[profile.uuid ?? ''] = avatar || null;
                } catch {
                    avatars[profile.uuid ?? ''] = null;
                }
            }
            setProfileAvatars(avatars);
        } catch (err) {
            console.error('Failed to load profiles:', err);
        }
    }, []);

    // Load avatar preview
    const loadAvatar = useCallback(async () => {
        try {
            const avatar = await GetAvatarPreview();
            // Only set avatar if one exists - don't preserve old avatar
            setLocalAvatar(avatar || null);
        } catch (err) {
            console.error('Failed to load avatar:', err);
            setLocalAvatar(null);
        }
    }, []);

    useEffect(() => {
        if (isOpen) {
            const initializeEditor = async () => {
                await loadProfile();
                await loadAvatar();
                // Ensure current profile is saved to the profiles list
                try {
                    await SaveCurrentAsProfile();
                } catch (err) {
                    console.error('Failed to save current profile:', err);
                }
                await loadProfiles();
                setIsCreatingNewProfile(false);
                setShowWizard(false);
            };
            initializeEditor();
        }
    }, [isOpen, loadAvatar, loadProfiles]);

    // Poll for avatar updates while editor is open
    useEffect(() => {
        if (!isOpen) return;
        const interval = setInterval(() => {
            GetAvatarPreview().then(avatar => {
                if (avatar && avatar !== localAvatar) {
                    setLocalAvatar(avatar);
                }
            }).catch(() => {});
        }, 3000); // Check every 3 seconds while editor is open
        return () => clearInterval(interval);
    }, [isOpen, localAvatar]);

    const loadProfile = async () => {
        setIsLoading(true);
        try {
            const [userUuid, userName] = await Promise.all([
                GetUUID(),
                GetNick()
            ]);
            setUuid(userUuid || generateUUID());
            // Use 'HyPrism' as fallback - matches backend default
            const displayName = userName || 'HyPrism';
            setUsernameState(displayName);
            setEditUsername(displayName);
            setEditUuid(userUuid || '');
        } catch (err) {
            console.error('Failed to load profile:', err);
            setUuid(generateUUID());
            setUsernameState('HyPrism');
        }
        setIsLoading(false);
    };

    const generateUUID = () => {
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
            const r = (Math.random() * 16) | 0;
            const v = c === 'x' ? r : (r & 0x3) | 0x8;
            return v.toString(16);
        });
    };

    // Auto-save current profile whenever username or uuid changes
    const autoSaveProfile = useCallback(async () => {
        try {
            await SaveCurrentAsProfile();
            await loadProfiles();
        } catch (err) {
            console.error('Failed to auto-save profile:', err);
        }
    }, [loadProfiles]);

    const handleSaveUsername = async () => {
        const trimmedUsername = editUsername.trim();
        if (trimmedUsername && trimmedUsername.length >= 1 && trimmedUsername.length <= 16) {
            setSaveStatus('saving');
            try {
                await SetNick(trimmedUsername);
                setUsernameState(trimmedUsername);
                setIsEditingUsername(false);
                setIsCreatingNewProfile(false);
                
                // Auto-save the profile
                await autoSaveProfile();
                
                setSaveStatus('saved');
                onProfileUpdate?.();
                setTimeout(() => setSaveStatus('idle'), 2000);
            } catch (err) {
                console.error('Failed to save username:', err);
                setSaveStatus('idle');
            }
        }
    };

    const handleSaveUuid = async () => {
        const trimmed = editUuid.trim();
        if (trimmed) {
            setSaveStatus('saving');
            try {
                await SetUUID(trimmed);
                setUuid(trimmed);
                setIsEditingUuid(false);
                
                // Auto-save the profile
                await autoSaveProfile();
                
                setSaveStatus('saved');
                onProfileUpdate?.();
                setTimeout(() => setSaveStatus('idle'), 2000);
            } catch (err) {
                console.error('Failed to save UUID:', err);
                setSaveStatus('idle');
            }
        }
    };

    const handleRandomizeUuid = () => {
        const newUuid = generateUUID();
        setEditUuid(newUuid);
    };

    const handleCopyUuid = async () => {
        try {
            await navigator.clipboard.writeText(uuid);
            setCopiedUuid(true);
            setTimeout(() => setCopiedUuid(false), 2000);
        } catch (err) {
            console.error('Failed to copy UUID:', err);
        }
    };

    const handleSaveUsernameWithName = async (name: string) => {
        if (name.trim() && name.length <= 16) {
            setSaveStatus('saving');
            try {
                await SetNick(name.trim());
                setUsernameState(name.trim());
                setEditUsername(name.trim());
                setIsEditingUsername(false);
                setIsCreatingNewProfile(false);
                
                // Auto-save the profile
                await autoSaveProfile();
                
                setSaveStatus('saved');
                onProfileUpdate?.();
                setTimeout(() => setSaveStatus('idle'), 2000);
            } catch (err) {
                console.error('Failed to save username:', err);
                setSaveStatus('idle');
            }
        }
    };

    const handleUsernameKeyDown = (e: React.KeyboardEvent) => {
        if (e.key === 'Enter') {
            handleSaveUsername();
        } else if (e.key === 'Escape') {
            if (isCreatingNewProfile) {
                // If creating new profile and escaping, generate random name
                const randomName = generateRandomName();
                handleSaveUsernameWithName(randomName);
            } else {
                setEditUsername(username);
                setIsEditingUsername(false);
            }
        }
    };

    const handleUuidKeyDown = (e: React.KeyboardEvent) => {
        if (e.key === 'Enter') {
            handleSaveUuid();
        } else if (e.key === 'Escape') {
            setEditUuid(uuid);
            setIsEditingUuid(false);
        }
    };
    
    // Profile management handlers
    const handleSwitchProfile = async (profileId: string) => {
        try {
            // Find the actual index of this profile in the full profiles list
            const actualIndex = profiles.findIndex(p => p.id === profileId);
            if (actualIndex === -1) {
                console.error('Profile not found in profiles list');
                return;
            }
            
            const targetProfile = profiles[actualIndex];
            
            // Optimistically update the UI immediately (no loading state)
            setUsernameState(targetProfile.name);
            setEditUsername(targetProfile.name);
            setUuid(targetProfile.uuid ?? '');
            setEditUuid(targetProfile.uuid ?? '');
            setCurrentProfileIndex(actualIndex);
            
            // Pre-load the avatar for the target profile (use null if no avatar exists)
            const targetAvatar = profileAvatars[targetProfile.uuid ?? ''];
            setLocalAvatar(targetAvatar || null);
            
            const success = await SwitchProfile(profileId);
            
            if (success) {
                // Refresh avatar in the background (don't wait)
                GetAvatarPreview().then(avatar => {
                    // Set avatar or null if no avatar exists
                    setLocalAvatar(avatar || null);
                }).catch(() => {
                    setLocalAvatar(null);
                });
                
                onProfileUpdate?.();
            } else {
                // Revert on failure
                await loadProfile();
                await loadAvatar();
                await loadProfiles(); // Also refresh current profile index
            }
        } catch (err) {
            console.error('Failed to switch profile:', err);
        }
    };
    
    const handleDeleteProfile = (profileId: string, e: React.MouseEvent) => {
        e.stopPropagation();
        e.preventDefault();
        
        // Find the profile name for the confirmation modal
        const profileToDelete = profiles.find(p => p.id === profileId);
        const profileName = profileToDelete?.name || 'this profile';
        
        // Show in-launcher confirmation modal
        setDeleteConfirmation({ id: profileId, name: profileName });
    };
    
    const handleConfirmDelete = async () => {
        if (!deleteConfirmation) return;
        
        try {
            const success = await DeleteProfile(deleteConfirmation.id);
            if (success) {
                // Reload profiles after delete
                await loadProfiles();
                onProfileUpdate?.();
            }
        } catch (err) {
            console.error('Failed to delete profile:', err);
        }
        
        setDeleteConfirmation(null);
    };
    
    const handleCreateProfile = async () => {
        // Show the profile creation wizard in the right panel
        setShowWizard(true);
    };

    const handleWizardComplete = async (profile: Profile) => {
        setShowWizard(false);
        
        // Reload profiles list
        await loadProfiles();
        
        // Find the new profile and switch to it
        const updatedProfiles = await GetProfiles();
        const newProfileIndex = updatedProfiles?.findIndex(p => p.id === profile.id);
        if (newProfileIndex !== undefined && newProfileIndex >= 0) {
            await SwitchProfile(profile.id);
            setUsernameState(profile.name);
            setEditUsername(profile.name);
            setUuid(profile.uuid ?? '');
            setEditUuid(profile.uuid ?? '');
            setLocalAvatar(null);
            setCurrentProfileIndex(newProfileIndex);
        }
        
        await loadProfiles();
        onProfileUpdate?.();
    };

    const handleWizardCancel = () => {
        setShowWizard(false);
    };

    const handleDuplicateProfile = async (profileId: string, e: React.MouseEvent) => {
        e.stopPropagation();
        try {
            const newProfile = await DuplicateProfileWithoutData(profileId);
            if (newProfile) {
                await loadProfiles();
                onProfileUpdate?.();
            }
        } catch (err) {
            console.error('Failed to duplicate profile:', err);
        }
    };

    if (!isOpen && !isPageMode) return null;

    return (
        <AnimatePresence>
            <motion.div
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                exit={{ opacity: 0 }}
                className={isPageMode
                    ? "w-full h-full flex gap-4"
                    : `fixed inset-0 z-[200] flex items-center justify-center bg-[#0a0a0a]/90`
                }
                onClick={(e) => !isPageMode && e.target === e.currentTarget && onClose()}
            >
                {/* In modal mode, constrain width/height; in page mode, 'contents' makes this invisible to layout */}
                <div className={isPageMode
                    ? "contents"
                    : "w-full max-w-3xl mx-4 max-h-[80vh] flex gap-2 relative"
                }>
                    {/* Left Sidebar - Independent glass panel */}
                    <div className={`w-48 flex-shrink-0 flex flex-col py-4 overflow-y-auto rounded-2xl glass-panel-static-solid`}>
                        {!isPageMode && <h2 className="text-lg font-bold text-white px-4 mb-4">{t('profiles.savedProfiles')}</h2>}
                        
                        {/* Profile Navigation - All Profiles */}
                        <nav className="flex-1 space-y-1 px-2 overflow-y-auto">
                            {/* All Profiles - show all of them with current one highlighted */}
                            {(() => {
                                const filtered = profiles.filter(p => p.name && p.name.trim() !== '');
                                return filtered.map((profile) => {
                                // Find the actual index in the original profiles array
                                const actualIndex = profiles.findIndex(p => p.id === profile.id);
                                const isCurrentProfile = actualIndex === currentProfileIndex;
                                const profileAvatar = profileAvatars[profile.uuid ?? ''];
                                // Check if this is a duplicate (folder name differs from display name)
                                const isDuplicate = profile.folderName && profile.folderName !== profile.name;
                                
                                return (
                                    <div
                                        key={profile.id}
                                        className={`w-full flex items-center justify-between px-2 py-1.5 rounded-lg text-sm transition-colors group relative overflow-hidden ${
                                            isCurrentProfile
                                                ? ''
                                                : 'text-white/60 hover:text-white hover:bg-white/5'
                                        }`}
                                        style={isCurrentProfile ? { backgroundColor: `${accentColor}20`, color: accentColor } : undefined}
                                    >
                                        <button
                                            onClick={() => !isCurrentProfile && handleSwitchProfile(profile.id)}
                                            className="flex items-center gap-3 min-w-0 overflow-hidden text-left flex-1"
                                            disabled={isCurrentProfile}
                                        >
                                            <div
                                                className="w-8 h-8 rounded-full overflow-hidden flex-shrink-0 flex items-center justify-center"
                                                style={isCurrentProfile
                                                    ? { borderWidth: '2px', borderColor: accentColor, backgroundColor: profileAvatar ? 'transparent' : `${accentColor}20` }
                                                    : { borderWidth: '1px', borderColor: 'rgba(255,255,255,0.2)', backgroundColor: profileAvatar ? 'transparent' : 'rgba(255,255,255,0.05)' }
                                                }
                                            >
                                                {profileAvatar ? (
                                                    <img
                                                        src={profileAvatar}
                                                        className="w-full h-full object-cover object-[center_20%]"
                                                        alt="Avatar"
                                                    />
                                                ) : (
                                                    <User size={14} style={isCurrentProfile ? { color: accentColor } : { color: '#ffffff', opacity: 0.4 }} />
                                                )}
                                            </div>
                                            <span className={`whitespace-nowrap truncate ${isCurrentProfile ? 'font-medium' : ''}`}>
                                                {profile.name || 'Unnamed'}
                                                {isDuplicate && (
                                                    <span className="text-white/30 text-xs ml-1">({profile.folderName?.replace(profile.name + ' ', '')})</span>
                                                )}
                                            </span>
                                        </button>
                                        
                                        <div
                                            className="flex items-center opacity-0 group-hover:opacity-100 transition-opacity flex-shrink-0"
                                        >
                                            <IconButton
                                                size="sm"
                                                onClick={(e) => handleDuplicateProfile(profile.id, e)}
                                                title={t('profiles.duplicateProfile')}
                                            >
                                                <CopyPlus size={14} />
                                            </IconButton>
                                            {!isCurrentProfile && (
                                                <IconButton
                                                    size="sm"
                                                    className="text-red-400/60 hover:text-red-400 hover:bg-red-500/20"
                                                    onClick={(e) => handleDeleteProfile(profile.id, e)}
                                                    title={t('profiles.deleteProfile')}
                                                >
                                                    <Trash2 size={14} />
                                                </IconButton>
                                            )}
                                        </div>
                                    </div>
                                );
                            })})()}
                            
                            {profiles.filter(p => p.name && p.name.trim() !== '').length === 0 && (
                                <p className="text-center text-white/20 text-xs py-4 px-2">
                                    {t('profiles.noProfiles')}
                                </p>
                            )}
                        </nav>
                        
                        {/* Create New Profile Button at Bottom */}
                        <div className="px-2 pt-4 border-t border-white/[0.04] mx-2 relative">
                            <AnimatePresence>
                            {isCreateMenuOpen && (
                                <>
                                    {/* Backdrop to close menu */}
                                    <div 
                                        className="fixed inset-0 z-40" 
                                        onClick={() => setIsCreateMenuOpen(false)}
                                    />
                                    <motion.div
                                        initial={{ opacity: 0, y: 8, scale: 0.96 }}
                                        animate={{ opacity: 1, y: 0, scale: 1 }}
                                        exit={{ opacity: 0, y: 8, scale: 0.96 }}
                                        transition={{ duration: 0.15, ease: [0.4, 0, 0.2, 1] }}
                                        className="absolute bottom-[calc(100%+8px)] left-2 right-2 bg-[#1c1c1e] border border-white/[0.08] rounded-xl shadow-xl overflow-hidden z-50 py-1"
                                    >
                                        <MenuItemButton
                                            onClick={() => {
                                                setWizardInitialStep('official-auth');
                                                setShowWizard(true);
                                                setIsCreateMenuOpen(false);
                                            }}
                                        >
                                            <Globe size={18} />
                                            {t('profiles.wizard.official')}
                                        </MenuItemButton>
                                        <MenuItemButton
                                            onClick={() => {
                                                setWizardInitialStep('unofficial-name');
                                                setShowWizard(true);
                                                setIsCreateMenuOpen(false);
                                            }}
                                        >
                                            <Unplug size={18} />
                                            {t('profiles.wizard.unofficial')}
                                        </MenuItemButton>
                                    </motion.div>
                                </>
                            )}
                            </AnimatePresence>
                            <Button
                                onClick={() => setIsCreateMenuOpen(!isCreateMenuOpen)}
                                className="w-full border-dashed border-white/20 text-white/40 hover:text-white/60 hover:border-white/40 !h-auto py-2.5"
                            >
                                <Plus size={14} />
                                <span>{t('profiles.createNew')}</span>
                            </Button>
                        </div>
                    </div>

                    {/* Right Content - Independent glass panel */}
                    <div className={`flex-1 flex flex-col min-w-0 overflow-hidden rounded-2xl glass-panel-static-solid`}>
                        {/* Header */}
                        <div className="flex items-center justify-between p-4 border-b border-white/[0.04]">
                            <h3 className="text-white font-medium">{showWizard ? t('profiles.wizard.title') : t('profiles.editor')}</h3>
                            {!isPageMode && (
                                <IconButton
                                    variant="ghost"
                                    onClick={onClose}
                                >
                                    <X size={20} />
                                </IconButton>
                            )}
                        </div>

                        {/* Content: Wizard or Profile Details */}
                        {showWizard ? (
                            <ProfileCreationWizard
                                initialStep={wizardInitialStep}
                                onComplete={handleWizardComplete}
                                onCancel={handleWizardCancel}
                            />
                        ) : (
                        <div className="flex-1 p-6 overflow-y-auto">
                            {isLoading ? (
                                <div className="flex items-center justify-center py-12">
                                    <RefreshCw size={32} className="animate-spin text-white opacity-40" />
                                </div>
                            ) : (
                                <div className="space-y-6">
                                    {/* Profile Picture Section */}
                                    <div className="flex flex-col items-center gap-4">
                                        {/* Avatar - Display only */}
                                        <div className="relative">
                                            <div
                                                className="w-24 h-24 rounded-full overflow-hidden border-2 flex items-center justify-center"
                                                style={{ borderColor: accentColor, backgroundColor: localAvatar ? 'transparent' : `${accentColor}20` }}
                                            >
                                                {localAvatar ? (
                                                    <img
                                                        src={localAvatar}
                                                        className="w-full h-full object-cover object-[center_15%]"
                                                        alt="Player Avatar"
                                                    />
                                                ) : (
                                                    <User size={40} style={{ color: accentColor }} />
                                                )}
                                            </div>
                                        </div>
                                        
                                        {/* Username Display/Edit */}
                                        <div className="flex items-center gap-2">
                                            {isEditingUsername ? (
                                                <div className="flex items-center gap-2">
                                                    <input
                                                        type="text"
                                                        value={editUsername}
                                                        onChange={(e) => setEditUsername(e.target.value)}
                                                        onKeyDown={handleUsernameKeyDown}
                                                        maxLength={16}
                                                        autoFocus
                                                        placeholder={isCreatingNewProfile ? t('profiles.enterName') : ''}
                                                        className="bg-[#2c2c2e] text-white text-xl font-bold px-3 py-1 rounded-lg border outline-none w-48 text-center"
                                                        style={{ borderColor: accentColor }}
                                                    />
                                                    <IconButton
                                                        onClick={() => setEditUsername(generateRandomName())}
                                                        className="bg-white/10 text-white/70 hover:bg-white/20"
                                                        title={t('profiles.generateName')}
                                                    >
                                                        <Dices size={16} />
                                                    </IconButton>
                                                    <IconButton
                                                        onClick={handleSaveUsername}
                                                        style={{ backgroundColor: `${accentColor}33`, color: accentColor }}
                                                    >
                                                        <Check size={16} />
                                                    </IconButton>
                                                </div>
                                            ) : (
                                                <>
                                                    <span className="text-2xl font-bold text-white">{username}</span>
                                                    {isCurrentOfficial ? (
                                                        <span className="p-1.5 rounded-lg text-white/20" title={t('profiles.officialLocked')}>
                                                            <Lock size={14} />
                                                        </span>
                                                    ) : (
                                                        <>
                                                            <IconButton
                                                                onClick={async () => {
                                                                    const newName = generateRandomName();
                                                                    await SetNick(newName);
                                                                    setUsernameState(newName);
                                                                    setEditUsername(newName);
                                                                    onProfileUpdate?.();
                                                                }}
                                                                className="text-white/40 hover:text-white/80 hover:bg-white/5"
                                                                title={t('profiles.generateName')}
                                                            >
                                                                <Dices size={14} />
                                                            </IconButton>
                                                            <IconButton
                                                                onClick={() => {
                                                                    setEditUsername(username);
                                                                    setIsEditingUsername(true);
                                                                }}
                                                                className="text-white/40 hover:text-white/80 hover:bg-white/5"
                                                                title={t('profiles.editUsername')}
                                                            >
                                                                <Edit3 size={14} />
                                                            </IconButton>
                                                        </>
                                                    )}
                                                </>
                                            )}
                                        </div>
                                        
                                        {/* New profile hint */}
                                        {isCreatingNewProfile && isEditingUsername && (
                                            <p className="text-xs text-white/40 text-center">
                                                {t('profiles.enterNewName')}
                                            </p>
                                        )}
                                    </div>

                                    {/* UUID Section */}
                                    <div className="p-4 rounded-2xl bg-[#2c2c2e] border border-white/[0.06]">
                                        <div className="flex items-center justify-between mb-2">
                                            <label className="text-sm text-white/60">{t('profiles.playerUuid')}</label>
                                            <div className="flex items-center gap-1">
                                                <IconButton
                                                    size="sm"
                                                    onClick={handleCopyUuid}
                                                    className="text-white/40 hover:text-white/80 hover:bg-white/10"
                                                    title={t('profiles.copyUuid')}
                                                >
                                                    {copiedUuid ? <CheckCircle size={14} className="text-green-400" /> : <Copy size={14} />}
                                                </IconButton>
                                                {isCurrentOfficial ? (
                                                    <span className="p-1.5 rounded-lg text-white/20" title={t('profiles.officialLocked')}>
                                                        <Lock size={14} />
                                                    </span>
                                                ) : (
                                                    <>
                                                        <IconButton
                                                            onClick={async () => {
                                                                const newUuid = generateUUID();
                                                                await SetUUID(newUuid);
                                                                setUuid(newUuid);
                                                                setEditUuid(newUuid);
                                                                onProfileUpdate?.();
                                                            }}
                                                            className="text-white/40 hover:text-white/80 hover:bg-white/10"
                                                            title={t('profiles.randomUuid')}
                                                        >
                                                            <Dices size={14} />
                                                        </IconButton>
                                                        <IconButton
                                                            onClick={() => {
                                                                setEditUuid(uuid);
                                                                setIsEditingUuid(true);
                                                            }}
                                                            className="text-white/40 hover:text-white/80 hover:bg-white/10"
                                                            title={t('profiles.editUuid')}
                                                        >
                                                            <Edit3 size={14} />
                                                        </IconButton>
                                                    </>
                                                )}
                                            </div>
                                        </div>
                                        
                                        {isEditingUuid ? (
                                            <div className="flex items-center gap-2">
                                                <input
                                                    type="text"
                                                    value={editUuid}
                                                    onChange={(e) => setEditUuid(e.target.value)}
                                                    onKeyDown={handleUuidKeyDown}
                                                    autoFocus
                                                    className="flex-1 bg-[#1c1c1e] text-white font-mono text-sm px-3 py-2 rounded-lg border outline-none"
                                                    style={{ borderColor: accentColor }}
                                                />
                                                <IconButton
                                                    onClick={handleRandomizeUuid}
                                                    className="bg-white/10 text-white/70 hover:bg-white/20"
                                                    title={t('profiles.randomUuid')}
                                                >
                                                    <Dices size={16} />
                                                </IconButton>
                                                <IconButton
                                                    onClick={handleSaveUuid}
                                                    style={{ backgroundColor: `${accentColor}33`, color: accentColor }}
                                                    title={t('profiles.saveUuid')}
                                                >
                                                    <Check size={16} />
                                                </IconButton>
                                            </div>
                                        ) : (
                                            <p className="text-white font-mono text-sm truncate">{uuid}</p>
                                        )}
                                    </div>

                                    {/* Open Profile Folder Button */}
                                    <Button
                                        onClick={() => OpenCurrentProfileFolder()}
                                        className="w-full p-4 rounded-2xl bg-[#2c2c2e] border-white/[0.06] hover:border-white/20 text-white/60 hover:text-white"
                                    >
                                        <FolderOpen size={18} />
                                        <span className="text-sm">{t('profiles.openFolder')}</span>
                                    </Button>

                                    {/* Save Status */}
                                    {saveStatus === 'saved' && (
                                        <motion.div
                                            initial={{ opacity: 0, y: 10 }}
                                            animate={{ opacity: 1, y: 0 }}
                                            exit={{ opacity: 0 }}
                                            className="flex items-center justify-center gap-2 text-green-400 text-sm"
                                        >
                                            <CheckCircle size={16} />
                                            {t('profiles.saved')}
                                        </motion.div>
                                    )}
                                </div>
                            )}
                        </div>
                        )}
                    </div>
                </div>
            </motion.div>
            
            {/* Delete Profile Confirmation Modal */}
            {deleteConfirmation && (
                <DeleteProfileConfirmationModal
                    profileName={deleteConfirmation.name}
                    onConfirm={handleConfirmDelete}
                    onCancel={() => setDeleteConfirmation(null)}
                />
            )}
        </AnimatePresence>
    );
};

export default ProfileEditor;
