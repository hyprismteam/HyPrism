import { useAccentColor } from '@/contexts/AccentColorContext';

export function Switch({
  checked,
  onCheckedChange,
  disabled = false,
  className = '',
}: {
  checked: boolean;
  onCheckedChange: (next: boolean) => void;
  disabled?: boolean;
  className?: string;
}) {
  const { accentColor, accentTextColor } = useAccentColor();

  return (
    <button
      type="button"
      disabled={disabled}
      onClick={() => onCheckedChange(!checked)}
      className={`h-7 w-12 rounded-full flex items-center transition-all duration-200 flex-shrink-0 disabled:opacity-50 disabled:cursor-not-allowed ${className}`.trim()}
      style={{ backgroundColor: checked ? accentColor : 'rgba(255,255,255,0.15)' }}
      aria-pressed={checked}
    >
      <div
        className={`w-5 h-5 rounded-full shadow-md transform transition-all duration-200 ${checked ? 'translate-x-6' : 'translate-x-1'}`}
        style={{ backgroundColor: checked ? accentTextColor : 'white' }}
      />
    </button>
  );
}
