import React from 'react';
import { ChevronDown } from 'lucide-react';

export function DropdownTriggerButton({
  label,
  prefix,
  open,
  disabled,
  title,
  onClick,
  className = '',
}: {
  label: React.ReactNode;
  prefix?: React.ReactNode;
  open?: boolean;
  disabled?: boolean;
  title?: string;
  onClick?: React.MouseEventHandler<HTMLButtonElement>;
  className?: string;
}) {
  const base =
    'relative h-10 px-4 pr-10 rounded-xl bg-white/5 border border-white/10 text-white/80 text-sm hover:border-white/20 flex items-center gap-2 whitespace-nowrap transition-colors disabled:opacity-50 disabled:cursor-not-allowed disabled:hover:border-white/10';

  return (
    <button
      type="button"
      title={title}
      onClick={onClick}
      disabled={disabled}
      className={`${base} ${className}`.trim()}
    >
      {prefix ? <span className="text-white/50">{prefix}</span> : null}
      <span className="truncate min-w-0">{label}</span>
      <ChevronDown
        size={14}
        className={`absolute right-3 text-white/40 transition-transform ${open ? 'rotate-180' : ''}`.trim()}
      />
    </button>
  );
}
