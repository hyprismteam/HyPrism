import type { InstanceValidationDetails } from '@/lib/ipc';

/** Represents a mod installed inside a game instance. */
export interface InstalledModInfo {
  /** Unique mod identifier (e.g. a CurseForge numeric ID string or a `"local-…"` prefix). */
  id: string;
  /** Display name of the mod. */
  name: string;
  /** CurseForge slug used to construct the mod page URL. */
  slug?: string;
  /** Installed version string. */
  version: string;
  /** Name of the JAR file on disk. */
  fileName?: string;
  /** Author display name. */
  author: string;
  /** Short description or summary. */
  description: string;
  /** Whether the mod is currently enabled (`.jar`) vs disabled (`.jar.disabled`). */
  enabled: boolean;
  /** URL of the mod icon image. */
  iconUrl?: string;
  /** Total CurseForge download count. */
  downloads?: number;
  /** Primary category name. */
  category?: string;
  /** All category names. */
  categories?: string[];
  /** Numeric CurseForge mod identifier used for update-checking. */
  curseForgeId?: number;
  /** CurseForge file ID of the installed file. */
  fileId?: number;
  /** CurseForge release type (`1` = Release, `2` = Beta, `3` = Alpha). */
  releaseType?: number;
  /** The latest available version string as returned by the update check. */
  latestVersion?: string;
  /** The CurseForge file ID of the latest available file. */
  latestFileId?: number;
}

/** Represents an installed game instance with its metadata and validation state. */
export interface InstalledVersionInfo {
  /** Unique instance identifier (UUID). */
  id: string;
  /** Game branch the instance belongs to (e.g. `"release"` or `"pre-release"`). */
  branch: string;
  /** Patch version number; `0` means "latest". */
  version: number;
  /** Absolute path to the instance directory on disk. */
  path: string;
  /** Total disk usage of the instance in bytes. */
  sizeBytes?: number;
  /** Whether this instance is the latest version for its branch. */
  isLatest?: boolean;
  /** `true` when `version === 0` (always tracks the latest build). */
  isLatestInstance?: boolean;
  /** Accumulated play time in seconds. */
  playTimeSeconds?: number;
  /** Human-readable play-time string. */
  playTimeFormatted?: string;
  /** ISO 8601 creation timestamp. */
  createdAt?: string;
  /** ISO 8601 timestamp of the last play session. */
  lastPlayedAt?: string;
  /** ISO 8601 timestamp of the last modification. */
  updatedAt?: string;
  /** File path to the custom icon image. */
  iconPath?: string;
  /** Optional user-defined display name for the instance. */
  customName?: string;
  /** Result of the last file-integrity validation pass. */
  validationStatus?: 'Valid' | 'NotInstalled' | 'Corrupted' | 'Unknown';
  /** Detailed information from the last validation pass. */
  validationDetails?: InstanceValidationDetails;
}

/** The tab identifier for the instances detail panel. */
export type InstanceTab = 'content' | 'browse' | 'worlds';
