import { CommonModule } from '@angular/common';
import { Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import {
  DestinationDiscoveryDestinationTypeFilter,
  DestinationDiscoveryService,
  DestinationDiscoverySearchItem,
  DestinationDiscoveryTagTypeFilter,
} from '../../services/destination-discovery.service';
import { EXPLORE_IMAGE_FALLBACK } from '../../../../core/constants/explore-media.constants';

const SEARCH_DEBOUNCE_MS = 220;
const PAGE_SIZE = 20;
const MIN_SEARCH_LENGTH = 2;

@Component({
  selector: 'app-destination-discovery-page',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './destination-discovery-page.component.html',
  styleUrl: './destination-discovery-page.component.scss',
})
export class DestinationDiscoveryPageComponent implements OnDestroy {
  private readonly discovery = inject(DestinationDiscoveryService);
  private readonly router = inject(Router);

  protected readonly imageFallback = EXPLORE_IMAGE_FALLBACK;
  protected readonly query = signal('');
  protected readonly items = signal<DestinationDiscoverySearchItem[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly loading = signal(false);
  protected readonly loadingMore = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly hasMore = signal(false);
  protected readonly filtersLoading = signal(false);
  protected readonly filtersError = signal<string | null>(null);
  protected readonly destinationTypeFilters = signal<DestinationDiscoveryDestinationTypeFilter[]>([]);
  protected readonly tagTypeFilters = signal<DestinationDiscoveryTagTypeFilter[]>([]);
  protected readonly selectedDestinationTypeIds = signal<Set<number>>(new Set());
  protected readonly selectedTagIds = signal<Set<number>>(new Set());

  protected readonly visibleDestinationTypeFilters = computed(() =>
    this.destinationTypeFilters().filter(type => type.count > 0),
  );

  protected readonly visibleTagTypeFilters = computed(() =>
    this.tagTypeFilters().filter(group => group.options.length > 0),
  );

  protected readonly hasActiveFilters = computed(() =>
    this.query().trim().length > 0 ||
    this.selectedDestinationTypeIds().size > 0 ||
    this.selectedTagIds().size > 0,
  );

  protected readonly resultSummary = computed(() => {
    if (this.loading() && this.items().length === 0) return 'Searching destinations...';
    const count = this.totalCount();
    if (count === 0) return 'No destinations found';
    if (count === 1) return '1 destination found';
    return `${count} destinations found`;
  });

  private nextSkip = 0;
  private searchTimer?: ReturnType<typeof setTimeout>;
  private requestVersion = 0;

  constructor() {
    this.loadFilters();
    this.runSearch(0, false);
  }

  ngOnDestroy(): void {
    if (this.searchTimer) {
      clearTimeout(this.searchTimer);
    }
  }

  protected onQueryInput(value: string): void {
    this.query.set(value);

    if (this.searchTimer) {
      clearTimeout(this.searchTimer);
    }

    const trimmed = value.trim();
    if (trimmed.length > 0 && trimmed.length < MIN_SEARCH_LENGTH) {
      this.items.set([]);
      this.totalCount.set(0);
      this.hasMore.set(false);
      this.loadError.set(null);
      return;
    }

    this.searchTimer = setTimeout(() => this.runSearch(0, false), SEARCH_DEBOUNCE_MS);
  }

  protected submitSearch(): void {
    if (this.searchTimer) {
      clearTimeout(this.searchTimer);
    }

    this.runSearch(0, false);
  }

  protected clearQuery(): void {
    this.query.set('');
    this.runSearch(0, false);
  }

  protected toggleDestinationType(destinationTypeId: number): void {
    this.selectedDestinationTypeIds.update(current => this.toggleSetValue(current, destinationTypeId));
    this.runSearch(0, false);
  }

  protected toggleTag(tagId: number): void {
    this.selectedTagIds.update(current => this.toggleSetValue(current, tagId));
    this.runSearch(0, false);
  }

  protected clearFilters(): void {
    this.query.set('');
    this.selectedDestinationTypeIds.set(new Set());
    this.selectedTagIds.set(new Set());
    this.runSearch(0, false);
  }

  protected loadMore(): void {
    if (this.loadingMore() || this.loading()) return;
    this.runSearch(this.nextSkip, true);
  }

  protected openDestination(destination: DestinationDiscoverySearchItem): void {
    void this.router.navigateByUrl(destination.detailRoute || `/explore?destinationId=${destination.id}`);
  }

  protected goBack(): void {
    const navigationId = Number(globalThis.history?.state?.navigationId ?? 0);
    if (navigationId > 1) {
      globalThis.history.back();
      return;
    }

    void this.router.navigateByUrl('/explore');
  }

  protected isDestinationTypeSelected(destinationTypeId: number): boolean {
    return this.selectedDestinationTypeIds().has(destinationTypeId);
  }

  protected isTagSelected(tagId: number): boolean {
    return this.selectedTagIds().has(tagId);
  }

  private loadFilters(): void {
    this.filtersLoading.set(true);
    this.filtersError.set(null);

    this.discovery.getFilters().subscribe({
      next: (filters) => {
        this.destinationTypeFilters.set(filters.destinationTypeFilters ?? []);
        this.tagTypeFilters.set(filters.tagTypeFilters ?? []);
        this.filtersLoading.set(false);
      },
      error: () => {
        this.filtersError.set('Could not load destination filters.');
        this.filtersLoading.set(false);
      },
    });
  }

  private runSearch(skip: number, append: boolean): void {
    const trimmed = this.query().trim();
    if (trimmed.length > 0 && trimmed.length < MIN_SEARCH_LENGTH) return;

    const requestVersion = ++this.requestVersion;
    this.loadError.set(null);

    if (append) {
      this.loadingMore.set(true);
    } else {
      this.loading.set(true);
      this.nextSkip = 0;
    }

    this.discovery.search({
      query: trimmed,
      destinationTypeIds: [...this.selectedDestinationTypeIds()],
      tagIds: [...this.selectedTagIds()],
      skip,
      take: PAGE_SIZE,
    }).subscribe({
      next: (response) => {
        if (requestVersion !== this.requestVersion) return;

        this.items.set(append ? [...this.items(), ...response.items] : response.items);
        this.totalCount.set(response.totalCount);
        this.nextSkip = response.nextSkip;
        this.hasMore.set(response.hasMore);
        this.loading.set(false);
        this.loadingMore.set(false);
      },
      error: () => {
        if (requestVersion !== this.requestVersion) return;

        if (!append) {
          this.items.set([]);
          this.totalCount.set(0);
          this.hasMore.set(false);
        }

        this.loadError.set('Could not search destinations. Try again.');
        this.loading.set(false);
        this.loadingMore.set(false);
      },
    });
  }

  private toggleSetValue(current: Set<number>, value: number): Set<number> {
    const next = new Set(current);
    if (next.has(value)) {
      next.delete(value);
    } else {
      next.add(value);
    }

    return next;
  }
}
