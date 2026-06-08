import React from 'react';
import { Check } from 'lucide-react';
import { useAccentColor } from '@/contexts/AccentColorContext';

/**
 * A radio-style option card with icon, title, description, and a circular radio indicator.
 *
 * Used for mutually-exclusive settings (Java runtime mode, auth server, etc.)
 */
export interface RadioOptionCardProps {
  /** Lucide icon or any ReactNode rendered inside a 32×32 rounded box. */
  icon: React.ReactNode;
  /** Primary label */
  title: React.ReactNode;
  /** Secondary description below the title */
  description?: React.ReactNode;
  /** Whether this option is currently selected */
  selected: boolean;
  /** Called when this option is clicked */
  onClick: () => void;
  /** Disables the card */
  disabled?: boolean;
  /** Optional children rendered below the card header (e.g. expanded input for "custom" mode) */
  children?: React.ReactNode;
  className?: string;
}

export function RadioOptionCard({
  icon,
  title,
  description,
  selected,
  onClick,
  disabled = false,
  children,
  className = '',
}: RadioOptionCardProps) {
  const { accentColor, accentTextColor } = useAccentColor();

  const hasChildren = React.Children.count(children) > 0;

  return (
    <div
      className={`rounded-xl border transition-all ${
        selected
          ? 'border-white/20'
          : 'border-white/[0.06] hover:border-white/[0.12] bg-[#1c1c1e]'
      } ${disabled ? 'opacity-50 cursor-not-allowed' : ''} ${className}`.trim()}
      style={selected ? { backgroundColor: `${accentColor}15`, borderColor: `${accentColor}50` } : undefined}
    >
      <div
        className={`flex items-center gap-3 p-4 ${!disabled ? 'cursor-pointer' : ''}`}
        onClick={() => !disabled && onClick()}
      >
        <div
          className="w-8 h-8 rounded-lg flex items-center justify-center"
          style={{ backgroundColor: selected ? `${accentColor}25` : 'rgba(255,255,255,0.06)' }}
        >
          <span className={selected ? '' : 'text-white/70'} style={selected ? { color: accentColor } : undefined}>
            {icon}
          </span>
        </div>
        <div className="flex-1 min-w-0">
          <span className="text-white text-sm font-medium">{title}</span>
          {description && <p className="text-xs text-white/40">{description}</p>}
        </div>
        <div
          className={`w-5 h-5 rounded-full border-2 flex items-center justify-center transition-all flex-shrink-0 ${
            selected ? '' : 'border-white/30'
          }`}
          style={selected ? { borderColor: accentColor, backgroundColor: accentColor } : undefined}
        >
          {selected && <Check size={12} style={{ color: accentTextColor }} strokeWidth={3} />}
        </div>
      </div>

      {hasChildren && selected && (
        <div className="px-4 pb-4">{children}</div>
      )}
    </div>
  );
}
