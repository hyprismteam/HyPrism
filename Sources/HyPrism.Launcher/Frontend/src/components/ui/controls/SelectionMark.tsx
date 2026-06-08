import React from 'react';

export function SelectionMark({
  selected,
  variant = 'check',
  className = '',
  style,
}: {
  selected: boolean;
  variant?: 'check' | 'dot';
  className?: string;
  style?: React.CSSProperties;
}) {
  if (!selected) {
    return (
      <div
        className={`w-5 h-5 ${variant === 'dot' ? 'rounded-full' : 'rounded'} border-2 border-white/30 ${className}`.trim()}
      />
    );
  }

  return (
    <div
      className={`w-5 h-5 flex items-center justify-center ${variant === 'dot' ? 'rounded-full' : 'rounded'} border-2 ${className}`.trim()}
      style={style}
    >
      {variant === 'dot' ? <div className="w-2 h-2 rounded-full bg-current" /> : <span className="text-[12px] leading-none">✓</span>}
    </div>
  );
}
