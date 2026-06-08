import React from 'react';
import { useAccentColor } from '@/contexts/AccentColorContext';

export type LauncherActionVariant = 'play' | 'download' | 'stop' | 'update' | 'select';

export function LauncherActionButton({
  variant,
  className = '',
  disabled,
  title,
  onClick,
  children,
}: React.PropsWithChildren<{
  variant: LauncherActionVariant;
  className?: string;
  disabled?: boolean;
  title?: string;
  onClick?: React.MouseEventHandler<HTMLButtonElement>;
}>) {
  const { accentColor, accentTextColor } = useAccentColor();

  const base =
    'inline-flex items-center justify-center gap-2 rounded-2xl font-black tracking-tight transition-all hover:brightness-110 disabled:opacity-50 disabled:cursor-not-allowed disabled:hover:brightness-100';

  const variantClass = (() => {
    switch (variant) {
      case 'download':
        return 'bg-gradient-to-r from-green-500 to-emerald-600 text-white';
      case 'stop':
        return 'bg-gradient-to-r from-red-600 to-red-500 text-white';
      case 'update':
        return 'bg-gradient-to-r from-blue-500 to-cyan-500 text-white';
      case 'select':
        return 'bg-gradient-to-r from-purple-500 to-violet-600 text-white';
      case 'play':
      default:
        return 'text-white';
    }
  })();

  const style: React.CSSProperties | undefined =
    variant === 'play'
      ? {
          background: `linear-gradient(135deg, ${accentColor}, ${accentColor}cc)`,
          color: accentTextColor,
        }
      : undefined;

  return (
    <button
      type="button"
      title={title}
      onClick={onClick}
      disabled={disabled}
      style={style}
      className={`${base} ${variantClass} ${className}`.trim()}
    >
      {children}
    </button>
  );
}
