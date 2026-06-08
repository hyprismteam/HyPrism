import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import { Language } from './constants/enums';
import { ipc } from '@/lib/ipc';

// Import all locale files directly
import enUS from './assets/locales/en-US.json';
import ruRU from './assets/locales/ru-RU.json';
import deDE from './assets/locales/de-DE.json';
import esES from './assets/locales/es-ES.json';
import frFR from './assets/locales/fr-FR.json';
import itIT from './assets/locales/it-IT.json';
import jaJP from './assets/locales/ja-JP.json';
import koKR from './assets/locales/ko-KR.json';
import ptBR from './assets/locales/pt-BR.json';
import trTR from './assets/locales/tr-TR.json';
import ukUA from './assets/locales/uk-UA.json';
import zhCN from './assets/locales/zh-CN.json';
import beBY from './assets/locales/be-BY.json';

// Map of all available translations
const locales: Record<string, Record<string, unknown>> = {
    'en-US': enUS,
    'ru-RU': ruRU,
    'de-DE': deDE,
    'es-ES': esES,
    'fr-FR': frFR,
    'ja-JP': jaJP,
    'ko-KR': koKR,
    'pt-BR': ptBR,
    'it-IT': itIT,
    'tr-TR': trTR,
    'uk-UA': ukUA,
    'zh-CN': zhCN,
    'be-BY': beBY,
};

/**
 * Flatten nested JSON object to dot-separated keys
 * { main: { play: "PLAY" } } -> { "main.play": "PLAY" }
 */
function flattenTranslations(obj: Record<string, unknown>, prefix = ''): Record<string, string> {
    const result: Record<string, string> = {};
    
    for (const [key, value] of Object.entries(obj)) {
        const newKey = prefix ? `${prefix}.${key}` : key;
        
        if (typeof value === 'object' && value !== null && !Array.isArray(value)) {
            Object.assign(result, flattenTranslations(value as Record<string, unknown>, newKey));
        } else if (typeof value === 'string') {
            result[newKey] = value;
        }
    }
    
    return result;
}

/**
 * Get flattened translations for a language code
 */
function getTranslations(langCode: string): Record<string, string> {
    const locale = locales[langCode] || locales['en-US'];
    return flattenTranslations(locale);
}

/**
 * Initialize i18next with translations loaded from local files.
 * Must be called (and awaited) before rendering the React tree.
 */
export async function initI18n(): Promise<void> {
    let currentLang = Language.ENGLISH as string;

    try {
        // Get language from settings (persisted config)
        console.log('[i18n] Calling ipc.settings.get()...');
        const settings = await ipc.settings.get();
        console.log('[i18n] Settings from backend:', JSON.stringify(settings));
        console.log('[i18n] settings.language =', settings?.language, 'type:', typeof settings?.language);
        currentLang = settings?.language || Language.ENGLISH;
        console.log('[i18n] currentLang after assignment:', currentLang);
        
        // Validate language exists
        if (!locales[currentLang]) {
            console.warn(`[i18n] Language ${currentLang} not found in locales, falling back to English`);
            console.log('[i18n] Available locales:', Object.keys(locales));
            currentLang = Language.ENGLISH;
        }
    } catch (err) {
        console.warn('[i18n] Failed to get settings, using English:', err);
    }

    console.log('[i18n] Initializing with language:', currentLang);
    const translations = getTranslations(currentLang);
    console.log('[i18n] Loaded', Object.keys(translations).length, 'translations for', currentLang);

    // Build resources for all languages
    const resources: Record<string, { translation: Record<string, string> }> = {};
    for (const langCode of Object.keys(locales)) {
        resources[langCode] = { translation: getTranslations(langCode) };
    }

    await i18n
        .use(initReactI18next)
        .init({
            resources,
            lng: currentLang,
            fallbackLng: Language.ENGLISH,
            interpolation: {
                escapeValue: false,
            },
            // Return key as-is when translation not found
            returnNull: false,
            returnEmptyString: false,
        });
}

/**
 * Switch language: updates backend setting, then switches i18next.
 */
export async function changeLanguage(langCode: string): Promise<void> {
    // Validate language exists
    if (!locales[langCode]) {
        console.warn(`[i18n] Language ${langCode} not found`);
        return;
    }

    console.log(`[i18n] Changing language to: ${langCode}`);

    // Tell the backend to persist the language setting
    try {
        const result = await ipc.i18n.set(langCode);
        console.log(`[i18n] Backend response:`, result);
        if (!result?.success) {
            console.warn('[i18n] Backend failed to set language');
        }
    } catch (err) {
        console.warn('[i18n] Failed to persist language setting:', err);
    }

    // Switch i18next to the new language
    await i18n.changeLanguage(langCode);
    console.log(`[i18n] i18next language switched to: ${i18n.language}`);
}

/**
 * Get list of available languages
 */
export function getAvailableLanguages(): Array<{ code: string; name: string }> {
    return Object.entries(locales).map(([code, data]) => ({
        code,
        name: (data as { _langName?: string })._langName || code,
    }));
}

export default i18n;
