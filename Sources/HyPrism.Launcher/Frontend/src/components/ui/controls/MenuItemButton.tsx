import React from 'react';

export function MenuItemButton({
  onClick,
  disabled,
  variant = 'default',
  className = '',
  style,
  children,
}: React.PropsWithChildren<{
  onClick?: React.MouseEventHandler<HTMLButtonElement>;
  disabled?: boolean;
  variant?: 'default' | 'danger';
  className?: string;
  style?: React.CSSProperties;
}>) {
  const base =
    'w-full px-4 py-2.5 text-sm text-left flex items-center gap-2 transition-all disabled:opacity-50 disabled:cursor-not-allowed';
  const v =
    variant === 'danger'
      ? 'text-red-400 hover:text-red-300 hover:bg-red-500/10'
      : 'text-white/70 hover:text-white hover:bg-white/10';

  return (
    <button type="button" onClick={onClick} disabled={disabled} style={style} className={`${base} ${v} ${className}`.trim()}>
      {children}
    </button>
  );
}
