import React from 'react';
import { useAccentColor } from '@/contexts/AccentColorContext';
import { SegmentedControl } from './SegmentedControl';

export type AccentSegmentItem<TValue extends string> = {
  value: TValue;
  label: React.ReactNode;
  className?: string;
  disabled?: boolean;
  title?: string;
};

export function AccentSegmentedControl<TValue extends string>({
  value,
  onChange,
  items,
  className = '',
}: {
  value: TValue;
  onChange: (next: TValue) => void;
  items: AccentSegmentItem<TValue>[];
  className?: string;
}) {
  const { accentColor, accentTextColor } = useAccentColor();

  return (
    <SegmentedControl<TValue>
      value={value}
      onChange={onChange}
      className={className}
      sliderStyle={{
        background: `linear-gradient(135deg, ${accentColor}, ${accentColor}cc)`,
        borderColor: 'rgba(255,255,255,0.10)',
      }}
      items={items.map((i) => ({
        value: i.value,
        label: i.label,
        className: `${i.className ?? ''} font-black tracking-tight text-sm`.trim(),
        disabled: i.disabled,
        title: i.title,
        selectedStyle: { color: accentTextColor },
      }))}
    />
  );
}
