import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, BehaviorSubject, combineLatest, map, shareReplay, tap } from 'rxjs';
import {
  Country, Territory, TerritoryManager,
  CreateTerritoryRequest, TerritoryPage, TerritoryPageRequest,
  GeoJsonFeature, GeoJsonGeometry, TerritoryDetail, TerritoryMapBoundaryFeatureCollection,
  TerritoryMapFeatureCollection, TerritoryMapFeatureProperties, TerritoryMapStatusCollection,
  TerritoryMapStatusItem,
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
  { code: 'XS', name: 'Somaliland', continent: 'Africa', flag: '🏴' },
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

@Injectable({ providedIn: 'root' })
export class TerritoryService {
  private readonly endpoint = '/api/admin/territories';
  private boundaryCache$: Observable<TerritoryMapBoundaryFeatureCollection> | null = null;
  private mapCache$: Observable<TerritoryMapFeatureCollection> | null = null;
  readonly lastPage$ = new BehaviorSubject<TerritoryPage | null>(null);

  constructor(private readonly http: HttpClient) {}

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
    const normalized = code?.trim().toUpperCase();
    return ALL_COUNTRIES.find(c => c.code === normalized || c.iso3 === normalized);
  }

  getAllManagers(): TerritoryManager[] {
    return [];
  }

  getActiveManagers(): TerritoryManager[] {
    return [];
  }

  getPage$(req: TerritoryPageRequest): Observable<TerritoryPage> {
    let params = new HttpParams()
      .set('page', String(Math.max(1, req.page)))
      .set('pageSize', String(Math.max(1, req.pageSize)))
      .set('sortField', req.sortField)
      .set('sortDir', req.sortDir);

    if (req.search?.trim()) {
      params = params.set('search', req.search.trim());
    }

    for (const code of req.countryFilter ?? []) {
      if (code?.trim()) {
        params = params.append('countryFilter', code.trim().toUpperCase());
      }
    }

    return this.http.get<TerritoryPage>(this.endpoint, { params }).pipe(
      map(page => ({
        items: (page.items ?? []).map(item => this.normalizeTerritory(item)),
        total: Number.isFinite(page.total) ? page.total : 0,
      })),
      tap(page => this.lastPage$.next(page)),
    );
  }

  getMap$(forceRefresh = false): Observable<TerritoryMapFeatureCollection> {
    if (!forceRefresh && this.mapCache$) {
      return this.mapCache$;
    }

    this.mapCache$ = combineLatest([
      this.getMapBoundaries$(forceRefresh),
      this.getMapStatus$(forceRefresh),
    ]).pipe(
      map(([boundaries, status]) => this.mergeMapCollection(boundaries, status)),
      shareReplay({ bufferSize: 1, refCount: false }),
    );

    return this.mapCache$;
  }

  refreshMap$(): Observable<TerritoryMapFeatureCollection> {
    this.mapCache$ = null;
    this.boundaryCache$ = null;
    return this.getMap$(true);
  }

  refreshMapStatus$(): Observable<TerritoryMapFeatureCollection> {
    this.mapCache$ = null;
    this.mapCache$ = combineLatest([
      this.getMapBoundaries$(),
      this.getMapStatus$(true),
    ]).pipe(
      map(([boundaries, status]) => this.mergeMapCollection(boundaries, status)),
      shareReplay({ bufferSize: 1, refCount: false }),
    );
    return this.mapCache$;
  }

  invalidateMapCache(): void {
    this.mapCache$ = null;
    this.boundaryCache$ = null;
  }

  getDetail$(territoryId: string | number): Observable<TerritoryDetail> {
    const id = this.toTerritoryId(territoryId);
    if (!id) {
      throw new Error('Territory id is invalid.');
    }

    return this.http
      .get<TerritoryDetail>(`${this.endpoint}/${id}`)
      .pipe(map(item => this.normalizeTerritoryDetail(item)));
  }

  updateCountries$(id: string, name: string, countries: string[], zone?: GeoJsonFeature | null): Observable<Territory | null> {
    const territoryId = this.toTerritoryId(id);
    if (!territoryId) {
      throw new Error('Territory id is invalid.');
    }

    const country = countries[0] ? this.getCountryByCode(countries[0]) : undefined;
    return this.http.put<Territory>(`${this.endpoint}/${territoryId}/manager`, {
      name,
      countryCode: country?.code ?? countries[0] ?? null,
      boundary: zone?.geometry ?? null,
    }).pipe(map(item => this.normalizeTerritory(item)));
  }

  create$(req: CreateTerritoryRequest): Observable<Territory> {
    const countryCode = req.countries[0] ?? null;
    return this.http.post<Territory>(this.endpoint, {
      name: req.name,
      countryCode,
      assignedManagerUserId: req.assignedManagerUserId ?? this.toUserId(req.assignedManagerId ?? null),
      boundary: req.boundary ?? req.zone?.geometry ?? null,
    }).pipe(map(item => this.normalizeTerritory(item)));
  }

  assign$(territoryId: string, managerId: string | null): Observable<Territory | null> {
    const id = this.toTerritoryId(territoryId);
    if (!id) {
      throw new Error('Territory id is invalid.');
    }

    return this.updateManager$(id, managerId).pipe(map(item => this.normalizeTerritory(item)));
  }

  updateManager$(territoryId: string | number, managerId: string | number | null): Observable<TerritoryDetail> {
    const id = this.toTerritoryId(territoryId);
    if (!id) {
      throw new Error('Territory id is invalid.');
    }

    return this.http.put<TerritoryDetail>(`${this.endpoint}/${id}/manager`, {
      managerUserId: this.toUserId(managerId),
    }).pipe(map(item => this.normalizeTerritoryDetail(item)));
  }

  delete$(id: string): Observable<void> {
    const territoryId = this.toTerritoryId(id);
    if (!territoryId) {
      throw new Error('Territory id is invalid.');
    }

    return this.http.delete<void>(`${this.endpoint}/${territoryId}`);
  }

  setManagerStatus$(id: string, status: 'Active' | 'Inactive'): Observable<void> {
    void id;
    void status;
    throw new Error('Manager activation is handled through user administration.');
  }

  addManager$(user: { id: string; firstName: string; lastName: string; email: string }): Observable<TerritoryManager> {
    const displayName = [user.firstName, user.lastName].filter(Boolean).join(' ') || user.email;
    return this.searchManagerCandidates$(user.email, 1).pipe(
      map(items => items[0] ?? {
        id: user.id,
        userId: this.toUserId(user.id) ?? 0,
        name: displayName,
        displayName,
        firstName: user.firstName,
        lastName: user.lastName,
        email: user.email,
        initials: this.buildInitials(displayName, user.email),
        avatarColor: this.avatarColorFor(user.id),
        assignedTerritoryIds: [],
        status: 'Active' as const,
        roles: [],
        assignedTerritories: [],
        isActive: true,
        canAssign: true,
      }),
    );
  }

  isAlreadyManager(userId: string): boolean {
    void userId;
    return false;
  }

  removeManager$(id: string): Observable<void> {
    void id;
    throw new Error('Manager removal is handled by unassigning territories.');
  }

  getAssignedCountryCodes(): Set<string> {
    const codes = new Set<string>();
    for (const t of this.lastPage$.value?.items ?? []) {
      for (const c of t.countries) codes.add(c);
    }
    return codes;
  }

  searchManagerCandidates$(query?: string | null, take = 20, territoryId?: number | null): Observable<TerritoryManager[]> {
    let params = new HttpParams().set('take', String(Math.max(1, take)));
    if (query?.trim()) {
      params = params.set('query', query.trim());
    }
    if (territoryId && territoryId > 0) {
      params = params.set('territoryId', String(territoryId));
    }

    return this.http
      .get<{ items: TerritoryManager[] }>(`${this.endpoint}/managers/search`, { params })
      .pipe(map(response => (response.items ?? []).map(item => this.normalizeManager(item))));
  }

  getAssignableManagers$(territoryId: string | number, query?: string | null, take = 20): Observable<TerritoryManager[]> {
    const id = this.toTerritoryId(territoryId);
    if (!id) {
      throw new Error('Territory id is invalid.');
    }

    let params = new HttpParams().set('take', String(Math.max(1, take)));
    if (query?.trim()) {
      params = params.set('query', query.trim());
    }

    return this.http
      .get<{ items: TerritoryManager[] }>(`${this.endpoint}/${id}/assignable-managers`, { params })
      .pipe(map(response => (response.items ?? []).map(item => this.normalizeManager(item))));
  }

  private normalizeTerritory(value: Territory): Territory {
    const territoryKind = this.normalizeTerritoryKind(value.territoryKind);
    const isDisputed = territoryKind === 'DisputedTerritory';
    const countryCode = isDisputed ? null : (value.iso2 ?? value.countryCode ?? value.countries?.[0] ?? null);
    const iso3 = value.iso3 ?? null;
    const territoryId = value.territoryId ?? this.toTerritoryId(value.id) ?? 0;
    const hasManager = !!(value.assignedManagerId || value.assignedManagerUserId);
    const status = isDisputed
      ? (value.status === 'DisputedAssigned' ? 'DisputedAssigned' : 'DisputedViewOnly')
      : value.status === 'Assigned' || value.status === 'Active'
        ? (hasManager ? 'Assigned' : 'Unassigned')
        : 'Unassigned';

    return {
      ...value,
      territoryId,
      countryId: value.countryId ?? territoryId,
      id: value.id || `TERR-${String(territoryId).padStart(3, '0')}`,
      displayName: value.displayName ?? value.name,
      internalReference: value.internalReference ?? value.id ?? `TERR-${String(territoryId).padStart(3, '0')}`,
      territoryKind,
      countryCode,
      iso2: countryCode,
      iso3: isDisputed ? null : iso3,
      countries: countryCode ? [countryCode] : [],
      assignedManagerId: value.assignedManagerId ?? (value.assignedManagerUserId ? String(value.assignedManagerUserId) : null),
      status,
      createdAt: value.createdAt,
      updatedAt: value.updatedAt ?? value.createdAt,
    };
  }

  private normalizeTerritoryDetail(value: TerritoryDetail): TerritoryDetail {
    const base = this.normalizeTerritory(value);
    const assignedManager = value.assignedManager
      ? {
          ...value.assignedManager,
          id: value.assignedManager.id || String(value.assignedManager.userId),
          name: value.assignedManager.name || value.assignedManager.displayName || value.assignedManager.email,
          displayName: value.assignedManager.displayName || value.assignedManager.name || value.assignedManager.email,
          initials: value.assignedManager.initials ||
            this.buildInitials(value.assignedManager.displayName || value.assignedManager.name || value.assignedManager.email, value.assignedManager.email),
          roles: value.assignedManager.roles ?? [],
        }
      : null;

    return {
      ...value,
      ...base,
      assignedManager,
      boundaryGeoJson: value.boundaryGeoJson ?? null,
      viewport: value.viewport ?? null,
      boundaryRequiredMessage: value.boundaryRequiredMessage ?? null,
      canAssign: base.territoryKind === 'DisputedTerritory' ? false : (value.canAssign ?? !!value.hasBoundary),
    };
  }

  private normalizeManager(value: TerritoryManager): TerritoryManager {
    const userId = value.userId ?? this.toUserId(value.id) ?? 0;
    const name = value.name || value.displayName || value.email;
    const assignedTerritoryIds = value.assignedTerritoryIds?.length
      ? value.assignedTerritoryIds
      : (value.assignedTerritories ?? []).map(item => `TERR-${String(item.territoryId).padStart(3, '0')}`);

    return {
      ...value,
      id: value.id || String(userId),
      userId,
      name,
      displayName: value.displayName ?? name,
      initials: value.initials || this.buildInitials(name, value.email),
      avatarColor: value.avatarColor || this.avatarColorFor(String(userId)),
      assignedTerritoryIds,
      roles: value.roles ?? [],
      assignedTerritories: value.assignedTerritories ?? [],
      status: value.status ?? 'Active',
      isActive: value.isActive ?? value.status !== 'Inactive',
      canAssign: value.canAssign ?? true,
    };
  }

  private getMapBoundaries$(forceRefresh = false): Observable<TerritoryMapBoundaryFeatureCollection> {
    if (!forceRefresh && this.boundaryCache$) {
      return this.boundaryCache$;
    }

    const options = forceRefresh ? this.noCacheOptions() : undefined;

    this.boundaryCache$ = this.http
      .get<TerritoryMapBoundaryFeatureCollection>(`${this.endpoint}/map/boundaries`, options)
      .pipe(
        map(collection => this.normalizeBoundaryCollection(collection)),
        shareReplay({ bufferSize: 1, refCount: false }),
      );

    return this.boundaryCache$;
  }

  private getMapStatus$(forceRefresh = false): Observable<TerritoryMapStatusCollection> {
    return this.http
      .get<TerritoryMapStatusCollection>(
        `${this.endpoint}/map/status`,
        forceRefresh ? this.noCacheOptions() : undefined,
      )
      .pipe(map(collection => this.normalizeStatusCollection(collection)));
  }

  private noCacheOptions(): { params: HttpParams; headers: HttpHeaders } {
    return {
      params: new HttpParams().set('_', String(Date.now())),
      headers: new HttpHeaders({ 'Cache-Control': 'no-cache' }),
    };
  }

  private mergeMapCollection(
    boundaries: TerritoryMapBoundaryFeatureCollection,
    status: TerritoryMapStatusCollection,
  ): TerritoryMapFeatureCollection {
    const statusByKey = new Map<string, TerritoryMapStatusItem>();
    for (const item of status.items ?? []) {
      statusByKey.set(this.mapStatusKey(item), item);
    }

    const usedStatusKeys = new Set<string>();
    const features = (boundaries.features ?? []).map(feature => {
      const key = this.mapBoundaryKey(feature.properties);
      const item = key ? statusByKey.get(key) : undefined;
      if (key && item) {
        usedStatusKeys.add(key);
      }

      const baseProps: TerritoryMapFeatureProperties = {
        territoryId: feature.properties.territoryId ?? item?.territoryId ?? null,
        countryId: feature.properties.countryId ?? item?.countryId ?? null,
        territoryKind: this.normalizeTerritoryKind(item?.territoryKind ?? feature.properties.territoryKind),
        iso2: item?.iso2 ?? feature.properties.iso2 ?? null,
        iso3: item?.iso3 ?? feature.properties.iso3 ?? null,
        countryCode: item?.countryCode ?? feature.properties.countryCode ?? null,
        name: item?.name ?? feature.properties.name,
        displayName: item?.displayName ?? feature.properties.displayName ?? feature.properties.name,
        status: item?.status ?? (feature.geometry ? 'Available' : 'MissingBoundary'),
        shapeGroup: item?.shapeGroup ?? feature.properties.shapeGroup ?? null,
        shapeType: item?.shapeType ?? feature.properties.shapeType ?? null,
        shapeId: item?.shapeId ?? feature.properties.shapeId ?? null,
        assignedManager: item?.assignedManager ?? null,
        hasBoundary: item?.hasBoundary ?? !!feature.geometry,
        hasValidationBoundary: item?.hasValidationBoundary ?? item?.hasBoundary ?? !!feature.geometry,
        hasDisplayBoundary: item?.hasDisplayBoundary ?? feature.properties.hasDisplayBoundary ?? !!feature.geometry,
        canAssign: item?.canAssign ?? false,
        canEdit: item?.canEdit ?? false,
        boundaryRequiredMessage: item?.boundaryRequiredMessage ?? null,
        boundaryMissingReason: item?.boundaryMissingReason ?? item?.boundaryRequiredMessage ?? null,
        viewport: feature.properties.viewport ?? null,
      };

      return {
        type: 'Feature' as const,
        geometry: feature.geometry,
        properties: this.normalizeMapProperties(baseProps),
      };
    });

    for (const item of status.items ?? []) {
      const key = this.mapStatusKey(item);
      if (!key || usedStatusKeys.has(key)) {
        continue;
      }

      features.push({
        type: 'Feature',
        geometry: null,
        properties: this.normalizeMapProperties({
          ...item,
          viewport: null,
        }),
      });
    }

    return {
      type: 'FeatureCollection',
      features,
      attribution: boundaries.attribution ?? null,
      generatedAt: status.generatedAt ?? boundaries.generatedAt ?? null,
    };
  }

  private normalizeBoundaryCollection(
    collection: TerritoryMapBoundaryFeatureCollection,
  ): TerritoryMapBoundaryFeatureCollection {
    return {
      type: 'FeatureCollection',
      features: (collection.features ?? []).map(feature => {
        const territoryKind = this.normalizeTerritoryKind(feature.properties.territoryKind);
        return {
          ...feature,
          properties: {
            ...feature.properties,
            territoryKind,
            iso2: territoryKind === 'DisputedTerritory' ? null : feature.properties.iso2 ?? null,
            iso3: territoryKind === 'DisputedTerritory' ? null : feature.properties.iso3 ?? null,
            countryCode: territoryKind === 'DisputedTerritory' ? null : feature.properties.countryCode ?? null,
          },
        };
      }),
      attribution: collection.attribution ?? null,
      generatedAt: collection.generatedAt ?? null,
    };
  }

  private normalizeStatusCollection(collection: TerritoryMapStatusCollection): TerritoryMapStatusCollection {
    return {
      items: (collection.items ?? []).map(item => this.normalizeStatusItem(item)),
      generatedAt: collection.generatedAt ?? null,
    };
  }

  private normalizeStatusItem(item: TerritoryMapStatusItem): TerritoryMapStatusItem {
    const territoryKind = this.normalizeTerritoryKind(item.territoryKind);
    return {
      ...item,
      territoryKind,
      iso2: territoryKind === 'DisputedTerritory' ? null : item.iso2 ?? null,
      iso3: territoryKind === 'DisputedTerritory' ? null : item.iso3 ?? null,
      countryCode: territoryKind === 'DisputedTerritory' ? null : item.countryCode ?? null,
      assignedManager: item.assignedManager ?? null,
      hasValidationBoundary: item.hasValidationBoundary ?? item.hasBoundary,
      hasDisplayBoundary: item.hasDisplayBoundary ?? item.hasBoundary,
      boundaryRequiredMessage: item.boundaryRequiredMessage ?? null,
      boundaryMissingReason: item.boundaryMissingReason ?? item.boundaryRequiredMessage ?? null,
    };
  }

  private normalizeMapCollection(collection: TerritoryMapFeatureCollection): TerritoryMapFeatureCollection {
    return {
      type: 'FeatureCollection',
      features: (collection.features ?? []).map(feature => ({
        ...feature,
        properties: this.normalizeMapProperties(feature.properties),
      })),
      attribution: collection.attribution ?? null,
      generatedAt: collection.generatedAt ?? null,
    };
  }

  private normalizeMapProperties(properties: TerritoryMapFeatureProperties): TerritoryMapFeatureProperties {
    const territoryKind = this.normalizeTerritoryKind(properties.territoryKind);
    return {
      ...properties,
      territoryKind,
      iso2: territoryKind === 'DisputedTerritory' ? null : properties.iso2 ?? null,
      iso3: territoryKind === 'DisputedTerritory' ? null : properties.iso3 ?? null,
      countryCode: territoryKind === 'DisputedTerritory' ? null : properties.countryCode ?? null,
      assignedManager: properties.assignedManager ?? null,
      hasValidationBoundary: properties.hasValidationBoundary ?? properties.hasBoundary,
      hasDisplayBoundary: properties.hasDisplayBoundary ?? properties.hasBoundary,
      boundaryRequiredMessage: properties.boundaryRequiredMessage ?? null,
      boundaryMissingReason: properties.boundaryMissingReason ?? properties.boundaryRequiredMessage ?? null,
      viewport: properties.viewport ?? null,
    };
  }

  private mapBoundaryKey(properties: {
    territoryId?: number | null;
    iso2?: string | null;
    iso3?: string | null;
    countryCode?: string | null;
    shapeId?: string | null;
    shapeGroup?: string | null;
  }): string {
    return this.mapFeatureKey(
      properties.territoryId,
      properties.iso2 ?? properties.countryCode ?? null,
      properties.iso3 ?? null,
      properties.shapeId ?? null,
      properties.shapeGroup ?? null,
    );
  }

  private mapStatusKey(item: TerritoryMapStatusItem): string {
    return this.mapFeatureKey(
      item.territoryId,
      item.iso2 ?? item.countryCode ?? null,
      item.iso3 ?? null,
      item.shapeId ?? null,
      item.shapeGroup ?? null,
    );
  }

  private mapFeatureKey(
    territoryId: number | null | undefined,
    iso2: string | null,
    iso3: string | null,
    shapeId: string | null,
    shapeGroup: string | null,
  ): string {
    if (territoryId) return `territory:${territoryId}`;
    if (shapeId?.trim()) return `shape:${shapeId.trim().toUpperCase()}`;
    if (iso3?.trim()) return `iso3:${iso3.trim().toUpperCase()}`;
    if (iso2?.trim()) return `iso2:${iso2.trim().toUpperCase()}`;
    if (shapeGroup?.trim()) return `group:${shapeGroup.trim().toUpperCase()}`;
    return '';
  }

  private normalizeTerritoryKind(value: Territory['territoryKind']): 'Country' | 'DisputedTerritory' {
    return value === 'DisputedTerritory' ? 'DisputedTerritory' : 'Country';
  }

  private toTerritoryId(id: string | number | null | undefined): number | null {
    if (typeof id === 'number' && Number.isFinite(id)) return id;
    if (!id) return null;

    const direct = Number(id);
    if (Number.isFinite(direct) && direct > 0) return direct;

    const match = String(id).match(/(\d+)$/);
    const parsed = match ? Number(match[1]) : NaN;
    return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
  }

  private toUserId(id: string | number | null | undefined): number | null {
    if (typeof id === 'number' && Number.isFinite(id)) return id;
    if (!id) return null;

    const parsed = Number(id);
    return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
  }

  private avatarColorFor(seed: string): string {
    const colors = ['#3b82f6', '#10b981', '#8b5cf6', '#f59e0b', '#ef4444', '#06b6d4', '#84cc16'];
    const hash = Array.from(seed).reduce((acc, char) => acc + char.charCodeAt(0), 0);
    return colors[hash % colors.length];
  }

  private buildInitials(name: string, email: string): string {
    const source = name.trim() || email.trim();
    const parts = source.split(/\s+/).filter(Boolean);
    if (parts.length >= 2) {
      return `${parts[0][0] ?? ''}${parts[1][0] ?? ''}`.toUpperCase();
    }
    return (source.slice(0, 2) || '?').toUpperCase();
  }
}
