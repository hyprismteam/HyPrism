// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

import React, { createContext, useContext, useEffect, ReactNode } from 'react';

interface AccentColorContextType {
  accentColor: string;
  accentTextColor: string; // Color for text on accent background (white or black)
}

const AccentColorContext = createContext<AccentColorContextType | undefined>(undefined);

// Helper to convert hex to RGB values
const hexToRgb = (hex: string): { r: number; g: number; b: number } | null => {
  const result = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex);
  return result
    ? {
        r: parseInt(result[1], 16),
        g: parseInt(result[2], 16),
        b: parseInt(result[3], 16),
      }
    : null;
};

// Calculate relative luminance using WCAG formula
const getLuminance = (r: number, g: number, b: number): number => {
  const [rs, gs, bs] = [r, g, b].map((c) => {
    const srgb = c / 255;
    return srgb <= 0.03928 ? srgb / 12.92 : Math.pow((srgb + 0.055) / 1.055, 2.4);
  });
  return 0.2126 * rs + 0.7152 * gs + 0.0722 * bs;
};

// Get contrast text color (black or white) based on background luminance
export const getContrastTextColor = (hex: string): string => {
  const rgb = hexToRgb(hex);
  if (!rgb) return '#FFFFFF';
  const luminance = getLuminance(rgb.r, rgb.g, rgb.b);
  // Use a threshold of 0.5 for deciding between black and white text
  return luminance > 0.5 ? '#000000' : '#FFFFFF';
};

// Update CSS variables on the document root
const updateCssVariables = (color: string) => {
  const root = document.documentElement;
  const rgb = hexToRgb(color);
  const contrastColor = getContrastTextColor(color);
  
  root.style.setProperty('--accent-color', color);
  root.style.setProperty('--accent-text-color', contrastColor);
  
  if (rgb) {
    root.style.setProperty('--accent-r', String(rgb.r));
    root.style.setProperty('--accent-g', String(rgb.g));
    root.style.setProperty('--accent-b', String(rgb.b));
  }
};

export const AccentColorProvider: React.FC<{ children: ReactNode }> = ({ children }) => {
  const accentColor = '#F5F5F6';
  const accentTextColor = '#111214';

  useEffect(() => {
    updateCssVariables(accentColor);
  }, []);

  return (
    <AccentColorContext.Provider value={{ accentColor, accentTextColor }}>
      {children}
    </AccentColorContext.Provider>
  );
};

export const useAccentColor = (): AccentColorContextType => {
  const context = useContext(AccentColorContext);
  if (context === undefined) {
    throw new Error('useAccentColor must be used within an AccentColorProvider');
  }
  return context;
};

export default AccentColorContext;
