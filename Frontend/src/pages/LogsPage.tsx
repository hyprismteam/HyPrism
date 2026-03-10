import React, { useState, useEffect, useCallback, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { RefreshCw, Copy, Check, Download, Search } from 'lucide-react';
import { useAccentColor } from '../contexts/AccentColorContext';
import { invoke } from '@/lib/ipc';
import { PageContainer } from '@/components/ui/PageContainer';
import { SettingsHeader } from '@/components/ui/SettingsHeader';
import { AccentSegmentedControl, Button, IconButton } from '@/components/ui/Controls';

type LogLevel = 'all' | 'INF' | 'SUC' | 'WRN' | 'ERR' | 'DBG';

interface LogEntry {
  timestamp: string;
  level: string;
  category: string;
  message: string;
  raw: string;
}

const parseLogEntry = (line: string): LogEntry => {
  // Format: "HH:mm:ss | LVL | Category | Message"
  const parts = line.split(' | ');
  if (parts.length >= 4) {
    return {
      timestamp: parts[0],
      level: parts[1],
      category: parts[2],
      message: parts.slice(3).join(' | '),
      raw: line,
    };
  }
  return {
    timestamp: '',
    level: 'INF',
    category: 'Unknown',
    message: line,
    raw: line,
  };
};

const getLevelColor = (level: string): string => {
  switch (level) {
    case 'ERR': return 'text-red-400';
    case 'WRN': return 'text-yellow-400';
    case 'SUC': return 'text-green-400';
    case 'DBG': return 'text-gray-500';
    case 'INF':
    default: return 'text-gray-300';
  }
};

interface LogsPageProps {
  embedded?: boolean;
}

export const LogsPage: React.FC<LogsPageProps> = ({ embedded = false }) => {
  const { t } = useTranslation();
  const { accentColor } = useAccentColor();
  const [logs, setLogs] = useState<LogEntry[]>([]);
  const [loading, setLoading] = useState(true);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [filter, setFilter] = useState<LogLevel>('all');
  const [searchQuery, setSearchQuery] = useState('');
  const [copied, setCopied] = useState(false);
  const [autoRefresh, setAutoRefresh] = useState(true);
  const [selectedIndices, setSelectedIndices] = useState<Set<number>>(new Set());
  const [isDragging, setIsDragging] = useState(false);
  const scrollRef = useRef<HTMLDivElement>(null);
  const autoScrollRef = useRef(true);

  const fetchLogs = useCallback(async () => {
    if (logs.length === 0) setLoading(true);
    else setIsRefreshing(true);
    try {
      const rawLogs = await invoke<string[]>('hyprism:logs:get', { count: 100 });
      const parsed = (rawLogs || []).map(parseLogEntry);
      setLogs(parsed);
    } catch (err) {
      console.error('Failed to fetch logs:', err);
    } finally {
      setLoading(false);
      setIsRefreshing(false);
    }
  }, [logs.length]);

  // Initial fetch
  useEffect(() => {
    autoScrollRef.current = true;
    fetchLogs();
  }, []);

  // Auto-refresh every 2 seconds
  useEffect(() => {
    if (!autoRefresh) return;
    const interval = setInterval(fetchLogs, 2000);
    return () => clearInterval(interval);
  }, [autoRefresh, fetchLogs]);

  // Auto-scroll to bottom when new logs arrive
  useEffect(() => {
    if (autoScrollRef.current && scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [logs]);

  // Detect manual scroll to disable auto-scroll
  const handleScroll = useCallback(() => {
    if (!scrollRef.current) return;
    const { scrollTop, scrollHeight, clientHeight } = scrollRef.current;
    autoScrollRef.current = scrollHeight - scrollTop - clientHeight < 50;
  }, []);

  const filteredLogs = logs.filter(log => {
    const matchesLevel = filter === 'all' || log.level === filter;
    const matchesSearch = searchQuery === '' || 
      log.message.toLowerCase().includes(searchQuery.toLowerCase()) ||
      log.category.toLowerCase().includes(searchQuery.toLowerCase());
    return matchesLevel && matchesSearch;
  });

  // Handle mouse down on log entry - start selection
  const handleLogMouseDown = useCallback((index: number, event: React.MouseEvent) => {
    event.preventDefault(); // Prevent text selection
    
    if (event.shiftKey && selectedIndices.size > 0) {
      // Shift-click: select range
      const lastSelected = Math.max(...selectedIndices);
      const start = Math.min(lastSelected, index);
      const end = Math.max(lastSelected, index);
      const newSelection = new Set(selectedIndices);
      for (let i = start; i <= end; i++) {
        newSelection.add(i);
      }
      setSelectedIndices(newSelection);
    } else if (event.ctrlKey || event.metaKey) {
      // Ctrl/Cmd-click: toggle individual selection
      const newSelection = new Set(selectedIndices);
      if (newSelection.has(index)) {
        newSelection.delete(index);
      } else {
        newSelection.add(index);
      }
      setSelectedIndices(newSelection);
    } else {
      // Regular click: toggle if same single selection, otherwise select this one
      if (selectedIndices.size === 1 && selectedIndices.has(index)) {
        setSelectedIndices(new Set());
      } else {
        setSelectedIndices(new Set([index]));
        setIsDragging(true);
      }
    }
  }, [selectedIndices]);

  // Handle mouse enter during drag
  const handleLogMouseEnter = useCallback((index: number) => {
    if (isDragging) {
      setSelectedIndices(prev => new Set([...prev, index]));
    }
  }, [isDragging]);

  // Handle mouse up - stop dragging
  useEffect(() => {
    const handleMouseUp = () => setIsDragging(false);
    window.addEventListener('mouseup', handleMouseUp);
    return () => window.removeEventListener('mouseup', handleMouseUp);
  }, []);

  const handleCopy = useCallback(async () => {
    let text: string;
    if (selectedIndices.size > 0) {
      // Copy only selected logs
      const sortedIndices = Array.from(selectedIndices).sort((a, b) => a - b);
      text = sortedIndices.map(i => filteredLogs[i]?.raw).filter(Boolean).join('\n');
      setSelectedIndices(new Set()); // Clear selection after copy
    } else {
      // Copy all filtered logs
      text = filteredLogs.map(l => l.raw).join('\n');
    }
    try {
      await navigator.clipboard.writeText(text);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch (err) {
      console.error('Failed to copy logs:', err);
    }
  }, [filteredLogs, selectedIndices]);

  const handleExport = useCallback(() => {
    const text = filteredLogs.map(l => l.raw).join('\n');
    const blob = new Blob([text], { type: 'text/plain' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `hyprism-logs-${new Date().toISOString().slice(0, 10)}.txt`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  }, [filteredLogs]);

  const levelFilters: { value: LogLevel; label: string; color: string }[] = [
    { value: 'all', label: t('logs.filter.all'), color: 'text-white/70' },
    { value: 'INF', label: t('logs.filter.info'), color: 'text-gray-300' },
    { value: 'SUC', label: t('logs.filter.success'), color: 'text-green-400' },
    { value: 'WRN', label: t('logs.filter.warning'), color: 'text-yellow-400' },
    { value: 'ERR', label: t('logs.filter.error'), color: 'text-red-400' },
    { value: 'DBG', label: t('logs.filter.debug'), color: 'text-gray-500' },
  ];

  const headerActions = (
    <>
      <Button
        size="sm"
        onClick={() => setAutoRefresh(!autoRefresh)}
        className="h-10"
        style={
          autoRefresh
            ? { backgroundColor: `${accentColor}20`, borderColor: `${accentColor}40`, color: accentColor }
            : undefined
        }
      >
        {t('logs.auto')}
      </Button>

      <IconButton onClick={fetchLogs} disabled={isRefreshing} title={t('logs.refresh')}>
        <RefreshCw size={16} className={isRefreshing ? 'animate-spin' : ''} />
      </IconButton>

      <IconButton onClick={handleCopy} title={selectedIndices.size > 0 ? t('logs.copySelected') : t('logs.copy')}>
        {copied ? <Check size={16} style={{ color: accentColor }} /> : <Copy size={16} />}
      </IconButton>

      <IconButton onClick={handleExport} title={t('logs.export')}>
        <Download size={16} />
      </IconButton>
    </>
  );


  const content = (
    <div className="h-full flex flex-col">
      <div className={embedded ? 'p-4 border-b border-white/[0.06] flex-shrink-0' : 'flex-shrink-0 mb-4'}>
        <SettingsHeader title={t('logs.title')} actions={headerActions} size="sm" />
      </div>

      <div className="flex-1 min-h-0 overflow-hidden flex flex-col">
        <div className="p-4 flex-shrink-0">
          <div className="flex items-center gap-3">
            <div className="relative flex-1 max-w-sm">
              <Search size={16} className="absolute left-3 top-1/2 -translate-y-1/2 text-white opacity-40" />
              <input
                type="text"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                placeholder={t('logs.searchPlaceholder')}
                className="w-full h-10 pl-10 pr-4 rounded-xl bg-[#2c2c2e] border border-white/[0.08] text-white text-sm placeholder-white/40 focus:outline-none focus:border-white/20"
              />
            </div>

            <AccentSegmentedControl
              value={filter}
              onChange={setFilter}
              items={levelFilters.map((f) => ({
                value: f.value,
                label: f.label,
                className: f.color,
              }))}
            />
          </div>

          <div className="mt-2 text-xs text-white/40 flex items-center gap-2">
            <span>
              {t('logs.showing', {
                count: filteredLogs.length,
                total: logs.length,
              })}
            </span>
            {selectedIndices.size > 0 ? (
              <span className="text-white/60">• {t('logs.selected', { count: selectedIndices.size })}</span>
            ) : null}
          </div>
        </div>

        <div className="flex-1 min-h-0 mx-4 mb-4 rounded-xl bg-white/[0.03] border border-white/[0.06] overflow-hidden">
          <div ref={scrollRef} onScroll={handleScroll} className="h-full overflow-auto font-mono text-xs">
            {loading && logs.length === 0 ? (
              <div className="flex items-center justify-center h-full text-white/40">
                <RefreshCw size={20} className="animate-spin mr-2" />
                {t('logs.loading')}
              </div>
            ) : filteredLogs.length === 0 ? (
              <div className="flex items-center justify-center h-full text-white/40">
                {searchQuery || filter !== 'all' ? t('logs.noResults') : t('logs.empty')}
              </div>
            ) : (
              <div className="p-2 space-y-0.5 select-none min-w-max">
                {filteredLogs.map((log, i) => (
                  <div
                    key={i}
                    onMouseDown={(e) => handleLogMouseDown(i, e)}
                    onMouseEnter={() => handleLogMouseEnter(i)}
                    className={`flex items-start gap-2 px-2 py-1 rounded border cursor-pointer transition-colors ${
                      selectedIndices.has(i)
                        ? 'bg-white/15 border-white/20'
                        : 'border-transparent hover:bg-white/5'
                    }`}
                  >
                    <span className="text-white/30 shrink-0 w-16">{log.timestamp}</span>
                    <span className={`shrink-0 w-8 font-semibold ${getLevelColor(log.level)}`}>{log.level}</span>
                    <span className="shrink-0 w-24 truncate" style={{ color: accentColor }}>
                      {log.category}
                    </span>
                    <span className="text-white/80 whitespace-pre">{log.message}</span>
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>

      </div>
    </div>
  );

  if (embedded) {
    return <div className="h-full">{content}</div>;
  }

  return (
    <PageContainer contentClassName="h-full">
      {content}
    </PageContainer>
  );
};
