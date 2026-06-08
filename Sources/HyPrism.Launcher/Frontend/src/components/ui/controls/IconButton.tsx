import React from 'react';

type CommonButtonProps = {
  className?: string;
  disabled?: boolean;
  title?: string;
};

export function IconButton({
  className = '',
  disabled,
  title,
  variant = 'default',
  size = 'md',
  onClick,
  children,
  style,
}: React.PropsWithChildren<
  CommonButtonProps & {
    variant?: 'default' | 'ghost' | 'overlay';
    size?: 'sm' | 'md' | 'lg';
    onClick?: React.MouseEventHandler<HTMLButtonElement>;
    style?: React.CSSProperties;
  }
>) {
  const base =
    variant === 'overlay'
      ? 'border border-white/10 bg-black/30 text-white/80 hover:text-white hover:bg-black/40'
      : variant === 'ghost'
        ? 'glass-control-solid border border-transparent bg-transparent text-white/60 hover:text-white hover:bg-white/[0.06]'
        : 'glass-control-solid border border-white/[0.06] text-white/60 hover:text-white hover:bg-white/[0.06] hover:border-white/20';

  const s = size === 'sm' ? 'h-9 w-9' : size === 'lg' ? 'h-11 w-11' : 'h-10 w-10';

  return (
    <button
      type="button"
      title={title}
      disabled={disabled}
      onClick={onClick}
      style={style}
      className={`${s} rounded-full ${base} transition-all disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-transparent flex items-center justify-center ${className}`.trim()}
    >
      {children}
    </button>
  );
}
