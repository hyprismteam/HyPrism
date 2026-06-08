import React from 'react';

export function MenuActionButton({
  onClick,
  className = '',
  children,
}: React.PropsWithChildren<{
  onClick?: React.MouseEventHandler<HTMLButtonElement>;
  className?: string;
}>) {
  const base =
    'w-full h-11 px-4 flex items-center justify-center gap-2 text-sm font-black tracking-tight transition-all text-white/80 hover:text-white hover:bg-white/[0.06]';
  return (
    <button type="button" onClick={onClick} className={`${base} ${className}`.trim()}>
      {children}
    </button>
  );
}
