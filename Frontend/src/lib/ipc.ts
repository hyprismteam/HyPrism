/*
 .-..-.      .---.       _
 : :; :      : .; :     :_;
 :    :.-..-.:  _.'.--. .-. .--. ,-.,-.,-.
 : :: :: :; :: :   : ..': :`._-.': ,. ,. :
 :_;:_;`._. ;:_;   :_;  :_; `.__.':_;:_;:_;
        .-. :
        `._.'             HyPrism.IpcGen (Roslyn)

 AUTO-GENERATED — DO NOT EDIT BY HAND.
 Source of truth: IpcService.cs  (attributes + method signatures)
 Re-generate: dotnet build  (target GenerateIpcTs)
*/

// eslint-disable-next-line @typescript-eslint/no-explicit-any
declare global {
  interface Window {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    require: (module: string) => any;
  }
}

const { ipcRenderer } = window.require('electron');

export function send(channel: string, data?: unknown): void {
  ipcRenderer.send(channel, JSON.stringify(data));
}

export function on<T>(channel: string, cb: (data: T) => void): () => void {
  const handler = (_: unknown, raw: string) => {
    try { cb(JSON.parse(raw) as T); } catch { /* ignore */ }
  };
  ipcRenderer.on(channel, handler);
  return () => ipcRenderer.removeListener(channel, handler);
}

export function invoke<T>(channel: string, data?: unknown, timeoutMs = 10_000): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const replyChannel = channel + ':reply';
    let done = false;

    const timer = timeoutMs > 0
      ? setTimeout(() => {
          if (!done) { done = true; reject(new Error(`IPC timeout: ${channel}`)); }
        }, timeoutMs)
      : null;

    ipcRenderer.once(replyChannel, (_: unknown, raw: string) => {
      if (done) return;
      done = true;
      if (timer !== null) clearTimeout(timer);
      try { resolve(JSON.parse(raw) as T); } catch (e) { reject(e); }
    });

    send(channel, data);
  });
}

export interface SuccessResult {
  success: boolean;
  error?: string | null;
}

export interface LauncherUpdateInfo {
  currentVersion: string;
  latestVersion: string;
  changelog?: string | null;
  downloadUrl?: string | null;
  assetName?: string | null;
  releaseUrl?: string | null;
  isBeta?: boolean | null;
}

export interface LauncherUpdateProgress {
  stage: string;
  progress: number;
  message: string;
  downloadedBytes?: number | null;
  totalBytes?: number | null;
  downloadedFilePath?: string | null;
  hasDownloadedFile?: boolean | null;
}

export interface AppConfig {
  language: string;
  dataDirectory: string;
}

export interface LaunchGameRequest {
  instanceId?: string | null;
  launchAfterDownload?: boolean | null;
}

export interface InstalledInstance {
  id: string;
  branch: string;
  version: number;
  path: string;
  hasUserData: boolean;
  userDataSize: number;
  totalSize: number;
  isValid: boolean;
  validationStatus: 'Valid' | 'NotInstalled' | 'Corrupted' | 'Unknown';
  validationDetails?: InstanceValidationDetails | null;
  customName?: string | null;
}

export interface GetVersionsRequest {
  branch?: string | null;
}

export interface VersionListResponse {
  versions: VersionInfo[];
  hasOfficialAccount: boolean;
  officialSourceAvailable: boolean;
  hasDownloadSources: boolean;
  enabledMirrorCount: number;
}

export interface ProgressUpdate {
  state: string;
  progress: number;
  messageKey: string;
  args?: unknown[] | null;
  downloadedBytes: number;
  totalBytes: number;
}

export interface GameState {
  state: string;
  exitCode: number;
}

export interface GameError {
  type: string;
  message: string;
  technical?: string | null;
}

export interface InstanceInfo {
  id: string;
  name: string;
  branch: string;
  version: number;
  isInstalled: boolean;
}

export interface CreateInstanceRequest {
  branch: string;
  version: number;
  customName?: string | null;
  isLatest?: boolean | null;
}

export interface InstanceIdRequest {
  instanceId: string;
}

export interface SelectInstanceRequest {
  id: string;
}

export interface RenameInstanceRequest {
  instanceId: string;
  customName?: string | null;
}

export interface ChangeVersionRequest {
  instanceId: string;
  branch: string;
  version: number;
}

export interface SaveInfo {
  name: string;
  previewPath?: string | null;
  lastModified?: string | null;
  sizeBytes?: number | null;
}

export interface OpenSaveFolderRequest {
  instanceId: string;
  saveName: string;
}

export interface SetIconRequest {
  instanceId: string;
  iconBase64: string;
}

export interface NewsItemResponse {
  title: string;
  excerpt: string;
  url: string;
  date: string;
  publishedAt: string;
  author: string;
  imageUrl?: string | null;
  source: string;
}

export interface ProfileSnapshot {
  nick: string;
  uuid: string;
  avatarPath?: string | null;
}

export interface IpcProfile {
  id: string;
  name: string;
  uuid?: string | null;
  isOfficial?: boolean | null;
  avatar?: string | null;
  folderName?: string | null;
}

export interface SwitchProfileRequest {
  id: string;
}

export interface CreateProfileRequest {
  name: string;
  uuid: string;
  isOfficial?: boolean | null;
}

export interface HytaleAuthStatus {
  loggedIn: boolean;
  username?: string | null;
  uuid?: string | null;
  error?: string | null;
  errorType?: string | null;
}

export interface SettingsSnapshot {
  language: string;
  musicEnabled: boolean;
  launcherBranch: string;
  versionType: string;
  selectedVersion: number;
  closeAfterLaunch: boolean;
  launchAfterDownload: boolean;
  showDiscordAnnouncements: boolean;
  disableNews: boolean;
  backgroundMode: string;
  availableBackgrounds: string[];
  accentColor: string;
  hasCompletedOnboarding: boolean;
  onlineMode: boolean;
  authDomain: string;
  dataDirectory: string;
  instanceDirectory: string;
  showAlphaMods: boolean;
  launcherVersion: string;
  javaArguments?: string | null;
  useCustomJava?: boolean | null;
  customJavaPath?: string | null;
  systemMemoryMb?: number | null;
  gpuPreference?: string | null;
  gameEnvironmentVariables?: string | null;
  useDualAuth?: boolean | null;
  launchOnStartup?: boolean | null;
  minimizeToTray?: boolean | null;
}

export interface UpdateSettingsRequest {
  updates: Record<string, JsonElement>;
}

export interface MirrorSpeedTestResult {
  mirrorId: string;
  mirrorUrl: string;
  mirrorName: string;
  pingMs: number;
  speedMBps: number;
  isAvailable: boolean;
  testedAt: string;
}

export interface TestMirrorSpeedRequest {
  mirrorId: string;
  forceRefresh?: boolean | null;
}

export interface TestOfficialSpeedRequest {
  forceRefresh?: boolean | null;
}

export interface DownloadSourcesSummary {
  hasDownloadSources: boolean;
  hasOfficialAccount: boolean;
  enabledMirrorCount: number;
}

export interface MirrorInfo {
  id: string;
  name: string;
  priority: number;
  enabled: boolean;
  sourceType: string;
  hostname: string;
  description?: string | null;
}

export interface AddMirrorResult {
  success: boolean;
  error?: string | null;
  mirror?: MirrorInfo | null;
}

export interface AddMirrorRequest {
  url: string;
  headers?: string | null;
}

export interface MirrorIdRequest {
  mirrorId: string;
}

export interface ToggleMirrorRequest {
  mirrorId: string;
  enabled: boolean;
}

export interface SetInstanceDirResult {
  success: boolean;
  path: string;
  noop?: boolean | null;
  reason?: string | null;
  error?: string | null;
}

export interface AuthServerPingResult {
  isAvailable: boolean;
  pingMs: number;
  authDomain: string;
  checkedAt: string;
  isOfficial: boolean;
  error?: string | null;
}

export interface PingAuthServerRequest {
  authDomain?: string | null;
}

export interface SetLanguageResult {
  success: boolean;
  language: string;
}

export interface LanguageInfo {
  code: string;
  name: string;
}

export interface InstalledMod {
  id: string;
  name: string;
  slug: string;
  version: string;
  fileId: string;
  fileName: string;
  enabled: boolean;
  author: string;
  description: string;
  iconUrl: string;
  curseForgeId: string;
  fileDate: string;
  releaseType: number;
  screenshots: CurseForgeScreenshot[];
  latestFileId: string;
  latestVersion: string;
  disabledOriginalExtension: string;
}

export interface ModSearchResult {
  mods: ModInfo[];
  totalCount: number;
}

export interface ModSearchRequest {
  query: string;
  page: number;
  pageSize: number;
  sortField: number;
  sortOrder: number;
  categories: string[];
}

export interface ModInstalledRequest {
  instanceId?: string | null;
}

export interface ModUninstallRequest {
  modId: string;
  instanceId?: string | null;
}

export interface ModCheckUpdatesRequest {
  instanceId?: string | null;
}

export interface ModInstallRequest {
  modId: string;
  fileId: string;
  instanceId?: string | null;
}

export interface ModFilesResult {
  files: ModFileInfo[];
  totalCount: number;
}

export interface ModFilesRequest {
  modId: string;
  page?: number | null;
  pageSize?: number | null;
}

export interface ModInfo {
  id: string;
  name: string;
  slug: string;
  summary: string;
  description: string;
  author: string;
  downloadCount: number;
  iconUrl: string;
  thumbnailUrl: string;
  categories: string[];
  dateUpdated: string;
  latestFileId: string;
  screenshots: CurseForgeScreenshot[];
}

export interface ModInfoRequest {
  modId: string;
}

export interface ModChangelogRequest {
  modId: string;
  fileId: string;
}

export interface ModCategory {
  id: number;
  name: string;
  slug: string;
}

export interface ModInstallLocalRequest {
  sourcePath: string;
  instanceId?: string | null;
}

export interface ModInstallBase64Request {
  fileName: string;
  base64Content: string;
  instanceId?: string | null;
}

export interface ModOpenFolderRequest {
  instanceId?: string | null;
}

export interface ModToggleRequest {
  modId: string;
  instanceId?: string | null;
}

export interface ModExportRequest {
  instanceId?: string | null;
  exportPath: string;
  exportType?: string | null;
}

export interface ModImportListRequest {
  listPath: string;
}

export interface GpuAdapterInfo {
  name: string;
  vendor: string;
  pciId: string;
  type: string;
}

export interface PlatformInfo {
  os: string;
  isLinux: boolean;
  isWindows: boolean;
  isMacOS: boolean;
}

export interface GetLogsRequest {
  count?: number | null;
}

export interface InstanceValidationDetails {
  hasExecutable: boolean;
  hasAssets: boolean;
  hasLibraries: boolean;
  hasConfig: boolean;
  missingComponents: string[];
  errorMessage?: string | null;
}

export interface VersionInfo {
  version: number;
  source: 'Official' | 'Mirror';
  isLatest: boolean;
}

export interface JsonElement {
  valueKind: 'Undefined' | 'Object' | 'Array' | 'String' | 'Number' | 'True' | 'False' | 'Null';
}

export interface CurseForgeScreenshot {
  id: number;
  title?: string | null;
  thumbnailUrl?: string | null;
  url?: string | null;
}

export interface ModFileInfo {
  id: string;
  modId: string;
  fileName: string;
  displayName: string;
  downloadUrl: string;
  fileLength: number;
  fileDate: string;
  releaseType: number;
  gameVersions: string[];
  downloadCount: number;
}

const _auth = {
  status: () => invoke<HytaleAuthStatus>('hyprism:auth:status', {}),
  login: () => invoke<HytaleAuthStatus>('hyprism:auth:login', {}),
  logout: () => invoke<SuccessResult>('hyprism:auth:logout', {}),
};

const _browser = {
  open: (data: string) => send('hyprism:browser:open', data),
};

const _config = {
  get: () => invoke<AppConfig>('hyprism:config:get', {}),
  save: () => invoke<SuccessResult>('hyprism:config:save', {}),
};

const _consoleCtl = {
  log: (data: string) => send('hyprism:console:log', data),
  warn: (data: string) => send('hyprism:console:warn', data),
  error: (data: string) => send('hyprism:console:error', data),
};

const _file = {
  browseFolder: (data: string) => invoke<string>('hyprism:file:browseFolder', data, 300000),
  browseJavaExecutable: () => invoke<string>('hyprism:file:browseJavaExecutable', {}, 300000),
  browseModFiles: () => invoke<string[]>('hyprism:file:browseModFiles', {}),
  exists: (data: string) => invoke<boolean>('hyprism:file:exists', data),
};

const _game = {
  launch: (data: LaunchGameRequest | null) => send('hyprism:game:launch', data),
  cancel: () => send('hyprism:game:cancel'),
  stop: () => invoke<boolean>('hyprism:game:stop', {}),
  instances: () => invoke<InstalledInstance[]>('hyprism:game:instances', {}),
  isRunning: () => invoke<boolean>('hyprism:game:isRunning', {}),
  versions: (data: GetVersionsRequest | null) => invoke<number[]>('hyprism:game:versions', data),
  versionsWithSources: (data: GetVersionsRequest | null) => invoke<VersionListResponse>('hyprism:game:versionsWithSources', data),
  onProgress: (cb: (data: ProgressUpdate) => void) => on<ProgressUpdate>('hyprism:game:progress', cb),
  onState: (cb: (data: GameState) => void) => on<GameState>('hyprism:game:state', cb),
  onError: (cb: (data: GameError) => void) => on<GameError>('hyprism:game:error', cb),
};

const _i18n = {
  current: () => invoke<string>('hyprism:i18n:current', {}),
  set: (data: string) => invoke<SetLanguageResult>('hyprism:i18n:set', data),
  languages: () => invoke<LanguageInfo[]>('hyprism:i18n:languages', {}),
};

const _instance = {
  create: (data: CreateInstanceRequest) => invoke<InstanceInfo | null>('hyprism:instance:create', data),
  delete: (data: InstanceIdRequest) => invoke<boolean>('hyprism:instance:delete', data),
  select: (data: SelectInstanceRequest) => invoke<boolean>('hyprism:instance:select', data),
  getSelected: () => invoke<InstanceInfo | null>('hyprism:instance:getSelected', {}),
  list: () => invoke<InstanceInfo[]>('hyprism:instance:list', {}),
  rename: (data: RenameInstanceRequest) => invoke<boolean>('hyprism:instance:rename', data),
  changeVersion: (data: ChangeVersionRequest) => invoke<boolean>('hyprism:instance:changeVersion', data),
  openFolder: (data: InstanceIdRequest) => send('hyprism:instance:openFolder', data),
  openModsFolder: (data: InstanceIdRequest) => send('hyprism:instance:openModsFolder', data),
  export: (data: InstanceIdRequest) => invoke<string>('hyprism:instance:export', data, 300000),
  import: () => invoke<boolean>('hyprism:instance:import', {}, 300000),
  saves: (data: InstanceIdRequest) => invoke<SaveInfo[]>('hyprism:instance:saves', data),
  openSaveFolder: (data: OpenSaveFolderRequest) => send('hyprism:instance:openSaveFolder', data),
  getIcon: (data: InstanceIdRequest) => invoke<string | null>('hyprism:instance:getIcon', data),
  setIcon: (data: SetIconRequest) => invoke<boolean>('hyprism:instance:setIcon', data),
};

const _logs = {
  get: (data: GetLogsRequest | null) => invoke<string[]>('hyprism:logs:get', data),
};

const _mods = {
  list: () => invoke<InstalledMod[]>('hyprism:mods:list', {}),
  search: (data: ModSearchRequest) => invoke<ModSearchResult>('hyprism:mods:search', data, 30000),
  installed: (data: ModInstalledRequest) => invoke<InstalledMod[]>('hyprism:mods:installed', data),
  uninstall: (data: ModUninstallRequest) => invoke<boolean>('hyprism:mods:uninstall', data),
  checkUpdates: (data: ModCheckUpdatesRequest) => invoke<InstalledMod[]>('hyprism:mods:checkUpdates', data, 30000),
  install: (data: ModInstallRequest) => invoke<boolean>('hyprism:mods:install', data, 300000),
  files: (data: ModFilesRequest) => invoke<ModFilesResult>('hyprism:mods:files', data),
  info: (data: ModInfoRequest) => invoke<ModInfo | null>('hyprism:mods:info', data, 30000),
  changelog: (data: ModChangelogRequest) => invoke<string>('hyprism:mods:changelog', data),
  categories: () => invoke<ModCategory[]>('hyprism:mods:categories', {}),
  installLocal: (data: ModInstallLocalRequest) => invoke<boolean>('hyprism:mods:installLocal', data),
  installBase64: (data: ModInstallBase64Request) => invoke<boolean>('hyprism:mods:installBase64', data),
  openFolder: (data: ModOpenFolderRequest) => send('hyprism:mods:openFolder', data),
  toggle: (data: ModToggleRequest) => invoke<boolean>('hyprism:mods:toggle', data),
  exportToFolder: (data: ModExportRequest) => invoke<string>('hyprism:mods:exportToFolder', data),
  importList: (data: ModImportListRequest) => invoke<number>('hyprism:mods:importList', data),
};

const _network = {
  pingAuthServer: (data: PingAuthServerRequest | null) => invoke<AuthServerPingResult>('hyprism:network:pingAuthServer', data),
};

const _news = {
  get: () => invoke<NewsItemResponse[]>('hyprism:news:get', {}),
};

const _profile = {
  get: () => invoke<ProfileSnapshot>('hyprism:profile:get', {}),
  list: () => invoke<IpcProfile[]>('hyprism:profile:list', {}),
  switch: (data: SwitchProfileRequest) => invoke<SuccessResult>('hyprism:profile:switch', data),
  setNick: (data: string) => invoke<SuccessResult>('hyprism:profile:setNick', data),
  setUuid: (data: string) => invoke<SuccessResult>('hyprism:profile:setUuid', data),
  create: (data: CreateProfileRequest) => invoke<IpcProfile | null>('hyprism:profile:create', data),
  delete: (data: string) => invoke<SuccessResult>('hyprism:profile:delete', data),
  activeIndex: () => invoke<number>('hyprism:profile:activeIndex', {}),
  save: () => invoke<SuccessResult>('hyprism:profile:save', {}),
  duplicate: (data: string) => invoke<IpcProfile | null>('hyprism:profile:duplicate', data),
  openFolder: () => send('hyprism:profile:openFolder'),
  avatarForUuid: (data: string) => invoke<string>('hyprism:profile:avatarForUuid', data),
};

const _settings = {
  get: () => invoke<SettingsSnapshot>('hyprism:settings:get', {}),
  update: (data: UpdateSettingsRequest) => invoke<SuccessResult>('hyprism:settings:update', data),
  testMirrorSpeed: (data: TestMirrorSpeedRequest) => invoke<MirrorSpeedTestResult>('hyprism:settings:testMirrorSpeed', data),
  testOfficialSpeed: (data: TestOfficialSpeedRequest | null) => invoke<MirrorSpeedTestResult>('hyprism:settings:testOfficialSpeed', data),
  hasDownloadSources: () => invoke<DownloadSourcesSummary>('hyprism:settings:hasDownloadSources', {}),
  getMirrors: () => invoke<MirrorInfo[]>('hyprism:settings:getMirrors', {}),
  addMirror: (data: AddMirrorRequest) => invoke<AddMirrorResult>('hyprism:settings:addMirror', data, 0),
  deleteMirror: (data: MirrorIdRequest) => invoke<SuccessResult>('hyprism:settings:deleteMirror', data),
  toggleMirror: (data: ToggleMirrorRequest) => invoke<SuccessResult>('hyprism:settings:toggleMirror', data),
  launcherPath: () => invoke<string>('hyprism:settings:launcherPath', {}),
  defaultInstanceDir: () => invoke<string>('hyprism:settings:defaultInstanceDir', {}),
  setInstanceDir: (data: string) => invoke<SetInstanceDirResult>('hyprism:settings:setInstanceDir', data, 300000),
};

const _system = {
  gpuAdapters: () => invoke<GpuAdapterInfo[]>('hyprism:system:gpuAdapters', {}),
  platform: () => invoke<PlatformInfo>('hyprism:system:platform', {}),
};

const _update = {
  check: () => invoke<SuccessResult>('hyprism:update:check', {}),
  install: () => invoke<boolean>('hyprism:update:install', {}, 300000),
  onAvailable: (cb: (data: LauncherUpdateInfo) => void) => on<LauncherUpdateInfo>('hyprism:update:available', cb),
  onProgress: (cb: (data: LauncherUpdateProgress) => void) => on<LauncherUpdateProgress>('hyprism:update:progress', cb),
};

const _windowCtl = {
  minimize: () => send('hyprism:window:minimize'),
  maximize: () => send('hyprism:window:maximize'),
  close: () => send('hyprism:window:close'),
  restart: () => send('hyprism:window:restart'),
};

export const ipc = {
  auth: _auth,
  browser: _browser,
  config: _config,
  consoleCtl: _consoleCtl,
  file: _file,
  game: _game,
  i18n: _i18n,
  instance: _instance,
  logs: _logs,
  mods: _mods,
  network: _network,
  news: _news,
  profile: _profile,
  settings: _settings,
  system: _system,
  update: _update,
  windowCtl: _windowCtl,
};

// Type aliases (C# model names → frontend-expected names)
export type Profile = IpcProfile;
export type NewsItem = NewsItemResponse;
export type ModScreenshot = CurseForgeScreenshot;
