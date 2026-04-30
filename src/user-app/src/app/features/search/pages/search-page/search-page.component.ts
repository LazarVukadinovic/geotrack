import { Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AppHeaderComponent } from '../../../../shared/components/app-header/app-header.component';
import { EmptyStateComponent } from '../../../../shared/components/empty-state/empty-state.component';
import { SearchBarComponent } from '../../../../shared/components/search-bar/search-bar.component';
import { EXPLORE_IMAGE_FALLBACK } from '../../../../core/constants/explore-media.constants';
import { AnalyticsService } from '../../../../core/services/analytics.service';
import type { AnalyticsEntityType } from '../../../../core/services/analytics.types';
import { DestinationService } from '../../../../core/services/destination.service';
import { LocationService } from '../../../../core/services/location.service';
import { ExploreService } from '../../../explore/services/explore.service';
import {
  GlobalSearchResponse,
  SEARCH_DOMAINS,
  SearchDomain,
  SearchFilterGroup,
  SearchFilterOption,
  SearchResultItem,
} from '../../models/search.model';
import { SearchService } from '../../services/search.service';

const MIN_QUERY_LENGTH = 2;
const RECENT_SEARCHES_KEY = 'geotrack.search.recent';
const MAX_RECENT_SEARCHES = 5;

interface SearchBrowseCategory {
  id: SearchDomain;
  title: string;
  subtitle: string;
  query: string;
  icon: 'destinations' | 'places' | 'stays' | 'events' | 'activities';
  iconBg: string;
  iconFg: string;
}

interface SearchTheme {
  id: string;
  label: string;
  query: string;
  domain: SearchDomain;
  icon: 'beach' | 'food' | 'stay' | 'music' | 'view';
  iconBg: string;
  iconFg: string;
  matchTerms: string[];
}

interface SearchSuggestion {
  id: string;
  label: string;
  detail: string;
  query: string;
  domain?: SearchDomain | null;
  source: 'recent' | 'category' | 'theme' | 'metadata';
}

@Component({
  selector: 'app-search-page',
  standalone: true,
  imports: [AppHeaderComponent, EmptyStateComponent, SearchBarComponent],
  templateUrl: './search-page.component.html',
  styleUrl: './search-page.component.scss',
})
export class SearchPageComponent implements OnDestroy {
  private readonly searchService = inject(SearchService);
  private readonly locationSvc = inject(LocationService);
  private readonly destinationSvc = inject(DestinationService);
  private readonly exploreSvc = inject(ExploreService);
  private readonly analytics = inject(AnalyticsService);
  private readonly router = inject(Router);

  protected readonly domains = SEARCH_DOMAINS;
  protected readonly query = signal('');
  protected readonly activeDomains = signal<SearchDomain[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly metadataGroups = signal<SearchFilterGroup[]>([]);
  protected readonly response = signal<GlobalSearchResponse | null>(null);
  protected readonly imgSrcByKey = signal<Record<string, string>>({});
  protected readonly searchFocused = signal(false);
  protected readonly recentSearches = signal<string[]>(this.loadRecentSearches());

  protected readonly browseCategories: SearchBrowseCategory[] = [
    {
      id: 'places',
      title: 'Places',
      subtitle: 'Restaurants, beaches, landmarks',
      query: 'places',
      icon: 'places',
      iconBg: '#e0f2fe',
      iconFg: '#0369a1',
    },
    {
      id: 'stays',
      title: 'Stays',
      subtitle: 'Hotels, villas, apartments',
      query: 'stays',
      icon: 'stays',
      iconBg: '#ede9fe',
      iconFg: '#6d28d9',
    },
    {
      id: 'events',
      title: 'Events',
      subtitle: 'Concerts, festivals, markets',
      query: 'events',
      icon: 'events',
      iconBg: '#ffedd5',
      iconFg: '#c2410c',
    },
    {
      id: 'activities',
      title: 'Activities',
      subtitle: 'Tours, outdoor, experiences',
      query: 'activities',
      icon: 'activities',
      iconBg: '#dcfce7',
      iconFg: '#047857',
    },
    {
      id: 'destinations',
      title: 'Destinations',
      subtitle: 'Cities and regions to explore',
      query: 'destinations',
      icon: 'destinations',
      iconBg: '#e0f2fe',
      iconFg: '#0369a1',
    },
  ];

  private readonly themeSeeds: SearchTheme[] = [
    {
      id: 'beaches',
      label: 'Beaches',
      query: 'beaches',
      domain: 'places',
      icon: 'beach',
      iconBg: '#e0f2fe',
      iconFg: '#0369a1',
      matchTerms: ['beach', 'beaches'],
    },
    {
      id: 'restaurants',
      label: 'Restaurants',
      query: 'restaurants',
      domain: 'places',
      icon: 'food',
      iconBg: '#ffedd5',
      iconFg: '#c2410c',
      matchTerms: ['restaurant', 'restaurants', 'food drink', 'fine dining'],
    },
    {
      id: 'luxury-stays',
      label: 'Luxury stays',
      query: 'luxury stays',
      domain: 'stays',
      icon: 'stay',
      iconBg: '#ede9fe',
      iconFg: '#6d28d9',
      matchTerms: ['luxury', 'boutique', 'sea view'],
    },
    {
      id: 'live-music',
      label: 'Live music',
      query: 'live music',
      domain: 'events',
      icon: 'music',
      iconBg: '#dcfce7',
      iconFg: '#047857',
      matchTerms: ['live music', 'nightlife', 'concert'],
    },
    {
      id: 'scenic-views',
      label: 'Scenic views',
      query: 'scenic views',
      domain: 'activities',
      icon: 'view',
      iconBg: '#fef3c7',
      iconFg: '#a16207',
      matchTerms: ['scenic views', 'scenic walks'],
    },
  ];

  private readonly fallbackUsed = new Set<string>();
  private searchTimeout?: ReturnType<typeof setTimeout>;
  private requestVersion = 0;

  protected readonly topResult = computed(() => this.response()?.topResult ?? null);
  protected readonly resultGroups = computed(() => this.response()?.groups ?? []);
  protected readonly totalCount = computed(() => this.response()?.totalCount ?? 0);
  protected readonly hasSearched = computed(() => this.query().trim().length >= MIN_QUERY_LENGTH);
  protected readonly showLanding = computed(() =>
    !this.hasSearched() && !this.searchFocused() && this.query().trim().length === 0);
  protected readonly showAssisted = computed(() =>
    !this.hasSearched() && (this.searchFocused() || this.query().trim().length > 0));
  protected readonly showEmptyResults = computed(() =>
    this.hasSearched() && !this.loading() && !this.error() && this.totalCount() === 0);
  protected readonly resultSummary = computed(() => {
    const count = this.totalCount();
    if (this.loading()) return 'Searching GeoTrack';
    if (count === 1) return '1 match';
    return `${count} matches`;
  });

  protected readonly availableThemes = computed(() => {
    const options = this.metadataGroups()
      .filter(group => group.key !== 'domains')
      .flatMap(group => group.options);

    if (options.length === 0) {
      return [];
    }

    return this.themeSeeds
      .filter(theme => options.some(option => {
        const haystack = this.normalizeText(`${option.id} ${option.label}`);
        return theme.matchTerms.some(term => haystack.includes(this.normalizeText(term)));
      }))
      .slice(0, 5);
  });

  protected readonly suggestions = computed(() => {
    const text = this.normalizeText(this.query());
    const suggestions: SearchSuggestion[] = [];

    for (const recent of this.recentSearches()) {
      if (!text || this.normalizeText(recent).includes(text)) {
        this.addSuggestion(suggestions, {
          id: `recent:${recent}`,
          label: recent,
          detail: 'Recent search',
          query: recent,
          source: 'recent',
        });
      }
    }

    for (const category of this.browseCategories) {
      const haystack = this.normalizeText(`${category.title} ${category.subtitle} ${category.query}`);
      if (!text || haystack.includes(text)) {
        this.addSuggestion(suggestions, {
          id: `category:${category.id}`,
          label: category.title,
          detail: category.subtitle,
          query: category.query,
          domain: category.id,
          source: 'category',
        });
      }
    }

    for (const theme of this.availableThemes()) {
      const haystack = this.normalizeText(`${theme.label} ${theme.query} ${theme.matchTerms.join(' ')}`);
      if (!text || haystack.includes(text)) {
        this.addSuggestion(suggestions, {
          id: `theme:${theme.id}`,
          label: theme.label,
          detail: this.domainLabel(theme.domain),
          query: theme.query,
          domain: theme.domain,
          source: 'theme',
        });
      }
    }

    if (text) {
      for (const group of this.metadataGroups().filter(group => group.key !== 'domains')) {
        for (const option of group.options) {
          const haystack = this.normalizeText(`${option.id} ${option.label}`);
          if (!haystack.includes(text)) {
            continue;
          }

          this.addSuggestion(suggestions, {
            id: `metadata:${option.id}`,
            label: option.label,
            detail: group.label,
            query: option.label,
            domain: this.isSearchDomain(option.domain) ? option.domain : null,
            source: 'metadata',
          });
        }
      }
    }

    return suggestions.slice(0, 8);
  });

  constructor() {
    this.searchService.metadata().subscribe({
      next: (metadata) => {
        this.metadataGroups.set(metadata.filterGroups);
      },
      error: () => {
        this.metadataGroups.set([]);
      },
    });
  }

  ngOnDestroy(): void {
    if (this.searchTimeout) {
      clearTimeout(this.searchTimeout);
    }
  }

  protected onSearchFocus(): void {
    this.searchFocused.set(true);
  }

  protected onSearchChange(value: string): void {
    this.query.set(value);
    this.scheduleSearch();
  }

  protected onSearchSubmit(value: string): void {
    const nextQuery = value.trim();
    if (nextQuery.length < MIN_QUERY_LENGTH) return;
    this.query.set(nextQuery);
    this.saveRecentSearch(nextQuery);
    this.scheduleSearch(0);
  }

  protected toggleDomain(domain: SearchDomain): void {
    this.activeDomains.update((current) =>
      current.includes(domain)
        ? current.filter(entry => entry !== domain)
        : [...current, domain]);
    this.scheduleSearch(0);
  }

  protected clearDomains(): void {
    if (this.activeDomains().length === 0) return;
    this.activeDomains.set([]);
    this.scheduleSearch(0);
  }

  protected isDomainActive(domain: SearchDomain): boolean {
    const active = this.activeDomains();
    return active.length === 0 || active.includes(domain);
  }

  protected applyCategory(category: SearchBrowseCategory): void {
    this.commitSearch(category.query, [category.id]);
  }

  protected applyTheme(theme: SearchTheme): void {
    this.commitSearch(theme.query, [theme.domain]);
  }

  protected applyRecentSearch(search: string): void {
    this.commitSearch(search, this.activeDomains());
  }

  protected applySuggestion(suggestion: SearchSuggestion): void {
    this.commitSearch(
      suggestion.query,
      suggestion.domain ? [suggestion.domain] : this.activeDomains());
  }

  protected applyDiscovery(option: SearchFilterOption): void {
    this.commitSearch(
      option.label,
      this.isSearchDomain(option.domain) ? [option.domain] : this.activeDomains());
  }

  protected clearRecentSearches(): void {
    this.recentSearches.set([]);
    try {
      localStorage.removeItem(RECENT_SEARCHES_KEY);
    } catch {
      // localStorage may be unavailable; ignore.
    }
  }

  protected openResult(result: SearchResultItem): void {
    const currentQuery = this.query().trim();
    if (currentQuery.length >= MIN_QUERY_LENGTH) {
      this.saveRecentSearch(currentQuery);
    }

    if (result.entityType === 'destination') {
      this.openDestinationInExplore(result);
      return;
    }

    if (result.route) {
      this.trackResultOpen(result);
      this.router.navigateByUrl(result.route);
      return;
    }

    this.commitSearch(result.title, []);
  }

  protected openDestinationInExplore(result: SearchResultItem): void {
    const destinationId = result.destinationId ?? result.id;
    if (destinationId <= 0) return;

    this.destinationSvc.setDestinationOverride({
      id: destinationId,
      name: result.destinationName ?? result.title,
      latitude: result.latitude ?? undefined,
      longitude: result.longitude ?? undefined,
    });

    this.trackResultOpen(result);
    void this.exploreSvc.loadForDestination(destinationId).finally(() => {
      this.router.navigate(['/explore'], { queryParams: { destinationId } });
    });
  }

  protected onImageError(result: SearchResultItem): void {
    const key = this.resultKey(result);
    if (this.fallbackUsed.has(key)) return;
    this.fallbackUsed.add(key);
    this.imgSrcByKey.update(current => ({ ...current, [key]: EXPLORE_IMAGE_FALLBACK }));
  }

  protected resultKey(result: SearchResultItem): string {
    return `${result.entityType}:${result.id}`;
  }

  protected entityLabel(result: SearchResultItem): string {
    switch (result.entityType) {
      case 'destination': return 'Destination';
      case 'stay': return 'Stay';
      case 'event': return 'Event';
      case 'activity': return result.categoryLabel ?? 'Activity';
      default: return result.categoryLabel ?? 'Place';
    }
  }

  protected domainLabel(domain: SearchDomain): string {
    return this.domains.find(entry => entry.id === domain)?.label ?? 'Results';
  }

  private commitSearch(query: string, domains: SearchDomain[] = []): void {
    const nextQuery = query.trim();
    if (nextQuery.length < MIN_QUERY_LENGTH) return;
    this.query.set(nextQuery);
    this.activeDomains.set(domains);
    this.saveRecentSearch(nextQuery);
    this.scheduleSearch(0);
  }

  private scheduleSearch(delayMs = 260): void {
    if (this.searchTimeout) {
      clearTimeout(this.searchTimeout);
    }

    if (this.query().trim().length < MIN_QUERY_LENGTH) {
      this.requestVersion++;
      this.loading.set(false);
      this.error.set(null);
      this.response.set(null);
      this.imgSrcByKey.set({});
      this.fallbackUsed.clear();
      return;
    }

    this.searchTimeout = setTimeout(() => this.runSearch(), delayMs);
  }

  private async runSearch(): Promise<void> {
    const q = this.query().trim();
    if (q.length < MIN_QUERY_LENGTH) return;

    const requestId = ++this.requestVersion;
    this.loading.set(true);
    this.error.set(null);
    this.response.set(null);
    this.imgSrcByKey.set({});

    const loc = this.locationSvc.location() ?? await this.locationSvc.ensureIpFallback();
    if (requestId !== this.requestVersion) return;

    this.searchService.search(q, this.activeDomains(), loc?.latitude, loc?.longitude).subscribe({
      next: (res) => {
        if (requestId !== this.requestVersion) return;
        this.response.set(res);
        this.refreshImages(res);
        this.loading.set(false);
        this.analytics.track('search_performed', {
          metadata: {
            surface: 'global_search',
            queryLength: q.length,
            domains: this.activeDomains(),
            resultCount: res.totalCount,
            destinationIntent: res.intent.destination?.name ?? null,
            activities: res.intent.activities,
          },
        });
      },
      error: () => {
        if (requestId !== this.requestVersion) return;
        this.response.set(null);
        this.loading.set(false);
        this.error.set("We couldn't search GeoTrack right now. Check your connection and try again.");
      },
    });
  }

  private refreshImages(res: GlobalSearchResponse): void {
    const next: Record<string, string> = {};
    const allResults = [
      ...(res.topResult ? [res.topResult] : []),
      ...res.groups.flatMap(group => group.items),
    ];

    for (const result of allResults) {
      next[this.resultKey(result)] = result.image || EXPLORE_IMAGE_FALLBACK;
    }

    this.imgSrcByKey.set(next);
    this.fallbackUsed.clear();
  }

  private trackResultOpen(result: SearchResultItem): void {
    const entityType = (result.entityType === 'stay' ? 'hotel' : result.entityType) as AnalyticsEntityType | 'activity';
    if (entityType === 'activity') return;
    this.analytics.track('search_performed', {
      entityType,
      entityId: result.id,
      destinationId: result.destinationId ?? undefined,
      metadata: {
        surface: 'global_search',
        committed: true,
        queryLength: this.query().trim().length,
        resultType: result.entityType,
      },
    });
  }

  private loadRecentSearches(): string[] {
    try {
      const raw = localStorage.getItem(RECENT_SEARCHES_KEY);
      if (!raw) return [];
      const parsed = JSON.parse(raw) as unknown;
      if (!Array.isArray(parsed)) return [];
      return parsed
        .filter((entry): entry is string => typeof entry === 'string' && entry.trim().length >= MIN_QUERY_LENGTH)
        .map(entry => entry.trim())
        .slice(0, MAX_RECENT_SEARCHES);
    } catch {
      return [];
    }
  }

  private saveRecentSearch(query: string): void {
    const trimmed = query.trim();
    if (trimmed.length < MIN_QUERY_LENGTH) return;

    const next = [
      trimmed,
      ...this.recentSearches().filter(entry => entry.toLowerCase() !== trimmed.toLowerCase()),
    ].slice(0, MAX_RECENT_SEARCHES);

    this.recentSearches.set(next);
    try {
      localStorage.setItem(RECENT_SEARCHES_KEY, JSON.stringify(next));
    } catch {
      // localStorage may be unavailable; ignore.
    }
  }

  private addSuggestion(suggestions: SearchSuggestion[], suggestion: SearchSuggestion): void {
    const key = `${this.normalizeText(suggestion.query)}:${suggestion.domain ?? 'all'}`;
    if (suggestions.some(entry => `${this.normalizeText(entry.query)}:${entry.domain ?? 'all'}` === key)) {
      return;
    }

    suggestions.push(suggestion);
  }

  private normalizeText(value: string): string {
    return value
      .trim()
      .toLowerCase()
      .normalize('NFD')
      .replace(/[\u0300-\u036f]/g, '')
      .replace(/[^a-z0-9]+/g, ' ')
      .replace(/\s+/g, ' ')
      .trim();
  }

  private isSearchDomain(value: unknown): value is SearchDomain {
    return value === 'destinations' ||
      value === 'places' ||
      value === 'stays' ||
      value === 'events' ||
      value === 'activities';
  }
}
