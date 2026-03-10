import React from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import {
  Box,
  ChevronRight,
  Edit2,
  FolderOpen,
  HardDrive,
  Loader2,
  Package,
  Plus,
  RefreshCw,
  Trash2,
  Upload,
} from 'lucide-react';
import { Button, IconButton, MenuItemButton } from '@/components/ui/Controls';
import type { InstalledVersionInfo } from '@/types';

export type ValidationInfo = {
  status: 'valid' | 'warning' | 'error';
  label: string;
  color: string;
  bgColor: string;
  icon: React.ReactNode;
};

export function InstancesSidebar({
  title,
  accentColor,
  instances,
  isLoading,
  instanceDir,
  selectedInstanceId,
  onCreate,
  onImport,
  importDisabled,
  onRefresh,
  refreshDisabled,
  onSelectInstance,
  onContextMenuInstance,
  inlineMenuInstanceId,
  inlineMenuRef,
  exportingInstanceId,
  exportDisabled,
  onEdit,
  onOpenFolder,
  onOpenModsFolder,
  onExport,
  onDelete,
  getDisplayName,
  getIcon,
  getValidationInfo,
  formatSize,
  tCommonUnknown,
  tAddInstance,
  tNoInstances,
  tImport,
  tOpenModsFolder,
  tCommonRefresh,
  tCommonEdit,
  tCommonOpenFolder,
  tCommonExport,
  tCommonDelete,
}: {
  title: string;
  accentColor: string;
  instances: InstalledVersionInfo[];
  isLoading: boolean;
  instanceDir: string;
  selectedInstanceId: string | null;
  onCreate: () => void;
  onImport: () => void;
  importDisabled: boolean;
  onRefresh: () => void;
  refreshDisabled: boolean;
  onSelectInstance: (inst: InstalledVersionInfo) => void;
  onContextMenuInstance: (inst: InstalledVersionInfo) => void;
  inlineMenuInstanceId: string | null;
  inlineMenuRef: React.RefObject<HTMLDivElement>;
  exportingInstanceId: string | null;
  exportDisabled: boolean;
  onEdit: (inst: InstalledVersionInfo) => void;
  onOpenFolder: (inst: InstalledVersionInfo) => void;
  onOpenModsFolder: (inst: InstalledVersionInfo) => void;
  onExport: (inst: InstalledVersionInfo) => void;
  onDelete: (inst: InstalledVersionInfo) => void;
  getDisplayName: (inst: InstalledVersionInfo) => string;
  getIcon: (inst: InstalledVersionInfo) => React.ReactNode;
  getValidationInfo: (inst: InstalledVersionInfo) => ValidationInfo;
  formatSize: (bytes: number) => string;
  tCommonUnknown: string;
  tAddInstance: string;
  tNoInstances: string;
  tImport: string;
  tOpenModsFolder: string;
  tCommonRefresh: string;
  tCommonEdit: string;
  tCommonOpenFolder: string;
  tCommonExport: string;
  tCommonDelete: string;
}) {
  return (
    <div className="w-72 flex-shrink-0 flex flex-col">
      <div className="flex items-center justify-between mb-3 px-3">
        <div className="flex items-center gap-2">
          <HardDrive size={18} className="text-white opacity-70" />
          <h2 className="text-sm font-semibold text-white">{title}</h2>
        </div>

        <div className="flex items-center gap-1">
          <IconButton title={tAddInstance} onClick={onCreate} className="h-7 w-7 rounded-lg">
            <Plus size={14} />
          </IconButton>
          <IconButton title={tImport} onClick={onImport} disabled={importDisabled} className="h-7 w-7 rounded-lg">
            {importDisabled ? <Loader2 size={14} className="animate-spin" /> : <Upload size={14} />}
          </IconButton>
          <IconButton title={tCommonRefresh} onClick={onRefresh} disabled={refreshDisabled} className="h-7 w-7 rounded-lg">
            <RefreshCw size={14} className={refreshDisabled ? 'animate-spin' : ''} />
          </IconButton>
        </div>
      </div>

      <div className="flex-1 flex flex-col overflow-hidden rounded-2xl glass-panel-static-solid min-h-0">
        <div className="flex-1 overflow-y-auto">
          <div className="p-2 space-y-1">
            {isLoading ? (
              <div className="flex items-center justify-center py-8">
                <Loader2 size={24} className="animate-spin" style={{ color: accentColor }} />
              </div>
            ) : instances.length === 0 ? (
              <div className="flex flex-col items-center justify-center py-8 text-white/40">
                <Box size={32} className="mb-2 opacity-50" />
                <p className="text-xs text-center mb-3">{tNoInstances}</p>
                <Button size="sm" onClick={onCreate}>
                  <Plus size={14} />
                  {tAddInstance}
                </Button>
              </div>
            ) : (
              instances.map((inst) => {
                const isSelected = selectedInstanceId === inst.id;
                const validation = getValidationInfo(inst);

                return (
                  <div key={inst.id} className="relative">
                    <button
                      type="button"
                      onClick={() => onSelectInstance(inst)}
                      onContextMenu={(e) => {
                        e.preventDefault();
                        onContextMenuInstance(inst);
                      }}
                      className={`w-full p-3 rounded-xl flex items-center gap-3 text-left transition-all duration-150 ${
                        isSelected ? 'shadow-md' : 'hover:bg-white/[0.04]'
                      }`}
                      style={
                        isSelected
                          ? {
                              backgroundColor: `${accentColor}18`,
                              boxShadow: `0 0 0 1px ${accentColor}40`,
                            }
                          : undefined
                      }
                    >
                      <div
                        className="w-11 h-11 rounded-xl flex items-center justify-center flex-shrink-0 border border-white/[0.08]"
                        style={{ backgroundColor: isSelected ? `${accentColor}25` : 'rgba(255,255,255,0.06)' }}
                      >
                        {getIcon(inst)}
                      </div>

                      <div className="flex-1 min-w-0">
                        <p
                          className="text-white text-sm font-medium leading-tight overflow-hidden whitespace-nowrap"
                          title={getDisplayName(inst)}
                          style={{
                            maskImage: 'linear-gradient(to right, black 85%, transparent 100%)',
                            WebkitMaskImage: 'linear-gradient(to right, black 85%, transparent 100%)',
                          }}
                        >
                          {getDisplayName(inst)}
                        </p>
                        <div className="flex items-center gap-2 mt-0.5">
                          <span className="text-white/40 text-xs">
                            {inst.sizeBytes ? formatSize(inst.sizeBytes) : tCommonUnknown}
                          </span>
                          <span
                            className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded text-[10px] font-medium"
                            style={{ backgroundColor: validation.bgColor, color: validation.color }}
                            title={inst.validationDetails?.errorMessage || validation.label}
                          >
                            {validation.icon}
                            {validation.status !== 'valid' && validation.label}
                          </span>
                        </div>
                      </div>

                      {isSelected ? <ChevronRight size={14} style={{ color: accentColor }} /> : null}
                    </button>

                    <AnimatePresence>
                      {inlineMenuInstanceId === inst.id && (
                        <motion.div
                          ref={inlineMenuRef}
                          initial={{ opacity: 0, y: -8, scale: 0.96 }}
                          animate={{ opacity: 1, y: 0, scale: 1 }}
                          exit={{ opacity: 0, y: -8, scale: 0.96 }}
                          transition={{ duration: 0.15, ease: [0.4, 0, 0.2, 1] }}
                          className="absolute left-2 right-2 top-full mt-1 bg-[#1c1c1e] border border-white/[0.08] rounded-xl shadow-xl overflow-hidden z-40"
                        >
                          <MenuItemButton onClick={() => onEdit(inst)}>
                            <Edit2 size={14} />
                            {tCommonEdit}
                          </MenuItemButton>
                          <MenuItemButton onClick={() => onOpenFolder(inst)}>
                            <FolderOpen size={14} />
                            {tCommonOpenFolder}
                          </MenuItemButton>
                          <MenuItemButton onClick={() => onOpenModsFolder(inst)}>
                            <Package size={14} />
                            {tOpenModsFolder}
                          </MenuItemButton>
                          <MenuItemButton
                            onClick={() => onExport(inst)}
                            disabled={exportDisabled}
                          >
                            {exportingInstanceId === inst.id ? (
                              <Loader2 size={14} className="animate-spin" />
                            ) : (
                              <Upload size={14} />
                            )}
                            {tCommonExport}
                          </MenuItemButton>
                          <div className="border-t border-white/10 my-1" />
                          <MenuItemButton variant="danger" onClick={() => onDelete(inst)}>
                            <Trash2 size={14} />
                            {tCommonDelete}
                          </MenuItemButton>
                        </motion.div>
                      )}
                    </AnimatePresence>
                  </div>
                );
              })
            )}
          </div>
        </div>

        {instanceDir ? (
          <div className="px-3 py-2 border-t border-white/[0.06] text-xs text-white/40 truncate flex-shrink-0">
            {instanceDir}
          </div>
        ) : null}
      </div>
    </div>
  );
}
