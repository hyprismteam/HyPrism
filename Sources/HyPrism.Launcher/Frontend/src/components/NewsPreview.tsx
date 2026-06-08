import { Calendar, RefreshCw, User, ChevronDown, ChevronUp, Newspaper } from 'lucide-react';
import React, { useState, useEffect, useRef, memo, useCallback, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { ipc } from '@/lib/ipc';
import { useAccentColor } from '../contexts/AccentColorContext';
import { Button, IconButton, LinkButton } from '@/components/ui/Controls';

interface NewsItem {
    title: string;
    excerpt?: string;
    url?: string;
    date?: string;
    author?: string;
    imageUrl?: string;
    source?: 'hytale' | 'hyprism';
}

interface NewsPreviewProps {
    getNews: (count: number) => Promise<NewsItem[]>
    isPaused?: boolean
}

type NewsFilter = 'all' | 'hytale' | 'hyprism';

export const NewsPreview: React.FC<NewsPreviewProps> = memo(({ getNews, isPaused = false }) => {
    const { t } = useTranslation();
    const { accentColor, accentTextColor } = useAccentColor();
    const [news, setNews] = useState<NewsItem[]>([]);
    const [initialLoading, setInitialLoading] = useState(true);
    const [isRefreshing, setIsRefreshing] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [limit, setLimit] = useState(6);
    const [filter, setFilter] = useState<NewsFilter>('all');
    const [isMinimized, setIsMinimized] = useState(false);
    const listRef = useRef<HTMLDivElement>(null);
    const containerRef = useRef<HTMLDivElement>(null);

    const fetchNews = useCallback(async (count: number, reset = false) => {
        // Only show initial loading spinner if we have no news yet
        if (news.length === 0) {
            setInitialLoading(true);
        } else {
            setIsRefreshing(true);
        }
        setError(null);
        try {
            const items = await getNews(count);
            setNews((prev) => {
                if (reset) return items;
                const seen = new Map<string, NewsItem>();
                prev.forEach((item) => seen.set(item.url + item.title, item));
                (items || []).forEach((item) => seen.set(item.url + item.title, item));
                return Array.from(seen.values());
            });
        } catch (err) {
            // Only set error if we have no news to show
            if (news.length === 0) {
                setError(err instanceof Error ? err.message : 'Failed to fetch news');
            }
        } finally {
            setInitialLoading(false);
            setIsRefreshing(false);
        }
    }, [getNews, news.length]);

    useEffect(() => {
        fetchNews(limit, limit === 6 && news.length === 0);
    }, [limit, fetchNews]);

    useEffect(() => {
        const interval = setInterval(() => {
            if (!isPaused) {
                fetchNews(limit);
            }
        }, 1800000); // 30 minutes
        return () => clearInterval(interval);
    }, [limit, fetchNews, isPaused]);

    const openLink = useCallback((url: string) => {
        ipc.browser.open(url);
    }, []);

    const handleScroll = useCallback(() => {
        // Avoid pagination while loading
        if (!listRef.current || initialLoading || isRefreshing) return;
        const { scrollTop, scrollHeight, clientHeight } = listRef.current;
        if (scrollHeight - scrollTop - clientHeight < 120) {
            setLimit((prev) => prev + 6);
        }
    }, [initialLoading, isRefreshing]);

    // Memoize filtered news to avoid recalculating on every render
    const filteredNews = useMemo(() => 
        filter === 'all' 
            ? news 
            : news.filter(item => item.source === filter),
        [filter, news]
    );

    return (
        <div ref={containerRef} className='flex flex-col gap-y-2 w-full max-w-[280px] sm:max-w-[320px] md:max-w-[360px] lg:max-w-[400px]'>
            <div className='flex justify-between items-center'>
                <div className='flex items-center gap-2'>
                    <Newspaper size={18} className='text-white' />
                    <h2 className='text-sm font-bold text-white'>{t('news.title')}</h2>
                </div>
                <div className='flex gap-2 sm:gap-3 items-center ml-2 sm:ml-4'>
                    {!isMinimized && (
                        <>
                            <button
                                onClick={() => setFilter('all')}
                                className={`px-3 py-1 text-xs rounded-lg transition-all ${
                                    filter === 'all'
                                        ? 'font-medium'
                                        : 'bg-white/10 text-white/70 hover:bg-white/20'
                                }`}
                                style={filter === 'all' ? { backgroundColor: accentColor, color: accentTextColor } : undefined}
                            >
                                {t('news.all')}
                            </button>
                            <button
                                onClick={() => setFilter('hytale')}
                                className={`px-3 py-1 text-xs rounded-lg transition-all ${
                                    filter === 'hytale'
                                        ? 'font-medium'
                                        : 'bg-white/10 text-white/70 hover:bg-white/20'
                                }`}
                                style={filter === 'hytale' ? { backgroundColor: accentColor, color: accentTextColor } : undefined}
                            >
                                {t('news.hytale')}
                            </button>
                            <button
                                onClick={() => setFilter('hyprism')}
                                className={`px-3 py-1 text-xs rounded-lg transition-all ${
                                    filter === 'hyprism'
                                        ? 'font-medium'
                                        : 'bg-white/10 text-white/70 hover:bg-white/20'
                                }`}
                                style={filter === 'hyprism' ? { backgroundColor: accentColor, color: accentTextColor } : undefined}
                            >
                                {t('news.hyprism')}
                            </button>
                        </>
                    )}
                    <IconButton
                        size="sm"
                        onClick={() => setIsMinimized(!isMinimized)}
                        title={isMinimized ? t('news.expand') : t('news.minimize')}
                        className="w-7 h-7"
                    >
                        {isMinimized ? <ChevronDown size={14} /> : <ChevronUp size={14} />}
                    </IconButton>
                </div>
            </div>

            {!isMinimized && (
                initialLoading ? (
                    <div className="flex items-center justify-center py-4">
                        <RefreshCw size={24} className="animate-spin" style={{ color: accentColor }} />
                    </div>
                ) : error ? (
                    <div className="flex items-center justify-center py-8">
                        <div className="text-center">
                            <p className="text-red-400 mb-3 text-sm">{error}</p>
                            <Button
                                onClick={() => fetchNews(limit, true)}
                                size="sm"
                            >
                                {t('news.tryAgain')}
                            </Button>
                        </div>
                    </div>
                ) : filteredNews.length > 0 ? (
                    <div ref={listRef} onScroll={handleScroll} className='flex flex-col gap-2 overflow-y-auto pr-1 relative max-h-[40vh]'>
                        {/* Subtle refresh indicator */}
                        {isRefreshing && (
                            <div className="absolute top-0 left-1/2 -translate-x-1/2 z-10">
                                <RefreshCw size={12} className="animate-spin" style={{ color: `${accentColor}80` }} />
                            </div>
                        )}
                        {filteredNews.map((item) => (
                            <button
                                key={item.url + item.title}
                                onClick={() => item.url && openLink(item.url)}
                                className='flex gap-2 group hover:bg-white/5 p-1.5 sm:p-2 rounded-lg transition-all cursor-pointer text-left w-full glass'
                            >
                                {item.imageUrl && (
                                    <img
                                        src={item.imageUrl}
                                        alt={item.title}
                                        className={`object-cover rounded-md flex-shrink-0 group-hover:scale-105 transition-transform ${item.source === 'hyprism' ? 'w-10 h-10' : 'w-20 h-14'}`}
                                    />
                                )}
                                <div className='flex flex-col justify-center min-w-0'>
                                    <p className='group-hover:underline text-xs font-medium line-clamp-2 mb-0.5' style={{ color: accentColor }}>
                                        {item.title}
                                    </p>
                                    <p className='text-white/70 text-xs line-clamp-2 mb-1'>{item.excerpt}</p>
                                    <div className='flex gap-x-2 items-center text-white/50 text-xs'>
                                        <span className='flex items-center gap-1'><User size='10' />{item.author}</span>
                                        <span className='flex items-center gap-1'><Calendar size='10' />{item.date}</span>
                                    </div>
                                </div>
                            </button>
                        ))}
                        {isRefreshing && (
                            <div className='flex items-center justify-center py-2 text-xs text-white/50'>
                                <RefreshCw size={12} className='animate-spin' />
                            </div>
                        )}
                        <LinkButton
                            onClick={() => openLink("https://hytale.com/news")}
                            className='w-full font-semibold text-xs mt-0.5'
                            style={{ color: accentColor }}>
                            {t('news.readMore')} →
                        </LinkButton>
                    </div>
                ) : (
                    <div className="flex items-center justify-center py-4">
                        <p className="text-white/50 text-sm">{t('news.noNewsFound')}</p>
                    </div>
                )
            )}

        </div>
    );
});

NewsPreview.displayName = 'NewsPreview';
