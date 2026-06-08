import React, { useCallback, useEffect, useRef, useState } from 'react';

export function SegmentedControl<TValue extends string>({
  value,
  onChange,
  items,
  className = '',
  sliderStyle,
}: {
  value: TValue;
  onChange: (next: TValue) => void;
  items: Array<{ value: TValue; label: React.ReactNode; className?: string; selectedClassName?: string; selectedStyle?: React.CSSProperties; disabled?: boolean; title?: string }>;
  className?: string;
  sliderStyle?: React.CSSProperties;
}) {
  const containerRef = useRef<HTMLDivElement>(null);
  const itemRefs = useRef<Record<string, HTMLButtonElement | null>>({});
  const [slider, setSlider] = useState<{ left: number; width: number; ready: boolean }>({ left: 0, width: 0, ready: false });

  const updateSlider = useCallback(() => {
    const container = containerRef.current;
    const btn = itemRefs.current[value];
    if (!container || !btn) return;
    const containerRect = container.getBoundingClientRect();
    const btnRect = btn.getBoundingClientRect();
    if (btnRect.width <= 0) return;
    setSlider({ left: btnRect.left - containerRect.left, width: btnRect.width, ready: true });
  }, [value]);

  useEffect(() => {
    const id = requestAnimationFrame(updateSlider);
    window.addEventListener('resize', updateSlider);
    return () => {
      cancelAnimationFrame(id);
      window.removeEventListener('resize', updateSlider);
    };
  }, [updateSlider]);

  return (
    <div
      role="tablist"
      aria-label="Segmented control"
      ref={containerRef}
      className={`relative flex items-center gap-1 p-1 rounded-2xl glass-control-solid ${className}`.trim()}
    >
      <div
        className="absolute rounded-2xl bg-white/10 border border-white/15"
        style={{
          left: slider.left,
          width: slider.width,
          top: '0.25rem',
          bottom: '0.25rem',
          opacity: slider.ready ? 1 : 0,
          transition: slider.ready
            ? 'left 0.3s cubic-bezier(0.4, 0, 0.2, 1), width 0.25s cubic-bezier(0.4, 0, 0.2, 1)'
            : 'none',
          ...(sliderStyle ?? {}),
        }}
      />

      {items.map((item) => {
        const selected = item.value === value;
        return (
          <button
            key={item.value}
            type="button"
            role="tab"
            aria-selected={selected}
            title={item.title}
            ref={(el) => {
              itemRefs.current[item.value] = el;
            }}
            disabled={item.disabled}
            onClick={() => !item.disabled && onChange(item.value)}
            className={`relative z-10 h-10 px-4 rounded-2xl text-xs font-medium transition-all border ${
              selected
                ? (item.selectedClassName ?? 'border-transparent')
                : item.disabled
                  ? 'border-transparent opacity-40 cursor-not-allowed'
                  : 'border-transparent hover:bg-white/[0.06]'
            } ${item.className ?? ''}`.trim()}
            style={selected ? item.selectedStyle : undefined}
          >
            {item.label}
          </button>
        );
      })}
    </div>
  );
}
