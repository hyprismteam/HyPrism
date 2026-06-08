/**
 * Shared formatting utilities
 */

/**
 * Format bytes to human-readable size string
 * @param bytes - Number of bytes
 * @returns Formatted string like "256 MB" or "1.5 GB"
 */
export const formatBytes = (bytes: number): string => {
  if (bytes === 0) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return `${(bytes / Math.pow(k, i)).toFixed(i > 1 ? 1 : 0)} ${sizes[i]}`;
};
