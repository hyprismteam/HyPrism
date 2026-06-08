/**
 * Shared Framer Motion animation variants used across pages and components.
 */

/** Standard page enter/exit transition */
export const pageVariants = {
  initial: { opacity: 0, y: 12 },
  animate: { opacity: 1, y: 0 },
  exit: { opacity: 0, y: -12 },
} as const;
