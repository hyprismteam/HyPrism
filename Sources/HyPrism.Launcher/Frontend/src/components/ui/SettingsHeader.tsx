import React from 'react';

interface SettingsHeaderProps {
  title: React.ReactNode;
  description?: React.ReactNode;
  actions?: React.ReactNode;
  className?: string;
  size?: 'md' | 'sm';
}

export function SettingsHeader({ title, description, actions, className = '', size = 'md' }: SettingsHeaderProps) {
  return (
    <div className={`flex items-start justify-between gap-4 ${className}`.trim()}>
      <div className="min-w-0">
        <h1 className={`${size === 'sm' ? 'text-lg' : 'text-xl'} font-semibold text-white/90 truncate`}>{title}</h1>
        {description ? (
          <p className={`${size === 'sm' ? 'mt-0.5 text-xs' : 'mt-1 text-sm'} text-white/50 leading-relaxed`}>{description}</p>
        ) : null}
      </div>
      {actions ? <div className="flex items-center gap-2 flex-shrink-0">{actions}</div> : null}
    </div>
  );
}
