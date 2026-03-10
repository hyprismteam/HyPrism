/**
 * Language configuration metadata for all supported UI languages.
 */
import { Language } from './enums';

/** Metadata describing a single supported UI language. */
export interface LanguageMetadata {
    name: string;
    nativeName: string;
    code: Language;
    searchQuery: string;
    /** ISO 3166-1 alpha-2 country code for flag-icons (lowercase) */
    flagCode: string;
}

/** Map from each supported {@link Language} enum value to its display and configuration metadata. */
export const LANGUAGE_CONFIG: Record<Language, LanguageMetadata> = {
    [Language.ENGLISH]: {
        name: 'English',
        nativeName: 'English',
        code: Language.ENGLISH,
        searchQuery: '',
        flagCode: 'us',
    },
    [Language.RUSSIAN]: {
        name: 'Russian',
        nativeName: 'Русский',
        code: Language.RUSSIAN,
        searchQuery: 'Russian Translation (RU)',
        flagCode: 'ru',
    },
    [Language.TURKISH]: {
        name: 'Turkish',
        nativeName: 'Türkçe',
        code: Language.TURKISH,
        searchQuery: 'Türkçe çeviri',
        flagCode: 'tr',
    },
    [Language.FRENCH]: {
        name: 'French',
        nativeName: 'Français',
        code: Language.FRENCH,
        searchQuery: 'French Translation',
        flagCode: 'fr',
    },
    [Language.ITALIAN]: {
        name: 'Italian',
        nativeName: 'Italiano',
        code: Language.ITALIAN,
        searchQuery: 'Italian Translation',
        flagCode: 'it',
    },
    [Language.SPANISH]: {
        name: 'Spanish',
        nativeName: 'Español',
        code: Language.SPANISH,
        searchQuery: 'Spanish Translation',
        flagCode: 'es',
    },
    [Language.PORTUGUESE]: {
        name: 'Portuguese',
        nativeName: 'Português',
        code: Language.PORTUGUESE,
        searchQuery: 'Portuguese Translation',
        flagCode: 'br',
    },
    [Language.GERMAN]: {
        name: 'German',
        nativeName: 'Deutsch',
        code: Language.GERMAN,
        searchQuery: 'German Translation',
        flagCode: 'de',
    },
    [Language.CHINESE]: {
        name: 'Chinese',
        nativeName: '中文',
        code: Language.CHINESE,
        searchQuery: 'Chinese Translation',
        flagCode: 'cn',
    },
    [Language.JAPANESE]: {
        name: 'Japanese',
        nativeName: '日本語',
        code: Language.JAPANESE,
        searchQuery: 'Japanese Translation',
        flagCode: 'jp',
    },
    [Language.KOREAN]: {
        name: 'Korean',
        nativeName: '한국어',
        code: Language.KOREAN,
        searchQuery: 'Korean Translation',
        flagCode: 'kr',
    },
    [Language.UKRAINIAN]: {
        name: 'Ukrainian',
        nativeName: 'Українська',
        code: Language.UKRAINIAN,
        searchQuery: 'Ukrainian Translation (UA)',
        flagCode: 'ua',
    },
    [Language.BELARUSIAN]: {
        name: 'Belarusian',
        nativeName: 'Беларуская',
        code: Language.BELARUSIAN,
        searchQuery: 'Belarusian Translation (BE)',
        flagCode: 'by',
    },
};
