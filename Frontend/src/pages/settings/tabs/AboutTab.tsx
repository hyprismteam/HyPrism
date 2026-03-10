import React from 'react';
import { useTranslation } from 'react-i18next';
import { Github, Bug, Users, Sparkles } from 'lucide-react';
import { Button } from '@/components/ui/Controls';
import { DiscordIcon } from '@/components/icons/DiscordIcon';
import appIcon from '@/assets/images/logo.png';
import type { Contributor } from '@/hooks/useSettings';

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

interface AboutTabProps {
  gc: string;
  accentColor: string;
  contributors: Contributor[];
  isLoadingContributors: boolean;
  contributorsError: string | null;
  openGitHub: () => void;
  openDiscord: () => void;
  openBugReport: () => void;
  resetOnboarding: () => Promise<void>;
}

export const AboutTab: React.FC<AboutTabProps> = ({
  gc,
  accentColor,
  contributors,
  isLoadingContributors,
  contributorsError,
  openGitHub,
  openDiscord,
  openBugReport,
  resetOnboarding,
}) => {
  const { t } = useTranslation();

  const truncateName = (name: string, maxLength: number = 10) => {
    if (name.length <= maxLength) return name;
    return name.slice(0, maxLength - 2) + '...';
  };

  // Build core team with avatars from contributors data
  const coreTeam = CORE_TEAM_CONFIG.map(member => {
    const contributor = contributors.find(c => c.login.toLowerCase() === member.login.toLowerCase());
    return {
      ...member,
      avatar_url: contributor?.avatar_url || `https://github.com/${member.login}.png`,
      html_url: contributor?.html_url || `https://github.com/${member.login}`,
    };
  });

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

  // Filter out core team members and bots from contributors list
  const otherContributors = contributors.filter(c => {
    const login = c.login.toLowerCase();
    // Exclude core team
    if (CORE_TEAM_LOGINS.includes(login)) return false;
    // Exclude known bots
    if (BOT_LOGINS.includes(login)) return false;
    // Exclude accounts ending with [bot]
    if (login.endsWith('[bot]')) return false;
    // Exclude accounts containing 'bot' as a suffix pattern
    if (login.match(/-bot$|_bot$|bot\[bot\]$/)) return false;
    return true;
  });

  const openUrl = (url: string) => {
    import('@/utils/openUrl').then(({ openUrl: open }) => open(url));
  };

  return (
    <div className="space-y-6 w-full max-w-6xl mx-auto">
      <div className="grid grid-cols-1 xl:grid-cols-[260px_minmax(0,1fr)] gap-6 items-start">
        {/* Left Panel - App Info */}
        <div className={`p-5 rounded-2xl ${gc} space-y-5`}>
          <div className="flex flex-col items-center text-center">
            <img
              src={appIcon}
              alt="HyPrism"
              className="w-20 h-20 mb-3"
            />
            <h3 className="text-xl font-bold text-white">HyPrism</h3>
            <p className="text-sm text-white/50">{t('settings.aboutSettings.unofficial')}</p>
          </div>

          <div className="flex justify-center gap-4">
            <button
              onClick={openGitHub}
              className="opacity-80 hover:opacity-100 transition-opacity"
              title={t('social.github')}
            >
              <Github size={28} className="text-white" />
            </button>
            <button
              onClick={openDiscord}
              className="opacity-80 hover:opacity-100 transition-opacity"
              title={t('social.discord')}
            >
              <DiscordIcon size={20} color="white" />
            </button>
            <button
              onClick={openBugReport}
              className="opacity-80 hover:opacity-100 transition-opacity"
              title={t('settings.aboutSettings.bugReport')}
            >
              <Bug size={28} className="text-white" />
            </button>
          </div>

          <Button
            onClick={async () => {
              await resetOnboarding();
              window.location.reload();
            }}
            className="w-full"
          >
            {t('settings.aboutSettings.replayIntro')}
          </Button>

          {/* Disclaimer - shown in left panel on xl screens */}
          <p className="hidden xl:block text-white/40 text-xs text-center leading-relaxed pt-2">
            {t('settings.aboutSettings.disclaimer')}
          </p>
        </div>

        {/* Right Panel - Team & Contributors */}
        <div className="space-y-6">
          {isLoadingContributors ? (
            <div className="flex justify-center py-8">
              <div className="w-6 h-6 border-2 rounded-full animate-spin" style={{ borderColor: `${accentColor}30`, borderTopColor: accentColor }} />
            </div>
          ) : (
            <>
              {/* Core Team Section */}
              <div className={`p-4 rounded-2xl ${gc}`}>
                <div className="flex items-center gap-2 mb-1">
                  <Sparkles size={18} className="text-purple-400" />
                  <h3 className="text-sm font-semibold text-white">{t('settings.aboutSettings.coreTeam')}</h3>
                </div>
                <p className="text-xs text-white/40 mb-4">{t('settings.aboutSettings.coreTeamDescription')}</p>
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                  {coreTeam.map((member) => (
                    <button
                      key={member.login}
                      onClick={() => openUrl(member.html_url)}
                      className="flex items-center gap-3 p-3 rounded-xl bg-white/[0.03] hover:bg-white/[0.06] border border-white/[0.06] transition-colors"
                    >
                      <img
                        src={member.avatar_url}
                        alt={member.login}
                        className="w-10 h-10 rounded-full flex-shrink-0"
                      />
                      <div className="text-left min-w-0">
                        <span className="text-white font-medium text-sm block truncate">
                          {member.displayName}
                        </span>
                        <p className="text-[11px] text-white/40 truncate">
                          {t(`settings.aboutSettings.${member.roleKey}`)}
                        </p>
                      </div>
                    </button>
                  ))}
                </div>
              </div>

              {/* Contributors Section */}
              {otherContributors.length > 0 && (
                <div className={`p-4 rounded-2xl ${gc}`}>
                  <div className="flex items-center gap-2 mb-1">
                    <Users size={18} className="text-white opacity-60" />
                    <h3 className="text-sm font-semibold text-white">{t('settings.aboutSettings.contributors')}</h3>
                  </div>
                  <p className="text-xs text-white/40 mb-3">{t('settings.aboutSettings.contributorsDescription')}</p>
                  
                  {contributorsError && (
                    <p className="text-xs text-white/35 mb-3">{contributorsError}</p>
                  )}
                  
                  <div className="grid grid-cols-4 sm:grid-cols-5 lg:grid-cols-6 gap-2">
                    {otherContributors.map((contributor) => (
                      <button
                        key={contributor.login}
                        onClick={() => openUrl(contributor.html_url)}
                        className="flex flex-col items-center gap-1.5 p-2 rounded-lg hover:bg-white/5 transition-colors"
                        title={`${contributor.login} - ${contributor.contributions} contributions`}
                      >
                        <img
                          src={contributor.avatar_url}
                          alt={contributor.login}
                          className="w-10 h-10 rounded-full"
                        />
                        <span className="text-[10px] text-white/60 max-w-full truncate text-center w-full">
                          {truncateName(contributor.login, 10)}
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

      {/* Disclaimer - shown separately on smaller screens */}
      <div className={`p-4 rounded-2xl ${gc} xl:hidden`}>
        <p className="text-white/50 text-sm text-center">
          {t('settings.aboutSettings.disclaimer')}
        </p>
      </div>
    </div>
  );
};
