import React from 'react';
import { Check } from 'lucide-react';
import { useAccentColor } from '@/contexts/AccentColorContext';

export interface SelectableCheckboxProps {
  /** Whether the checkbox is checked */
  checked: boolean;
  /** Called when the checkbox is clicked */
  onChange: (checked: boolean) => void;
  /** Size variant */
  size?: 'sm' | 'md';
  /** Additional class names */
  className?: string;
  /** Disabled state */
  disabled?: boolean;
  /** Title attribute for hover hint */
  title?: string;
  /** Click handler for the wrapper (useful for stopping propagation) */
  onClick?: (e: React.MouseEvent) => void;
}

const sizeClasses = {
  sm: { box: 'w-4 h-4', icon: 10 },
  md: { box: 'w-5 h-5', icon: 12 },
};

/**
 * A styled checkbox component that uses the app's accent color.
 * Replaces the repeated checkbox pattern used in mod lists and bulk operations.
 */
export const SelectableCheckbox: React.FC<SelectableCheckboxProps> = ({
  checked,
  onChange,
  size = 'md',
  className = '',
  disabled = false,
  title,
  onClick,
}) => {
  const { accentColor, accentTextColor } = useAccentColor();
  const { box, icon } = sizeClasses[size];

  const handleClick = (e: React.MouseEvent) => {
    onClick?.(e);
    if (!disabled) {
      onChange(!checked);
    }
  };

  return (
    <button
      type="button"
      onClick={handleClick}
      disabled={disabled}
      title={title}
      className={`
        ${box} rounded border-2 flex items-center justify-center flex-shrink-0
        transition-colors disabled:opacity-50 disabled:cursor-not-allowed
        ${checked ? '' : 'bg-transparent border-white/30 hover:border-white/50'}
        ${className}
      `}
      style={checked ? { backgroundColor: accentColor, borderColor: accentColor } : undefined}
    >
      {checked && <Check size={icon} style={{ color: accentTextColor }} />}
    </button>
  );
};

export default SelectableCheckbox;
