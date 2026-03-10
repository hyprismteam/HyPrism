/**
 * Shared Java argument utility functions.
 * Used by useSettings and useJavaSettings hooks.
 */

/**
 * Parses a JVM heap size flag from a Java arguments string and returns the value in megabytes.
 * Supports K (kilobytes), M (megabytes), and G (gigabytes) suffixes.
 * @param args - The full Java arguments string (e.g. "-Xmx4G -Xms512M").
 * @param flag - The heap flag to extract: `'xmx'` for max heap or `'xms'` for initial heap.
 * @returns The parsed heap size in MB, or `null` if the flag is absent or invalid.
 */
export const parseJavaHeapMb = (args: string, flag: 'xmx' | 'xms'): number | null => {
  const match = args.match(new RegExp(`(?:^|\\s)-${flag}(\\d+(?:\\.\\d+)?)([kKmMgG])(?:\\s|$)`, 'i'));
  if (!match) return null;

  const value = Number.parseFloat(match[1]);
  if (!Number.isFinite(value) || value <= 0) return null;

  const unit = match[2].toUpperCase();
  if (unit === 'G') return Math.round(value * 1024);
  if (unit === 'K') return Math.max(1, Math.round(value / 1024));
  return Math.round(value);
};

/**
 * Inserts or replaces a JVM heap size argument (`-Xmx` or `-Xms`) in a Java arguments string.
 * Any existing occurrences of the flag are removed before the new value is prepended.
 * @param args - The current Java arguments string.
 * @param flag - The heap flag to upsert: `'Xmx'` for max heap or `'Xms'` for initial heap.
 * @param ramMb - The desired heap size in megabytes.
 * @returns The updated arguments string with the new heap flag applied.
 */
export const upsertJavaHeapArgument = (args: string, flag: 'Xmx' | 'Xms', ramMb: number): string => {
  const pattern = new RegExp(`(?:^|\\s)-${flag}\\S+`, 'gi');
  const sanitized = args.replace(pattern, ' ').replace(/\s+/g, ' ').trim();
  const heapArg = `-${flag}${ramMb}M`;
  return sanitized.length > 0 ? `${heapArg} ${sanitized}` : heapArg;
};

/**
 * Removes all occurrences of a JVM flag matched by the given pattern from a Java arguments string.
 * Extra whitespace is collapsed and the result is trimmed.
 * @param args - The Java arguments string to process.
 * @param pattern - A regex pattern matching the flag(s) to remove.
 * @returns The cleaned arguments string with matched flags removed.
 */
export const removeJavaFlag = (args: string, pattern: RegExp): string => {
  return args.replace(pattern, ' ').replace(/\s+/g, ' ').trim();
};

/**
 * Inserts or removes the `-XX:+UseG1GC` flag in a Java arguments string based on the desired GC mode.
 * Any existing G1GC flag is stripped before applying the new setting.
 * @param args - The current Java arguments string.
 * @param mode - `'g1'` to enable G1 garbage collector, `'auto'` to leave JVM default (flag removed).
 * @returns The updated arguments string with the GC flag applied or removed.
 */
export const upsertJavaGcMode = (args: string, mode: 'auto' | 'g1'): string => {
  const withoutGc = removeJavaFlag(args, /(?:^|\s)-XX:[+-]UseG1GC(?:\s|$)/gi);
  if (mode === 'auto') return withoutGc;
  return withoutGc.length > 0 ? `-XX:+UseG1GC ${withoutGc}` : '-XX:+UseG1GC';
};

/**
 * Detects the active GC mode from a Java arguments string.
 * @param args - The Java arguments string to inspect.
 * @returns `'g1'` if `-XX:+UseG1GC` is present, otherwise `'auto'`.
 */
export const detectJavaGcMode = (args: string): 'auto' | 'g1' => {
  return /(?:^|\s)-XX:\+UseG1GC(?:\s|$)/i.test(args) ? 'g1' : 'auto';
};

/**
 * Sanitizes a user-supplied Java arguments string by stripping dangerous flags
 * that could compromise launcher or JVM integrity (e.g. `-javaagent`, `-classpath`, `-jar`).
 * @param args - The raw Java arguments string provided by the user.
 * @returns An object with:
 *   - `sanitized`: the cleaned arguments string (empty string if all args were removed),
 *   - `blocked`: `true` if at least one dangerous flag was found and removed.
 */
export const sanitizeAdvancedJavaArguments = (args: string): { sanitized: string; blocked: boolean } => {
  let result = args;

  const blockedPatterns = [
    /(?:^|\s)-javaagent:\S+/gi,
    /(?:^|\s)-agentlib:\S+/gi,
    /(?:^|\s)-agentpath:\S+/gi,
    /(?:^|\s)-Xbootclasspath(?::\S+)?/gi,
    /(?:^|\s)-jar(?:\s+\S+)?/gi,
    /(?:^|\s)-cp(?:\s+\S+)?/gi,
    /(?:^|\s)-classpath(?:\s+\S+)?/gi,
    /(?:^|\s)--class-path(?:\s+\S+)?/gi,
    /(?:^|\s)--module-path(?:\s+\S+)?/gi,
    /(?:^|\s)-Djava\.home=\S+/gi,
  ];

  const hadBlocked = blockedPatterns.some((pattern) => pattern.test(result));

  for (const pattern of blockedPatterns) {
    result = result.replace(pattern, ' ');
  }

  result = result.replace(/\s+/g, ' ').trim();
  return { sanitized: result, blocked: hadBlocked };
};

/**
 * Formats a RAM amount in megabytes as a human-readable gigabyte string.
 * Whole-number values are displayed without decimals (e.g. `"4 GB"`);
 * fractional values are shown with one decimal place (e.g. `"1.5 GB"`).
 * @param ramMb - RAM amount in megabytes.
 * @returns Formatted string such as `"2 GB"` or `"2.5 GB"`.
 */
export const formatRamLabel = (ramMb: number): string => {
  const gb = ramMb / 1024;
  return Number.isInteger(gb) ? `${gb} GB` : `${gb.toFixed(1)} GB`;
};
