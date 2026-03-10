import { useMemo, useState } from 'react';
import {
  Bug,
  Coffee,
  Copy,
  Download,
  Edit,
  ExternalLink,
  FolderOpen,
  Globe,
  Play,
  Power,
  RefreshCw,
  Save,
  Server,
  Settings,
  Trash2,
  Upload,
  Wifi,
  X,
} from 'lucide-react';
import {
  AccentSegmentedControl,
  Button,
  DropdownTriggerButton,
  IconButton,
  LauncherActionButton,
  LinkButton,
  MenuActionButton,
  MenuItemButton,
  MirrorSpeedCard,
  ModalFooterActions,
  RadioOptionCard,
  ScrollArea,
  SegmentedControl,
  SelectionMark,
  SettingsToggleCard,
  Switch,
} from '@/components/ui/Controls';
import { SelectionCard } from '@/components/ui/SelectionCard';

type CatalogTab = 'launcher' | 'buttons' | 'tabs' | 'switches' | 'menus' | 'cards' | 'settings' | 'layout';

export function DeveloperUiCatalog() {
  const [tab, setTab] = useState<CatalogTab>('launcher');
  const [switchOn, setSwitchOn] = useState(true);
  const [selected, setSelected] = useState(true);
  const [radioValue, setRadioValue] = useState<'a' | 'b' | 'c'>('a');
  const [segValue, setSegValue] = useState<'one' | 'two' | 'three'>('one');

  const tabs = useMemo(
    () =>
      [
        { value: 'launcher' as const, label: 'Launcher' },
        { value: 'buttons' as const, label: 'Buttons' },
        { value: 'tabs' as const, label: 'Tabs' },
        { value: 'switches' as const, label: 'Switches' },
        { value: 'menus' as const, label: 'Menus' },
        { value: 'cards' as const, label: 'Cards' },
        { value: 'settings' as const, label: 'Settings' },
        { value: 'layout' as const, label: 'Layout' },
      ],
    []
  );

  return (
    <div className="space-y-4">
      <div className="glass-panel-static-solid rounded-2xl p-4">
        <div className="flex items-center justify-between">
          <div className="text-sm font-semibold text-white/80">UI Catalog</div>
          <div className="text-[10px] text-white/40">Developer tab</div>
        </div>

        <div className="mt-3">
          <AccentSegmentedControl<CatalogTab> value={tab} onChange={setTab} items={tabs} />
        </div>
      </div>

      {tab === 'launcher' ? (
        <div className="glass-panel-static-solid rounded-2xl p-4">
          <div className="text-xs font-semibold text-white/60">Launcher button samples</div>
          <div className="mt-3 flex flex-wrap items-center gap-2">
            <LauncherActionButton variant="play" className="h-10 px-5 text-sm">
              <Play className="w-4 h-4" /> Play
            </LauncherActionButton>
            <LauncherActionButton variant="download" className="h-10 px-5 text-sm">
              <Download className="w-4 h-4" /> Download
            </LauncherActionButton>
            <LauncherActionButton variant="update" className="h-10 px-5 text-sm">
              <RefreshCw className="w-4 h-4" /> Update
            </LauncherActionButton>
            <LauncherActionButton variant="stop" className="h-10 px-5 text-sm">
              <X className="w-4 h-4" /> Stop
            </LauncherActionButton>

            <Button size="sm">
              <FolderOpen className="w-4 h-4" /> Open folder
            </Button>
            <Button size="sm">
              <Edit className="w-4 h-4" /> Edit
            </Button>
            <Button size="sm">
              <Save className="w-4 h-4" /> Save
            </Button>
            <Button size="sm">
              <Upload className="w-4 h-4" /> Import
            </Button>
            <Button size="sm">
              <ExternalLink className="w-4 h-4" /> Open link
            </Button>
          </div>

          <div className="mt-4 text-xs font-semibold text-white/60">Icon-only actions</div>
          <div className="mt-2 flex flex-wrap items-center gap-2">
            <IconButton title="Refresh">
              <RefreshCw className="w-4 h-4" />
            </IconButton>
            <IconButton title="Copy">
              <Copy className="w-4 h-4" />
            </IconButton>
            <IconButton title="Folder">
              <FolderOpen className="w-4 h-4" />
            </IconButton>
          </div>
        </div>
      ) : null}

      {tab === 'buttons' ? (
        <div className="glass-panel-static-solid rounded-2xl p-4">
          <div className="text-xs font-semibold text-white/60">Primitives</div>
          <div className="mt-2 text-[11px] text-white/40">Button • IconButton</div>

          <div className="mt-3 flex flex-wrap items-center gap-2">
            <Button size="sm">Default</Button>
            <Button size="sm" variant="danger">Delete</Button>
            <IconButton title="IconButton">
              <Bug className="w-4 h-4" />
            </IconButton>
          </div>
        </div>
      ) : null}

      {tab === 'tabs' ? (
        <div className="glass-panel-static-solid rounded-2xl p-4">
          <div className="text-xs font-semibold text-white/60">Primitive</div>
          <div className="mt-2 text-[11px] text-white/40">AccentSegmentedControl</div>

          <div className="mt-3">
            <AccentSegmentedControl
              value={selected ? 'on' : 'off'}
              onChange={(v) => setSelected(v === 'on')}
              items={[
                { value: 'on' as const, label: 'On' },
                { value: 'off' as const, label: 'Off' },
                { value: 'disabled' as const, label: 'Disabled', disabled: true, title: 'Disabled example' },
              ]}
            />
          </div>
        </div>
      ) : null}

      {tab === 'switches' ? (
        <div className="glass-panel-static-solid rounded-2xl p-4">
          <div className="text-xs font-semibold text-white/60">Primitives</div>
          <div className="mt-2 text-[11px] text-white/40">Switch • SelectionMark</div>

          <div className="mt-3 flex flex-wrap items-center gap-4">
            <div className="flex items-center gap-3">
              <div className="text-xs text-white/50">Switch</div>
              <Switch checked={switchOn} onCheckedChange={setSwitchOn} />
            </div>

            <div className="flex items-center gap-3">
              <div className="text-xs text-white/50">SelectionMark</div>
              <SelectionMark selected={selected} className="border-white/50 text-white/80" />
              <Button size="sm" onClick={() => setSelected((s) => !s)}>
                Toggle
              </Button>
            </div>
          </div>
        </div>
      ) : null}

      {tab === 'menus' ? (
        <div className="glass-panel-static-solid rounded-2xl p-4">
          <div className="text-xs font-semibold text-white/60">Primitive</div>
          <div className="mt-2 text-[11px] text-white/40">MenuActionButton</div>

          <div className="mt-3 w-[260px] rounded-2xl overflow-hidden border border-white/15 bg-[#0a0a0a]/35">
            <MenuActionButton>
              <FolderOpen className="w-4 h-4" /> Open Folder
            </MenuActionButton>
            <div className="h-px bg-white/10" />
            <MenuActionButton>
              <Trash2 className="w-4 h-4" /> Delete
            </MenuActionButton>
          </div>
        </div>
      ) : null}

      {tab === 'cards' ? (
        <div className="glass-panel-static-solid rounded-2xl p-4">
          <div className="text-xs font-semibold text-white/60">Primitive</div>
          <div className="mt-2 text-[11px] text-white/40">SelectionCard</div>

          <div className="mt-3 grid grid-cols-2 gap-3">
            <SelectionCard
              icon={<Play className="w-5 h-5" />}
              title="SelectionCard"
              description="Selectable option card"
              selected
              onClick={() => {}}
            />
            <SelectionCard
              icon={<Trash2 className="w-5 h-5" />}
              title="Danger"
              description="Danger variant"
              variant="danger"
              onClick={() => {}}
            />
          </div>
        </div>
      ) : null}

      {tab === 'settings' ? (
        <>
          <div className="glass-panel-static-solid rounded-2xl p-4">
            <div className="text-xs font-semibold text-white/60">Primitive</div>
            <div className="mt-2 text-[11px] text-white/40">SettingsToggleCard</div>

            <div className="mt-3 space-y-2">
              <SettingsToggleCard
                icon={<Power size={16} className="text-white opacity-70" />}
                title="Toggle setting"
                description="Description of the setting"
                checked={switchOn}
                onCheckedChange={setSwitchOn}
              />
              <SettingsToggleCard
                icon={<Wifi size={16} className="text-white opacity-70" />}
                title="With badge"
                description="A toggle card with a badge"
                badge={
                  <span className="px-1.5 py-0.5 rounded text-[10px] font-semibold tracking-wider uppercase"
                    style={{ backgroundColor: '#ef444420', color: '#ef4444', border: '1px solid #ef444430' }}>
                    BETA
                  </span>
                }
                checked={!switchOn}
                onCheckedChange={(v) => setSwitchOn(!v)}
              />
              <SettingsToggleCard
                icon={<Settings size={16} className="text-white opacity-70" />}
                title="Disabled"
                description="This setting is disabled"
                checked={false}
                onCheckedChange={() => {}}
                disabled
              />
            </div>
          </div>

          <div className="glass-panel-static-solid rounded-2xl p-4">
            <div className="text-xs font-semibold text-white/60">Primitive</div>
            <div className="mt-2 text-[11px] text-white/40">RadioOptionCard</div>

            <div className="mt-3 space-y-2">
              <RadioOptionCard
                icon={<Coffee size={16} />}
                title="Option A"
                description="First option"
                selected={radioValue === 'a'}
                onClick={() => setRadioValue('a')}
              />
              <RadioOptionCard
                icon={<Globe size={16} />}
                title="Option B"
                description="Second option"
                selected={radioValue === 'b'}
                onClick={() => setRadioValue('b')}
              />
              <RadioOptionCard
                icon={<Server size={16} />}
                title="Option C (expandable)"
                description="Has child content when selected"
                selected={radioValue === 'c'}
                onClick={() => setRadioValue('c')}
              >
                <div className="text-xs text-white/50 p-2 rounded-lg bg-white/5">
                  Expanded content shown when selected
                </div>
              </RadioOptionCard>
            </div>
          </div>

          <div className="glass-panel-static-solid rounded-2xl p-4">
            <div className="text-xs font-semibold text-white/60">Primitive</div>
            <div className="mt-2 text-[11px] text-white/40">MirrorSpeedCard</div>

            <div className="mt-3">
              <MirrorSpeedCard
                name="Example Mirror"
                description="Community mirror"
                hostname="mirror.example.com"
                speedTest={null}
                isTesting={false}
                onTest={() => {}}
                testLabel="Test speed"
                testingLabel="Testing..."
                unavailableLabel="Unavailable"
              />
            </div>
          </div>
        </>
      ) : null}

      {tab === 'layout' ? (
        <>
          <div className="glass-panel-static-solid rounded-2xl p-4">
            <div className="text-xs font-semibold text-white/60">Primitives</div>
            <div className="mt-2 text-[11px] text-white/40">
              LinkButton • DropdownTriggerButton • MenuItemButton
            </div>

            <div className="mt-3 flex flex-wrap items-center gap-3">
              <LinkButton onClick={() => {}}>
                <ExternalLink size={12} /> Link button
              </LinkButton>

              <DropdownTriggerButton label="Dropdown" open={false} onClick={() => {}} />
              <DropdownTriggerButton label="Open" open onClick={() => {}} />
            </div>

            <div className="mt-3 w-[260px] rounded-2xl overflow-hidden border border-white/15 bg-[#0a0a0a]/35">
              <MenuItemButton onClick={() => {}}>
                <FolderOpen className="w-4 h-4" /> Menu item (default)
              </MenuItemButton>
              <div className="h-px bg-white/10" />
              <MenuItemButton variant="danger" onClick={() => {}}>
                <Trash2 className="w-4 h-4" /> Menu item (danger)
              </MenuItemButton>
            </div>
          </div>

          <div className="glass-panel-static-solid rounded-2xl p-4">
            <div className="text-xs font-semibold text-white/60">Primitives</div>
            <div className="mt-2 text-[11px] text-white/40">SegmentedControl • ScrollArea • ModalFooterActions</div>

            <div className="mt-3">
              <SegmentedControl
                value={segValue}
                onChange={setSegValue}
                items={[
                  { value: 'one' as const, label: 'One' },
                  { value: 'two' as const, label: 'Two' },
                  { value: 'three' as const, label: 'Three' },
                ]}
              />
            </div>

            <div className="mt-3">
              <ScrollArea axis="y" thin className="h-24 rounded-lg border border-white/10 p-2">
                {Array.from({ length: 12 }, (_, i) => (
                  <div key={i} className="text-xs text-white/50 py-1">
                    ScrollArea item {i + 1}
                  </div>
                ))}
              </ScrollArea>
            </div>

            <div className="mt-3 rounded-xl overflow-hidden border border-white/10">
              <ModalFooterActions>
                <Button size="sm">Cancel</Button>
                <Button size="sm" variant="primary">Confirm</Button>
              </ModalFooterActions>
            </div>
          </div>
        </>
      ) : null}
    </div>
  );
}
