import React, { useEffect, useRef, useState, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { Wifi, Server, Globe, Edit3, CheckCircle, XCircle, Loader2, AlertTriangle, RefreshCw } from 'lucide-react';
import { Button, SettingsToggleCard, RadioOptionCard } from '@/components/ui/Controls';
import { ipc } from '@/lib/ipc';

interface NetworkTabProps {
  onlineMode: boolean;
  setOnlineMode: (v: boolean) => void;
  authMode: 'default' | 'official' | 'custom';
  setAuthModeState: (v: 'default' | 'official' | 'custom') => void;
  authDomain: string;
  setAuthDomain: (v: string) => void;
  customAuthDomain: string;
  setCustomAuthDomain: (v: string) => void;
  onAuthSettingsChange?: () => void;
  isActiveProfileOfficial?: boolean;
}

export const NetworkTab: React.FC<NetworkTabProps> = ({
  onlineMode,
  setOnlineMode,
  authMode,
  setAuthModeState,
  authDomain,
  setAuthDomain,
  customAuthDomain,
  setCustomAuthDomain,
  onAuthSettingsChange,
  isActiveProfileOfficial = false,
}) => {
  const { t } = useTranslation();
  const hasAutoSwitched = useRef(false);

  // Auto-switch auth mode based on active profile type
  useEffect(() => {
    if (hasAutoSwitched.current) return;
    
    const autoSwitch = async () => {
      if (isActiveProfileOfficial) {
        // Official profile: force official auth server
        if (authMode !== 'official') {
          setAuthModeState('official');
          setAuthDomain('sessions.hytale.com');
          await ipc.settings.update({ authDomain: 'sessions.hytale.com' });
          onAuthSettingsChange?.();
          hasAutoSwitched.current = true;
        }
      } else {
        // Unofficial profile: force default auth server if currently on official
        if (authMode === 'official') {
          setAuthModeState('default');
          setAuthDomain('sessions.sanasol.ws');
          await ipc.settings.update({ authDomain: 'sessions.sanasol.ws' });
          onAuthSettingsChange?.();
          hasAutoSwitched.current = true;
        }
      }
    };
    
    autoSwitch();
  }, [isActiveProfileOfficial, authMode, setAuthModeState, setAuthDomain, onAuthSettingsChange]);

  // Auth server ping state
  const [authPingStatus, setAuthPingStatus] = useState<'idle' | 'checking' | 'online' | 'offline'>('idle');
  const [authPingMs, setAuthPingMs] = useState<number | null>(null);
  const [isOfficialAuth, setIsOfficialAuth] = useState(false);

  // Check auth server availability
  const checkAuthServer = useCallback(async () => {
    if (!onlineMode) return;
    
    setAuthPingStatus('checking');
    try {
      const result = await ipc.network.pingAuthServer({ authDomain });
      setIsOfficialAuth(result.isOfficial);
      if (result.isAvailable) {
        setAuthPingStatus('online');
        setAuthPingMs(result.pingMs);
      } else {
        setAuthPingStatus('offline');
        setAuthPingMs(null);
      }
    } catch {
      setAuthPingStatus('offline');
      setAuthPingMs(null);
    }
  }, [onlineMode, authDomain]);

  // Auto-check auth server when settings change
  useEffect(() => {
    if (onlineMode && !isActiveProfileOfficial) {
      checkAuthServer();
    } else if (isActiveProfileOfficial) {
      // Official profiles always considered online
      setAuthPingStatus('online');
      setIsOfficialAuth(true);
      setAuthPingMs(null);
    } else {
      setAuthPingStatus('idle');
    }
  }, [onlineMode, authDomain, isActiveProfileOfficial, checkAuthServer]);

  return (
    <div className="space-y-6">
      {/* Online Mode Toggle */}
      <div className="space-y-3">
        <SettingsToggleCard
          icon={<Wifi size={16} className="text-white opacity-70" />}
          title={t('settings.networkSettings.onlineMode')}
          description={t('settings.networkSettings.onlineModeHint')}
          checked={onlineMode}
          onCheckedChange={async (v) => {
            setOnlineMode(v);
            await ipc.settings.update({ onlineMode: v });
            onAuthSettingsChange?.();
          }}
        />
      </div>

      {/* Auth Server Selector */}
      {onlineMode && (
        <div>
          <label className="block text-sm text-white/60 mb-2">{t('settings.networkSettings.authServer')}</label>
          <p className="text-xs text-white/40 mb-4">{t('settings.networkSettings.authServerHint')}</p>

          <div className="space-y-2">
            {/* Official profile: only show official auth server */}
            {isActiveProfileOfficial ? (
              <>
                {/* Official (Hytale) - shown for official profiles */}
                <RadioOptionCard
                  icon={<Globe size={16} />}
                  title={t('settings.networkSettings.authOfficial')}
                  description={t('settings.networkSettings.authOfficialHint')}
                  selected={true}
                  onClick={() => {
                    // Already official, no action needed
                  }}
                />
                <p className="text-xs text-white/40 mt-2 px-1">
                  {t('settings.networkSettings.officialProfileNote', 'Official accounts can only use official Hytale authentication.')}
                </p>
              </>
            ) : (
              <>
                {/* Default (sessions.sanasol.ws) - shown for unofficial profiles */}
                <RadioOptionCard
                  icon={<Server size={16} />}
                  title={t('settings.networkSettings.authDefault')}
                  description="sessions.sanasol.ws"
                  selected={authMode === 'default'}
                  onClick={async () => {
                    setAuthModeState('default');
                    setAuthDomain('sessions.sanasol.ws');
                    await ipc.settings.update({ authDomain: 'sessions.sanasol.ws' });
                    onAuthSettingsChange?.();
                  }}
                />

                {/* Custom - shown for unofficial profiles */}
                <RadioOptionCard
                  icon={<Edit3 size={16} />}
                  title={t('settings.networkSettings.authCustom')}
                  description={t('settings.networkSettings.authCustomHint')}
                  selected={authMode === 'custom'}
                  onClick={() => {
                    if (authMode !== 'custom') {
                      setAuthModeState('custom');
                    }
                  }}
                >
                  {/* Custom domain input */}
                  <div className="flex gap-2">
                    <input
                      type="text"
                      value={customAuthDomain}
                      onChange={(e) => setCustomAuthDomain(e.target.value)}
                      onKeyDown={async (e) => {
                        if (e.key === 'Enter' && customAuthDomain.trim()) {
                          setAuthDomain(customAuthDomain.trim());
                          await ipc.settings.update({ authDomain: customAuthDomain.trim() });
                          onAuthSettingsChange?.();
                        }
                      }}
                      placeholder="auth.example.com"
                      className="flex-1 h-10 px-3 rounded-lg bg-[#1c1c1e] border border-white/[0.08] text-white text-sm placeholder-white/30 focus:outline-none focus:border-white/20 transition-colors"
                    />
                    <Button
                      variant="primary"
                      onClick={async () => {
                        if (customAuthDomain.trim()) {
                          setAuthDomain(customAuthDomain.trim());
                          await ipc.settings.update({ authDomain: customAuthDomain.trim() });
                          onAuthSettingsChange?.();
                        }
                      }}
                      disabled={!customAuthDomain.trim()}
                    >
                      {t('common.save')}
                    </Button>
                  </div>
                  {authDomain && authDomain !== 'sessions.sanasol.ws' && authDomain !== 'sessions.hytale.com' && (
                    <p className="text-xs text-white/30 mt-2">
                      {t('settings.networkSettings.currentServer')}: <span className="text-white/50">{authDomain}</span>
                    </p>
                  )}
                </RadioOptionCard>
                <p className="text-xs text-white/40 mt-2 px-1">
                  {t('settings.networkSettings.unofficialProfileNote', 'Unofficial accounts cannot use official Hytale authentication.')}
                </p>
              </>
            )}
          </div>
        </div>
      )}

      {/* Auth Server Status */}
      {onlineMode && (
        <div className="p-4 rounded-xl bg-white/[0.03] border border-white/[0.06]">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-3">
              {authPingStatus === 'checking' && (
                <Loader2 size={18} className="text-yellow-400 animate-spin" />
              )}
              {authPingStatus === 'online' && (
                <CheckCircle size={18} className="text-green-400" />
              )}
              {authPingStatus === 'offline' && (
                <XCircle size={18} className="text-red-400" />
              )}
              {authPingStatus === 'idle' && (
                <Server size={18} className="text-white opacity-40" />
              )}
              <div>
                <p className="text-sm font-medium text-white/90">
                  {t('settings.networkSettings.authServerStatus')}
                </p>
                <p className="text-xs text-white/50">
                  {authPingStatus === 'checking' && t('settings.networkSettings.authServerChecking')}
                  {authPingStatus === 'online' && (
                    <>
                      {t('settings.networkSettings.authServerOnline')}
                      {authPingMs !== null && authPingMs > 0 && (
                        <span className="ml-2 text-white/30">
                          {t('settings.networkSettings.authServerPing', { ping: authPingMs })}
                        </span>
                      )}
                    </>
                  )}
                  {authPingStatus === 'offline' && t('settings.networkSettings.authServerOffline')}
                  {authPingStatus === 'idle' && t('settings.networkSettings.authServerStatusHint')}
                </p>
              </div>
            </div>
            <Button
              variant="default"
              size="sm"
              onClick={checkAuthServer}
              disabled={authPingStatus === 'checking'}
              className="flex items-center gap-2"
            >
              <RefreshCw size={14} className={authPingStatus === 'checking' ? 'animate-spin' : ''} />
              {t('settings.networkSettings.authServerCheckNow')}
            </Button>
          </div>
          
          {/* Warning when auth server is offline */}
          {authPingStatus === 'offline' && !isOfficialAuth && (
            <div className="mt-3 p-3 rounded-lg bg-red-500/10 border border-red-500/20 flex items-start gap-2">
              <AlertTriangle size={16} className="text-red-400 mt-0.5 flex-shrink-0" />
              <p className="text-xs text-red-300">
                {t('settings.networkSettings.authServerOfflineWarning')}
              </p>
            </div>
          )}
          
          {/* Note for official servers */}
          {isOfficialAuth && (
            <p className="mt-2 text-xs text-white/30">
              {t('settings.networkSettings.authServerOfficialNote')}
            </p>
          )}
        </div>
      )}
    </div>
  );
};
