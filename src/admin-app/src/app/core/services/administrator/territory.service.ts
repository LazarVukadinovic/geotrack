import { Injectable } from '@angular/core';
import { Observable, BehaviorSubject, of } from 'rxjs';
import { delay, tap } from 'rxjs/operators';
import {
  Country, Territory, TerritoryManager,
  CreateTerritoryRequest, TerritoryPage,
} from '../../models/administrator/territory.model';

export const ALL_COUNTRIES: Country[] = [
  // Europe
  { code: 'AL', name: 'Albania', continent: 'Europe', flag: '🇦🇱' },
  { code: 'AT', name: 'Austria', continent: 'Europe', flag: '🇦🇹' },
  { code: 'BE', name: 'Belgium', continent: 'Europe', flag: '🇧🇪' },
  { code: 'BA', name: 'Bosnia & Herzegovina', continent: 'Europe', flag: '🇧🇦' },
  { code: 'BG', name: 'Bulgaria', continent: 'Europe', flag: '🇧🇬' },
  { code: 'HR', name: 'Croatia', continent: 'Europe', flag: '🇭🇷' },
  { code: 'CY', name: 'Cyprus', continent: 'Europe', flag: '🇨🇾' },
  { code: 'CZ', name: 'Czech Republic', continent: 'Europe', flag: '🇨🇿' },
  { code: 'DK', name: 'Denmark', continent: 'Europe', flag: '🇩🇰' },
  { code: 'EE', name: 'Estonia', continent: 'Europe', flag: '🇪🇪' },
  { code: 'FI', name: 'Finland', continent: 'Europe', flag: '🇫🇮' },
  { code: 'FR', name: 'France', continent: 'Europe', flag: '🇫🇷' },
  { code: 'DE', name: 'Germany', continent: 'Europe', flag: '🇩🇪' },
  { code: 'GR', name: 'Greece', continent: 'Europe', flag: '🇬🇷' },
  { code: 'HU', name: 'Hungary', continent: 'Europe', flag: '🇭🇺' },
  { code: 'IE', name: 'Ireland', continent: 'Europe', flag: '🇮🇪' },
  { code: 'IT', name: 'Italy', continent: 'Europe', flag: '🇮🇹' },
  { code: 'LV', name: 'Latvia', continent: 'Europe', flag: '🇱🇻' },
  { code: 'LT', name: 'Lithuania', continent: 'Europe', flag: '🇱🇹' },
  { code: 'LU', name: 'Luxembourg', continent: 'Europe', flag: '🇱🇺' },
  { code: 'MT', name: 'Malta', continent: 'Europe', flag: '🇲🇹' },
  { code: 'MD', name: 'Moldova', continent: 'Europe', flag: '🇲🇩' },
  { code: 'ME', name: 'Montenegro', continent: 'Europe', flag: '🇲🇪' },
  { code: 'NL', name: 'Netherlands', continent: 'Europe', flag: '🇳🇱' },
  { code: 'MK', name: 'North Macedonia', continent: 'Europe', flag: '🇲🇰' },
  { code: 'NO', name: 'Norway', continent: 'Europe', flag: '🇳🇴' },
  { code: 'PL', name: 'Poland', continent: 'Europe', flag: '🇵🇱' },
  { code: 'PT', name: 'Portugal', continent: 'Europe', flag: '🇵🇹' },
  { code: 'RO', name: 'Romania', continent: 'Europe', flag: '🇷🇴' },
  { code: 'RS', name: 'Serbia', continent: 'Europe', flag: '🇷🇸' },
  { code: 'SK', name: 'Slovakia', continent: 'Europe', flag: '🇸🇰' },
  { code: 'SI', name: 'Slovenia', continent: 'Europe', flag: '🇸🇮' },
  { code: 'ES', name: 'Spain', continent: 'Europe', flag: '🇪🇸' },
  { code: 'SE', name: 'Sweden', continent: 'Europe', flag: '🇸🇪' },
  { code: 'CH', name: 'Switzerland', continent: 'Europe', flag: '🇨🇭' },
  { code: 'UA', name: 'Ukraine', continent: 'Europe', flag: '🇺🇦' },
  { code: 'GB', name: 'United Kingdom', continent: 'Europe', flag: '🇬🇧' },
  { code: 'BY', name: 'Belarus', continent: 'Europe', flag: '🇧🇾' },
  { code: 'RU', name: 'Russia', continent: 'Europe', flag: '🇷🇺' },

  // Asia
  { code: 'AM', name: 'Armenia', continent: 'Asia', flag: '🇦🇲' },
  { code: 'AZ', name: 'Azerbaijan', continent: 'Asia', flag: '🇦🇿' },
  { code: 'BH', name: 'Bahrain', continent: 'Asia', flag: '🇧🇭' },
  { code: 'BD', name: 'Bangladesh', continent: 'Asia', flag: '🇧🇩' },
  { code: 'BT', name: 'Bhutan', continent: 'Asia', flag: '🇧🇹' },
  { code: 'KH', name: 'Cambodia', continent: 'Asia', flag: '🇰🇭' },
  { code: 'CN', name: 'China', continent: 'Asia', flag: '🇨🇳' },
  { code: 'GE', name: 'Georgia', continent: 'Asia', flag: '🇬🇪' },
  { code: 'IN', name: 'India', continent: 'Asia', flag: '🇮🇳' },
  { code: 'ID', name: 'Indonesia', continent: 'Asia', flag: '🇮🇩' },
  { code: 'IR', name: 'Iran', continent: 'Asia', flag: '🇮🇷' },
  { code: 'IQ', name: 'Iraq', continent: 'Asia', flag: '🇮🇶' },
  { code: 'IL', name: 'Israel', continent: 'Asia', flag: '🇮🇱' },
  { code: 'JP', name: 'Japan', continent: 'Asia', flag: '🇯🇵' },
  { code: 'JO', name: 'Jordan', continent: 'Asia', flag: '🇯🇴' },
  { code: 'KZ', name: 'Kazakhstan', continent: 'Asia', flag: '🇰🇿' },
  { code: 'KW', name: 'Kuwait', continent: 'Asia', flag: '🇰🇼' },
  { code: 'KG', name: 'Kyrgyzstan', continent: 'Asia', flag: '🇰🇬' },
  { code: 'LA', name: 'Laos', continent: 'Asia', flag: '🇱🇦' },
  { code: 'LB', name: 'Lebanon', continent: 'Asia', flag: '🇱🇧' },
  { code: 'MY', name: 'Malaysia', continent: 'Asia', flag: '🇲🇾' },
  { code: 'MV', name: 'Maldives', continent: 'Asia', flag: '🇲🇻' },
  { code: 'MN', name: 'Mongolia', continent: 'Asia', flag: '🇲🇳' },
  { code: 'MM', name: 'Myanmar', continent: 'Asia', flag: '🇲🇲' },
  { code: 'NP', name: 'Nepal', continent: 'Asia', flag: '🇳🇵' },
  { code: 'KP', name: 'North Korea', continent: 'Asia', flag: '🇰🇵' },
  { code: 'OM', name: 'Oman', continent: 'Asia', flag: '🇴🇲' },
  { code: 'PK', name: 'Pakistan', continent: 'Asia', flag: '🇵🇰' },
  { code: 'PH', name: 'Philippines', continent: 'Asia', flag: '🇵🇭' },
  { code: 'QA', name: 'Qatar', continent: 'Asia', flag: '🇶🇦' },
  { code: 'SA', name: 'Saudi Arabia', continent: 'Asia', flag: '🇸🇦' },
  { code: 'SG', name: 'Singapore', continent: 'Asia', flag: '🇸🇬' },
  { code: 'KR', name: 'South Korea', continent: 'Asia', flag: '🇰🇷' },
  { code: 'LK', name: 'Sri Lanka', continent: 'Asia', flag: '🇱🇰' },
  { code: 'SY', name: 'Syria', continent: 'Asia', flag: '🇸🇾' },
  { code: 'TW', name: 'Taiwan', continent: 'Asia', flag: '🇹🇼' },
  { code: 'TJ', name: 'Tajikistan', continent: 'Asia', flag: '🇹🇯' },
  { code: 'TH', name: 'Thailand', continent: 'Asia', flag: '🇹🇭' },
  { code: 'TR', name: 'Turkey', continent: 'Asia', flag: '🇹🇷' },
  { code: 'TM', name: 'Turkmenistan', continent: 'Asia', flag: '🇹🇲' },
  { code: 'AE', name: 'United Arab Emirates', continent: 'Asia', flag: '🇦🇪' },
  { code: 'UZ', name: 'Uzbekistan', continent: 'Asia', flag: '🇺🇿' },
  { code: 'VN', name: 'Vietnam', continent: 'Asia', flag: '🇻🇳' },
  { code: 'YE', name: 'Yemen', continent: 'Asia', flag: '🇾🇪' },

  // Africa
  { code: 'DZ', name: 'Algeria', continent: 'Africa', flag: '🇩🇿' },
  { code: 'AO', name: 'Angola', continent: 'Africa', flag: '🇦🇴' },
  { code: 'BJ', name: 'Benin', continent: 'Africa', flag: '🇧🇯' },
  { code: 'BW', name: 'Botswana', continent: 'Africa', flag: '🇧🇼' },
  { code: 'BF', name: 'Burkina Faso', continent: 'Africa', flag: '🇧🇫' },
  { code: 'BI', name: 'Burundi', continent: 'Africa', flag: '🇧🇮' },
  { code: 'CM', name: 'Cameroon', continent: 'Africa', flag: '🇨🇲' },
  { code: 'CV', name: 'Cape Verde', continent: 'Africa', flag: '🇨🇻' },
  { code: 'CF', name: 'Central African Republic', continent: 'Africa', flag: '🇨🇫' },
  { code: 'TD', name: 'Chad', continent: 'Africa', flag: '🇹🇩' },
  { code: 'KM', name: 'Comoros', continent: 'Africa', flag: '🇰🇲' },
  { code: 'CD', name: 'Congo (DRC)', continent: 'Africa', flag: '🇨🇩' },
  { code: 'CG', name: 'Congo', continent: 'Africa', flag: '🇨🇬' },
  { code: 'CI', name: "Côte d'Ivoire", continent: 'Africa', flag: '🇨🇮' },
  { code: 'DJ', name: 'Djibouti', continent: 'Africa', flag: '🇩🇯' },
  { code: 'EG', name: 'Egypt', continent: 'Africa', flag: '🇪🇬' },
  { code: 'GQ', name: 'Equatorial Guinea', continent: 'Africa', flag: '🇬🇶' },
  { code: 'ER', name: 'Eritrea', continent: 'Africa', flag: '🇪🇷' },
  { code: 'SZ', name: 'Eswatini', continent: 'Africa', flag: '🇸🇿' },
  { code: 'ET', name: 'Ethiopia', continent: 'Africa', flag: '🇪🇹' },
  { code: 'GA', name: 'Gabon', continent: 'Africa', flag: '🇬🇦' },
  { code: 'GM', name: 'Gambia', continent: 'Africa', flag: '🇬🇲' },
  { code: 'GH', name: 'Ghana', continent: 'Africa', flag: '🇬🇭' },
  { code: 'GN', name: 'Guinea', continent: 'Africa', flag: '🇬🇳' },
  { code: 'GW', name: 'Guinea-Bissau', continent: 'Africa', flag: '🇬🇼' },
  { code: 'KE', name: 'Kenya', continent: 'Africa', flag: '🇰🇪' },
  { code: 'LS', name: 'Lesotho', continent: 'Africa', flag: '🇱🇸' },
  { code: 'LR', name: 'Liberia', continent: 'Africa', flag: '🇱🇷' },
  { code: 'LY', name: 'Libya', continent: 'Africa', flag: '🇱🇾' },
  { code: 'MG', name: 'Madagascar', continent: 'Africa', flag: '🇲🇬' },
  { code: 'MW', name: 'Malawi', continent: 'Africa', flag: '🇲🇼' },
  { code: 'ML', name: 'Mali', continent: 'Africa', flag: '🇲🇱' },
  { code: 'MR', name: 'Mauritania', continent: 'Africa', flag: '🇲🇷' },
  { code: 'MU', name: 'Mauritius', continent: 'Africa', flag: '🇲🇺' },
  { code: 'MA', name: 'Morocco', continent: 'Africa', flag: '🇲🇦' },
  { code: 'MZ', name: 'Mozambique', continent: 'Africa', flag: '🇲🇿' },
  { code: 'NA', name: 'Namibia', continent: 'Africa', flag: '🇳🇦' },
  { code: 'NE', name: 'Niger', continent: 'Africa', flag: '🇳🇪' },
  { code: 'NG', name: 'Nigeria', continent: 'Africa', flag: '🇳🇬' },
  { code: 'RW', name: 'Rwanda', continent: 'Africa', flag: '🇷🇼' },
  { code: 'ST', name: 'São Tomé & Príncipe', continent: 'Africa', flag: '🇸🇹' },
  { code: 'SN', name: 'Senegal', continent: 'Africa', flag: '🇸🇳' },
  { code: 'SC', name: 'Seychelles', continent: 'Africa', flag: '🇸🇨' },
  { code: 'SL', name: 'Sierra Leone', continent: 'Africa', flag: '🇸🇱' },
  { code: 'SO', name: 'Somalia', continent: 'Africa', flag: '🇸🇴' },
  { code: 'ZA', name: 'South Africa', continent: 'Africa', flag: '🇿🇦' },
  { code: 'SS', name: 'South Sudan', continent: 'Africa', flag: '🇸🇸' },
  { code: 'SD', name: 'Sudan', continent: 'Africa', flag: '🇸🇩' },
  { code: 'TZ', name: 'Tanzania', continent: 'Africa', flag: '🇹🇿' },
  { code: 'TG', name: 'Togo', continent: 'Africa', flag: '🇹🇬' },
  { code: 'TN', name: 'Tunisia', continent: 'Africa', flag: '🇹🇳' },
  { code: 'UG', name: 'Uganda', continent: 'Africa', flag: '🇺🇬' },
  { code: 'ZM', name: 'Zambia', continent: 'Africa', flag: '🇿🇲' },
  { code: 'ZW', name: 'Zimbabwe', continent: 'Africa', flag: '🇿🇼' },

  // North America
  { code: 'AG', name: 'Antigua & Barbuda', continent: 'North America', flag: '🇦🇬' },
  { code: 'BS', name: 'Bahamas', continent: 'North America', flag: '🇧🇸' },
  { code: 'BB', name: 'Barbados', continent: 'North America', flag: '🇧🇧' },
  { code: 'BZ', name: 'Belize', continent: 'North America', flag: '🇧🇿' },
  { code: 'CA', name: 'Canada', continent: 'North America', flag: '🇨🇦' },
  { code: 'CR', name: 'Costa Rica', continent: 'North America', flag: '🇨🇷' },
  { code: 'CU', name: 'Cuba', continent: 'North America', flag: '🇨🇺' },
  { code: 'DM', name: 'Dominica', continent: 'North America', flag: '🇩🇲' },
  { code: 'DO', name: 'Dominican Republic', continent: 'North America', flag: '🇩🇴' },
  { code: 'SV', name: 'El Salvador', continent: 'North America', flag: '🇸🇻' },
  { code: 'GD', name: 'Grenada', continent: 'North America', flag: '🇬🇩' },
  { code: 'GT', name: 'Guatemala', continent: 'North America', flag: '🇬🇹' },
  { code: 'HT', name: 'Haiti', continent: 'North America', flag: '🇭🇹' },
  { code: 'HN', name: 'Honduras', continent: 'North America', flag: '🇭🇳' },
  { code: 'JM', name: 'Jamaica', continent: 'North America', flag: '🇯🇲' },
  { code: 'MX', name: 'Mexico', continent: 'North America', flag: '🇲🇽' },
  { code: 'NI', name: 'Nicaragua', continent: 'North America', flag: '🇳🇮' },
  { code: 'PA', name: 'Panama', continent: 'North America', flag: '🇵🇦' },
  { code: 'KN', name: 'Saint Kitts & Nevis', continent: 'North America', flag: '🇰🇳' },
  { code: 'LC', name: 'Saint Lucia', continent: 'North America', flag: '🇱🇨' },
  { code: 'VC', name: 'Saint Vincent & the Grenadines', continent: 'North America', flag: '🇻🇨' },
  { code: 'TT', name: 'Trinidad & Tobago', continent: 'North America', flag: '🇹🇹' },
  { code: 'US', name: 'United States', continent: 'North America', flag: '🇺🇸' },

  // South America
  { code: 'AR', name: 'Argentina', continent: 'South America', flag: '🇦🇷' },
  { code: 'BO', name: 'Bolivia', continent: 'South America', flag: '🇧🇴' },
  { code: 'BR', name: 'Brazil', continent: 'South America', flag: '🇧🇷' },
  { code: 'CL', name: 'Chile', continent: 'South America', flag: '🇨🇱' },
  { code: 'CO', name: 'Colombia', continent: 'South America', flag: '🇨🇴' },
  { code: 'EC', name: 'Ecuador', continent: 'South America', flag: '🇪🇨' },
  { code: 'GY', name: 'Guyana', continent: 'South America', flag: '🇬🇾' },
  { code: 'PY', name: 'Paraguay', continent: 'South America', flag: '🇵🇾' },
  { code: 'PE', name: 'Peru', continent: 'South America', flag: '🇵🇪' },
  { code: 'SR', name: 'Suriname', continent: 'South America', flag: '🇸🇷' },
  { code: 'UY', name: 'Uruguay', continent: 'South America', flag: '🇺🇾' },
  { code: 'VE', name: 'Venezuela', continent: 'South America', flag: '🇻🇪' },

  // Oceania
  { code: 'AU', name: 'Australia', continent: 'Oceania', flag: '🇦🇺' },
  { code: 'FJ', name: 'Fiji', continent: 'Oceania', flag: '🇫🇯' },
  { code: 'KI', name: 'Kiribati', continent: 'Oceania', flag: '🇰🇮' },
  { code: 'MH', name: 'Marshall Islands', continent: 'Oceania', flag: '🇲🇭' },
  { code: 'FM', name: 'Micronesia', continent: 'Oceania', flag: '🇫🇲' },
  { code: 'NR', name: 'Nauru', continent: 'Oceania', flag: '🇳🇷' },
  { code: 'NZ', name: 'New Zealand', continent: 'Oceania', flag: '🇳🇿' },
  { code: 'PW', name: 'Palau', continent: 'Oceania', flag: '🇵🇼' },
  { code: 'PG', name: 'Papua New Guinea', continent: 'Oceania', flag: '🇵🇬' },
  { code: 'WS', name: 'Samoa', continent: 'Oceania', flag: '🇼🇸' },
  { code: 'SB', name: 'Solomon Islands', continent: 'Oceania', flag: '🇸🇧' },
  { code: 'TO', name: 'Tonga', continent: 'Oceania', flag: '🇹🇴' },
  { code: 'TV', name: 'Tuvalu', continent: 'Oceania', flag: '🇹🇻' },
  { code: 'VU', name: 'Vanuatu', continent: 'Oceania', flag: '🇻🇺' },
];

export const CONTINENTS = ['Europe', 'Asia', 'Africa', 'North America', 'South America', 'Oceania'] as const;

const TERRITORY_MANAGERS: TerritoryManager[] = [
  { id: 'TM-001', name: 'Marko Stojanović', email: 'm.stojanovic@geotrack.com', initials: 'MS', avatarColor: '#3b82f6', assignedTerritoryIds: ['TERR-001'], status: 'Active' },
  { id: 'TM-002', name: 'Jelena Marković', email: 'j.markovic@geotrack.com', initials: 'JM', avatarColor: '#10b981', assignedTerritoryIds: ['TERR-002'], status: 'Active' },
  { id: 'TM-003', name: 'John Williams', email: 'j.williams@geotrack.com', initials: 'JW', avatarColor: '#8b5cf6', assignedTerritoryIds: ['TERR-004'], status: 'Active' },
  { id: 'TM-004', name: 'Ana Đorđević', email: 'a.djordjevic@geotrack.com', initials: 'AĐ', avatarColor: '#f59e0b', assignedTerritoryIds: [], status: 'Active' },
  { id: 'TM-005', name: 'Robert Chen', email: 'r.chen@geotrack.com', initials: 'RC', avatarColor: '#ef4444', assignedTerritoryIds: [], status: 'Inactive' },
];

let TERRITORIES: Territory[] = [
  {
    id: 'TERR-001', name: 'Western Europe',
    countries: ['DE', 'FR', 'IT', 'ES', 'PT', 'NL', 'BE', 'CH', 'AT', 'LU', 'IE'],
    assignedManagerId: 'TM-001', assignedManagerName: 'Marko Stojanović', assignedManagerEmail: 'm.stojanovic@geotrack.com',
    status: 'Active', createdAt: '2024-01-10T09:00:00Z', updatedAt: '2024-03-15T14:30:00Z',
  },
  {
    id: 'TERR-002', name: 'Balkans & Eastern Europe',
    countries: ['RS', 'HR', 'BA', 'ME', 'MK', 'SI', 'SK', 'CZ', 'HU', 'RO', 'BG', 'AL'],
    assignedManagerId: 'TM-002', assignedManagerName: 'Jelena Marković', assignedManagerEmail: 'j.markovic@geotrack.com',
    status: 'Active', createdAt: '2024-01-15T10:00:00Z', updatedAt: '2024-04-20T11:15:00Z',
  },
  {
    id: 'TERR-003', name: 'Middle East',
    countries: ['TR', 'SA', 'AE', 'QA', 'KW', 'BH', 'OM', 'JO', 'LB', 'IL'],
    assignedManagerId: null, assignedManagerName: null, assignedManagerEmail: null,
    status: 'Unassigned', createdAt: '2024-02-01T08:00:00Z', updatedAt: '2024-02-01T08:00:00Z',
  },
  {
    id: 'TERR-004', name: 'Southeast Asia',
    countries: ['TH', 'VN', 'ID', 'MY', 'PH', 'SG', 'MM', 'KH', 'LA'],
    assignedManagerId: 'TM-003', assignedManagerName: 'John Williams', assignedManagerEmail: 'j.williams@geotrack.com',
    status: 'Active', createdAt: '2024-02-20T12:00:00Z', updatedAt: '2024-05-01T09:45:00Z',
  },
];

@Injectable({ providedIn: 'root' })
export class TerritoryService {
  readonly lastPage$ = new BehaviorSubject<TerritoryPage | null>(null);

  getAllCountries(): Country[] {
    return ALL_COUNTRIES;
  }

  getCountriesByContinent(): Record<string, Country[]> {
    const result: Record<string, Country[]> = {};
    for (const continent of CONTINENTS) {
      result[continent] = ALL_COUNTRIES.filter(c => c.continent === continent);
    }
    return result;
  }

  getCountryByCode(code: string): Country | undefined {
    return ALL_COUNTRIES.find(c => c.code === code);
  }

  getAllManagers(): TerritoryManager[] {
    return [...TERRITORY_MANAGERS];
  }

  getActiveManagers(): TerritoryManager[] {
    return TERRITORY_MANAGERS.filter(m => m.status === 'Active');
  }

  getPage$(
    search = '',
    pageSize = 20,
    sortField: 'name' | 'status' | 'countries' | 'createdAt' = 'createdAt',
    sortDir: 'asc' | 'desc' = 'desc',
    countryFilter: string[] = [],
  ): Observable<TerritoryPage> {
    let filtered = TERRITORIES.filter(t => {
      if (search) {
        const q = search.toLowerCase();
        const matchName    = t.name.toLowerCase().includes(q);
        const matchManager = t.assignedManagerName?.toLowerCase().includes(q) ?? false;
        const matchCountry = t.countries.some(code => {
          const c = ALL_COUNTRIES.find(x => x.code === code);
          return c?.name.toLowerCase().includes(q) ?? false;
        });
        if (!matchName && !matchManager && !matchCountry) return false;
      }
      if (countryFilter.length > 0) {
        return countryFilter.every(code => t.countries.includes(code));
      }
      return true;
    });

    filtered = [...filtered].sort((a, b) => {
      let cmp = 0;
      if (sortField === 'name')      cmp = a.name.localeCompare(b.name);
      else if (sortField === 'status')    cmp = a.status.localeCompare(b.status);
      else if (sortField === 'countries') cmp = a.countries.length - b.countries.length;
      else if (sortField === 'createdAt') cmp = a.createdAt.localeCompare(b.createdAt);
      return sortDir === 'asc' ? cmp : -cmp;
    });

    const total = filtered.length;
    const items = filtered.slice(0, pageSize);
    const page = { items, total };
    return of(page).pipe(
      delay(300),
      tap(p => this.lastPage$.next(p)),
    );
  }

  updateCountries$(id: string, name: string, countries: string[]): Observable<Territory | null> {
    const idx = TERRITORIES.findIndex(t => t.id === id);
    if (idx === -1) return of(null).pipe(delay(300));
    TERRITORIES[idx] = { ...TERRITORIES[idx], name, countries, updatedAt: new Date().toISOString() };
    return of({ ...TERRITORIES[idx] }).pipe(delay(400));
  }

  create$(req: CreateTerritoryRequest): Observable<Territory> {
    const id = `TERR-${String(TERRITORIES.length + 1).padStart(3, '0')}`;
    const manager = req.assignedManagerId
      ? TERRITORY_MANAGERS.find(m => m.id === req.assignedManagerId) ?? null
      : null;
    const territory: Territory = {
      id,
      name: req.name,
      countries: req.countries,
      assignedManagerId: manager?.id ?? null,
      assignedManagerName: manager?.name ?? null,
      assignedManagerEmail: manager?.email ?? null,
      status: manager ? 'Active' : 'Unassigned',
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    };
    TERRITORIES.push(territory);
    if (manager) {
      const idx = TERRITORY_MANAGERS.findIndex(m => m.id === manager.id);
      if (idx !== -1) TERRITORY_MANAGERS[idx].assignedTerritoryIds.push(id);
    }
    return of(territory).pipe(delay(500));
  }

  assign$(territoryId: string, managerId: string | null): Observable<Territory | null> {
    const idx = TERRITORIES.findIndex(t => t.id === territoryId);
    if (idx === -1) return of(null).pipe(delay(300));

    const prev = TERRITORIES[idx];
    if (prev.assignedManagerId) {
      const mIdx = TERRITORY_MANAGERS.findIndex(m => m.id === prev.assignedManagerId);
      if (mIdx !== -1) {
        TERRITORY_MANAGERS[mIdx].assignedTerritoryIds =
          TERRITORY_MANAGERS[mIdx].assignedTerritoryIds.filter(id => id !== territoryId);
      }
    }

    const manager = managerId ? TERRITORY_MANAGERS.find(m => m.id === managerId) ?? null : null;
    TERRITORIES[idx] = {
      ...prev,
      assignedManagerId: manager?.id ?? null,
      assignedManagerName: manager?.name ?? null,
      assignedManagerEmail: manager?.email ?? null,
      status: manager ? 'Active' : 'Unassigned',
      updatedAt: new Date().toISOString(),
    };

    if (manager) {
      const mIdx = TERRITORY_MANAGERS.findIndex(m => m.id === manager.id);
      if (mIdx !== -1 && !TERRITORY_MANAGERS[mIdx].assignedTerritoryIds.includes(territoryId)) {
        TERRITORY_MANAGERS[mIdx].assignedTerritoryIds.push(territoryId);
      }
    }

    return of({ ...TERRITORIES[idx] }).pipe(delay(400));
  }

  delete$(id: string): Observable<void> {
    const idx = TERRITORIES.findIndex(t => t.id === id);
    if (idx !== -1) {
      const t = TERRITORIES[idx];
      if (t.assignedManagerId) {
        const mIdx = TERRITORY_MANAGERS.findIndex(m => m.id === t.assignedManagerId);
        if (mIdx !== -1) {
          TERRITORY_MANAGERS[mIdx].assignedTerritoryIds =
            TERRITORY_MANAGERS[mIdx].assignedTerritoryIds.filter(tid => tid !== id);
        }
      }
      TERRITORIES.splice(idx, 1);
    }
    return of(undefined).pipe(delay(400));
  }

  setManagerStatus$(id: string, status: 'Active' | 'Inactive'): Observable<void> {
    const idx = TERRITORY_MANAGERS.findIndex(m => m.id === id);
    if (idx !== -1) TERRITORY_MANAGERS[idx] = { ...TERRITORY_MANAGERS[idx], status };
    if (status === 'Inactive') {
      for (let i = 0; i < TERRITORIES.length; i++) {
        if (TERRITORIES[i].assignedManagerId === id) {
          TERRITORIES[i] = {
            ...TERRITORIES[i],
            assignedManagerId: null,
            assignedManagerName: null,
            assignedManagerEmail: null,
            status: 'Unassigned',
            updatedAt: new Date().toISOString(),
          };
        }
      }
      if (idx !== -1) TERRITORY_MANAGERS[idx] = { ...TERRITORY_MANAGERS[idx], assignedTerritoryIds: [] };
    }
    return of(undefined).pipe(delay(400));
  }

  getAssignedCountryCodes(): Set<string> {
    const codes = new Set<string>();
    for (const t of TERRITORIES) {
      for (const c of t.countries) codes.add(c);
    }
    return codes;
  }
}
