import React, { useState, useEffect, useCallback, useMemo, memo } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { useTranslation } from 'react-i18next';
import { RefreshCw, ExternalLink, Calendar, User, Newspaper, Github } from 'lucide-react';
import { useAccentColor } from '../contexts/AccentColorContext';
import { ipc } from '@/lib/ipc';
import { PageContainer } from '@/components/ui/PageContainer';
import { Button, LinkButton, AccentSegmentedControl } from '@/components/ui/Controls';
import { pageVariants } from '@/constants/animations';

type NewsFilter = 'all' | 'hytale' | 'hyprism';

interface EnrichedNewsItem {
  title: string;
  excerpt?: string;
  url?: string;
  date?: string;
  author?: string;
  imageUrl?: string;
  source?: 'hytale' | 'hyprism';
}

interface NewsPageProps {
  getNews: (count: number) => Promise<EnrichedNewsItem[]>;
}

const cardVariants = {
  hidden: { opacity: 0, y: 20 },
  visible: (i: number) => ({
    opacity: 1,
    y: 0,
    transition: { delay: i * 0.05, duration: 0.35, ease: 'easeOut' },
  }),
};

export const NewsPage: React.FC<NewsPageProps> = memo(({ getNews }) => {
  const { t } = useTranslation();
  const { accentColor, accentTextColor } = useAccentColor();
  const [news, setNews] = useState<EnrichedNewsItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState<NewsFilter>('all');

  const fetchNews = useCallback(async (reset = false) => {
    if (news.length === 0) setLoading(true);
    else setIsRefreshing(true);
    setError(null);
    try {
      const items = await getNews(20);
      setNews(reset ? (items ?? []) : (() => {
        const seen = new Map<string, EnrichedNewsItem>();
        news.forEach((item) => seen.set(item.url || item.title, item));
        (items ?? []).forEach((item) => seen.set(item.url || item.title, item));
        return Array.from(seen.values());
      })());
    } catch (err) {
      if (news.length === 0) setError(err instanceof Error ? err.message : 'Failed to fetch news');
    } finally {
      setLoading(false);
      setIsRefreshing(false);
    }
  }, [getNews, news]);

  // Load once on mount
  useEffect(() => { fetchNews(true); }, []); // eslint-disable-line react-hooks/exhaustive-deps

  const filteredNews = useMemo(
    () => (filter === 'all' ? news : news.filter(item => item.source === filter)),
    [filter, news]
  );

  const openLink = useCallback((url: string) => { ipc.browser.open(url); }, []);

  const filterItems = useMemo(() => [
    { value: 'all' as NewsFilter,     label: t('news.all') },
    { value: 'hytale' as NewsFilter,  label: t('news.hytale') },
    { value: 'hyprism' as NewsFilter, label: t('news.hyprism') },
  ], [t]);

  return (
    <motion.div
      variants={pageVariants}
      initial="initial"
      animate="animate"
      exit="exit"
      transition={{ duration: 0.3, ease: 'easeOut' }}
      className="h-full w-full"
    >
      <PageContainer contentClassName="h-full">
      <div className="h-full flex flex-col">
      {/* Header */}
      <div className="flex items-center justify-between mb-6 flex-shrink-0">
        <div className="flex items-center gap-3">
          <Newspaper size={22} className="text-white opacity-80" />
          <h1 className="text-xl font-bold text-white">{t('news.title')}</h1>
          {isRefreshing && <RefreshCw size={14} className="animate-spin text-white opacity-40" />}
        </div>

        {/* Source filter — segmented control with accent slider */}
        <AccentSegmentedControl<NewsFilter>
          value={filter}
          onChange={setFilter}
          items={filterItems}
        />
      </div>
      {/* Content */}
      {loading ? (
        <div className="flex-1 flex items-center justify-center">
          <RefreshCw size={32} className="animate-spin" style={{ color: accentColor }} />
        </div>
      ) : error ? (
        <div className="flex-1 flex items-center justify-center">
          <div className="text-center">
            <p className="text-red-400 mb-4">{error}</p>
            <Button onClick={() => fetchNews(true)}>
              {t('news.tryAgain')}
            </Button>
          </div>
        </div>
      ) : filteredNews.length === 0 ? (
        <div className="flex-1 flex items-center justify-center">
          <p className="text-white/40">{t('news.noNewsFound')}</p>
        </div>
      ) : (
        <div className="flex-1 overflow-y-auto pr-2 -mr-2">
          <div className="grid grid-cols-2 lg:grid-cols-3 gap-4">
            <AnimatePresence>
              {filteredNews.map((item, index) => (
                <motion.button
                  key={item.url || item.title}
                  custom={index}
                  variants={cardVariants}
                  initial="hidden"
                  animate="visible"
                  whileTap={{ scale: 0.98 }}
                  onClick={() => item.url && openLink(item.url)}
                  className="relative group rounded-2xl overflow-hidden text-left cursor-pointer"
                  style={{
                    aspectRatio: '16/10',
                    background: 'rgba(255,255,255,0.03)',
                    border: '1px solid rgba(255,255,255,0.06)',
                  }}
                >
                  {/* Background Image or Placeholder */}
                  {item.source === 'hyprism' ? (
                    <div className="absolute inset-0 bg-gradient-to-br from-[#2c2c2e] to-[#1c1c1e] flex items-center justify-center">
                      <Github size={64} className="text-white opacity-10" />
                    </div>
                  ) : item.imageUrl ? (
                    <img
                      src={item.imageUrl}
                      alt={item.title}
                      className="absolute inset-0 w-full h-full object-cover transition-transform duration-500 group-hover:scale-105"
                    />
                  ) : null}

                  {/* Gradient Overlay */}
                  <div className="absolute inset-0 bg-gradient-to-t from-black/90 via-black/40 to-transparent" />

                  {/* Source Badge */}
                  {item.source && (
                    <div className="absolute top-3 left-3 z-10">
                      <span
                        className="px-2 py-0.5 text-[10px] font-bold uppercase rounded-md"
                        style={{
                          backgroundColor: item.source === 'hytale' ? 'rgba(255,168,69,0.9)' : `${accentColor}dd`,
                          color: item.source === 'hytale' ? '#000' : accentTextColor,
                        }}
                      >
                        {item.source === 'hytale' ? 'Hytale' : 'HyPrism'}
                      </span>
                    </div>
                  )}

                  {/* Read More glass icon - right side, visible on hover */}
                  <div className="absolute top-3 right-3 z-10 opacity-0 group-hover:opacity-100 translate-x-2 group-hover:translate-x-0 transition-all duration-300">
                    <span
                      className="flex items-center justify-center w-8 h-8 rounded-xl"
                      style={{
                        background: 'linear-gradient(135deg, rgba(255,255,255,0.15) 0%, rgba(255,255,255,0.06) 100%)',
                        backdropFilter: 'blur(20px)',
                        WebkitBackdropFilter: 'blur(20px)',
                        border: '1px solid rgba(255,255,255,0.15)',
                        boxShadow: '0 2px 8px rgba(0,0,0,0.3)',
                      }}
                    >
                      <ExternalLink size={14} className="text-white" />
                    </span>
                  </div>

                  {/* Content */}
                  <div className="absolute bottom-0 left-0 right-0 p-4 z-10">
                    <h3 className="text-white font-bold text-sm line-clamp-2 mb-1 drop-shadow-lg">
                      {item.title}
                    </h3>
                    <p className="text-white/60 text-xs line-clamp-2 mb-2 drop-shadow">
                      {item.excerpt}
                    </p>
                    <div className="flex items-center gap-3 text-white/40 text-[10px]">
                      {item.author && (
                        <span className="flex items-center gap-1"><User size={10} />{item.author}</span>
                      )}
                      {item.date && (
                        <span className="flex items-center gap-1"><Calendar size={10} />{item.date}</span>
                      )}
                    </div>
                  </div>
                </motion.button>
              ))}
            </AnimatePresence>
          </div>

          {/* Load more link */}
          <div className="text-center py-4 mt-2">
            <LinkButton
              onClick={() => openLink("https://hytale.com/news")}
              className="font-semibold text-xs"
              style={{ color: accentColor }}
            >
              {t('news.readMore')} →
            </LinkButton>
          </div>
        </div>
      )}
      </div>
      </PageContainer>
    </motion.div>
  );
});

NewsPage.displayName = 'NewsPage';
