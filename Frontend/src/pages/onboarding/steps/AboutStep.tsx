import React, { useMemo } from 'react';
import { Github, Bug, Sparkles, Users } from 'lucide-react';
import { openUrl } from '@/utils/openUrl';
import { DiscordIcon } from '@/components/icons/DiscordIcon';
import type { UseOnboardingReturn } from '@/hooks/useOnboarding';
import appIcon from '@/assets/images/logo.png';

// Core team members - logins and roles only, avatars come from GitHub API
const CORE_TEAM_CONFIG = [
  { login: 'yyyumeniku', displayName: 'yyyumeniku', roleKey: 'creatorRole' },
  { login: 'sanasol', displayName: 'sanasol', roleKey: 'authRole' },
  { login: 'freakdaniel', displayName: 'Daniel Freak', roleKey: 'codevRole' },
  { login: 'XargonWan', displayName: 'XargonWan', roleKey: 'cicdRole' },
  { login: 'FowlBytez', displayName: 'FowlBytez', roleKey: 'testerRole' },
  { login: 'CupRusk', displayName: 'CupRusk', roleKey: 'installerRole' },
];

const CORE_TEAM_LOGINS = CORE_TEAM_CONFIG.map(m => m.login.toLowerCase());

// Known bot accounts to filter out
const BOT_LOGINS = [
  'copilot',
  'github-actions',
  'dependabot',
  'renovate',
  'semantic-release-bot',
  'allcontributors',
  'imgbot',
  'codecov',
  'snyk-bot',
  'greenkeeper',
  'google-labs-jules',
];

interface AboutStepProps {
  onboarding: UseOnboardingReturn;
}

export const AboutStep: React.FC<AboutStepProps> = ({ onboarding }) => {
  const gc = 'bg-[#1a1a1a]/80 border border-white/5';

  // Build core team with avatars from contributors data
  const coreTeam = useMemo(() => CORE_TEAM_CONFIG.map(member => {
    const contributor = onboarding.contributors.find(c => c.login.toLowerCase() === member.login.toLowerCase());
    return {
      ...member,
      avatar_url: contributor?.avatar_url || `https://github.com/${member.login}.png`,
      html_url: contributor?.html_url || `https://github.com/${member.login}`,
    };
  }), [onboarding.contributors]);

  // Filter out core team members and bots from contributors list
  const otherContributors = useMemo(() => onboarding.contributors.filter(c => {
    const login = c.login.toLowerCase();
    if (CORE_TEAM_LOGINS.includes(login)) return false;
    if (BOT_LOGINS.includes(login)) return false;
    if (login.endsWith('[bot]')) return false;
    if (login.match(/-bot$|_bot$|bot\[bot\]$/)) return false;
    return true;
  }), [onboarding.contributors]);

  return (
    <div className="h-full flex flex-col gap-4">
      {/* Header */}
      <div>
        <h2 className="text-xl font-semibold text-white mb-1">{onboarding.t('onboarding.aboutHyPrism')}</h2>
        <p className="text-sm text-white/60">{onboarding.t('onboarding.allSet')}</p>
      </div>
      
      {/* Content Grid */}
      <div className="grid grid-cols-1 xl:grid-cols-[200px_1fr] gap-4 flex-1 min-h-0">
        {/* Left Panel - App Info */}
        <div className={`p-4 rounded-xl ${gc} flex flex-col items-center gap-3`}>
          <div className="flex items-center gap-3 xl:flex-col xl:text-center">
            <img src={appIcon} alt="HyPrism" className="w-12 h-12 xl:w-16 xl:h-16" />
            <div>
              <h3 className="text-lg font-bold text-white">HyPrism</h3>
              <p className="text-xs text-white/50">{onboarding.t('onboarding.unofficial')}</p>
              <p className="text-[10px] text-white/30 mt-0.5">v{onboarding.launcherVersion}</p>
            </div>
          </div>

          <div className="flex gap-3">
            <button
              onClick={onboarding.openGitHub}
              className="opacity-80 hover:opacity-100 transition-opacity"
              title={onboarding.t('social.github')}
            >
              <Github size={22} className="text-white" />
            </button>
            <button
              onClick={onboarding.openDiscord}
              className="opacity-80 hover:opacity-100 transition-opacity"
              title={onboarding.t('social.discord')}
            >
              <DiscordIcon size={16} color="white" />
            </button>
            <button
              onClick={onboarding.openBugReport}
              className="opacity-80 hover:opacity-100 transition-opacity"
              title={onboarding.t('onboarding.bugReport')}
            >
              <Bug size={22} className="text-white" />
            </button>
          </div>

          {/* Disclaimer - hidden on small screens */}
          <p className="hidden xl:block text-white/40 text-[10px] text-center leading-relaxed mt-auto">
            {onboarding.t('onboarding.disclaimer')}
          </p>
        </div>

        {/* Right Panel - Team & Contributors */}
        <div className="flex flex-col gap-3 overflow-y-auto min-h-0">
          {onboarding.isLoadingContributors ? (
            <div className="flex justify-center py-6">
              <div className="w-5 h-5 border-2 rounded-full animate-spin" style={{ borderColor: `${onboarding.accentColor}30`, borderTopColor: onboarding.accentColor }} />
            </div>
          ) : (
            <>
              {/* Core Team Section */}
              <div className={`p-3 rounded-xl ${gc}`}>
                <div className="flex items-center gap-2 mb-2">
                  <Sparkles size={14} className="text-purple-400" />
                  <h3 className="text-xs font-semibold text-white">{onboarding.t('settings.aboutSettings.coreTeam')}</h3>
                </div>
                <div className="grid grid-cols-2 sm:grid-cols-3 gap-2">
                  {coreTeam.map((member) => (
                    <button
                      key={member.login}
                      onClick={() => openUrl(member.html_url)}
                      className="flex items-center gap-2 p-2 rounded-lg bg-white/[0.03] hover:bg-white/[0.06] border border-white/[0.06] transition-colors"
                    >
                      <img
                        src={member.avatar_url}
                        alt={member.login}
                        className="w-8 h-8 rounded-full flex-shrink-0"
                      />
                      <div className="text-left min-w-0">
                        <span className="text-white font-medium text-xs block truncate">
                          {onboarding.truncateName(member.displayName, 10)}
                        </span>
                        <p className="text-[9px] text-white/40 truncate">
                          {onboarding.t(`settings.aboutSettings.${member.roleKey}`)}
                        </p>
                      </div>
                    </button>
                  ))}
                </div>
              </div>

              {/* Contributors Section */}
              {otherContributors.length > 0 && (
                <div className={`p-3 rounded-xl ${gc}`}>
                  <div className="flex items-center gap-2 mb-2">
                    <Users size={14} className="text-white opacity-60" />
                    <h3 className="text-xs font-semibold text-white">{onboarding.t('settings.aboutSettings.contributors')}</h3>
                  </div>
                  <div className="grid grid-cols-5 sm:grid-cols-6 lg:grid-cols-8 gap-1.5">
                    {otherContributors.slice(0, 16).map((contributor) => (
                      <button
                        key={contributor.login}
                        onClick={() => openUrl(contributor.html_url)}
                        className="flex flex-col items-center gap-1 p-1.5 rounded-lg hover:bg-white/5 transition-colors"
                        title={`${contributor.login} - ${contributor.contributions} contributions`}
                      >
                        <img
                          src={contributor.avatar_url}
                          alt={contributor.login}
                          className="w-7 h-7 rounded-full"
                        />
                        <span className="text-[8px] text-white/60 max-w-full truncate text-center w-full">
                          {onboarding.truncateName(contributor.login, 8)}
                        </span>
                      </button>
                    ))}
                  </div>
                </div>
              )}
            </>
          )}
        </div>
      </div>

      {/* Disclaimer - shown on small screens */}
      <div className={`p-2 rounded-lg ${gc} xl:hidden`}>
        <p className="text-white/50 text-[10px] text-center">
          {onboarding.t('onboarding.disclaimer')}
        </p>
      </div>
    </div>
  );
};
