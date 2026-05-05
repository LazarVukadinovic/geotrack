import {
  AfterViewInit,
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  signal,
} from '@angular/core';
import { Router } from '@angular/router';
import { FoodMusicService } from '../../services/food-music.service';
import { FoodMusicScrollStateService } from '../../services/food-music-scroll-state.service';
import { FOOD_MUSIC_FILTERS } from '../../models/food-music.model';
import { EXPLORE_IMAGE_FALLBACK } from '../../../../core/constants/explore-media.constants';
import { AnalyticsService } from '../../../../core/services/analytics.service';
import { EmptyStateComponent } from '../../../../shared/components/empty-state/empty-state.component';
import { PageTopNavComponent } from '../../../../shared/components/page-top-nav/page-top-nav.component';

@Component({
  selector: 'app-food-music-page',
  standalone: true,
  imports: [EmptyStateComponent, PageTopNavComponent],
  templateUrl: './food-music-page.component.html',
  styleUrl: './food-music-page.component.scss',
})
export class FoodMusicPageComponent implements AfterViewInit {
  private readonly service     = inject(FoodMusicService);
  private readonly router      = inject(Router);
  private readonly scrollState = inject(FoodMusicScrollStateService);
  private readonly elRef       = inject(ElementRef<HTMLElement>);
  private readonly analytics   = inject(AnalyticsService);

  protected readonly filters = FOOD_MUSIC_FILTERS;
  protected readonly activeFilter = signal('all');
  protected readonly loadError    = this.service.loadError;
  protected readonly loadMoreError = this.service.loadMoreError;
  protected readonly loading = this.service.loading;
  protected readonly loadingMore = this.service.loadingMore;
  protected readonly hasMore = this.service.hasMore;

  protected readonly filteredItems = computed(() => this.service.items());

  protected readonly imgSrcById = signal<Record<string, string>>({});
  private readonly fallbackUsed = new Set<string>();

  constructor() {
    effect(() => {
      const next: Record<string, string> = {};
      for (const item of this.service.items()) {
        next[item.id] = item.image;
      }
      this.imgSrcById.set(next);
      this.fallbackUsed.clear();
    });
  }

  // ── Scroll restoration ──────────────────────────────────────────────────────

  ngAfterViewInit(): void {
    const state = this.scrollState.pop();
    if (!state) return;

    const host = this.elRef.nativeElement as HTMLElement;

    requestAnimationFrame(() => {
      requestAnimationFrame(() => {
        const scrollEl  = host.querySelector<HTMLElement>('.fm-page__scroll');
        const filtersEl = host.querySelector<HTMLElement>('.fm-page__filters');

        if (scrollEl) {
          if (state.clickedItemId) {
            const card = scrollEl.querySelector<HTMLElement>(
              `[data-fm-id="${state.clickedItemId}"]`,
            );
            if (card) {
              card.scrollIntoView({ block: 'nearest', behavior: 'instant' });
            } else if (state.scrollTop > 0) {
              scrollEl.scrollTop = state.scrollTop;
            }
          } else if (state.scrollTop > 0) {
            scrollEl.scrollTop = state.scrollTop;
          }
        }

        if (filtersEl && state.pillsLeft > 0) {
          filtersEl.scrollLeft = state.pillsLeft;
        }
      });
    });
  }

  // ── Interactions ────────────────────────────────────────────────────────────

  protected onImgError(id: string): void {
    if (this.fallbackUsed.has(id)) return;
    this.fallbackUsed.add(id);
    this.imgSrcById.update((m) => ({ ...m, [id]: EXPLORE_IMAGE_FALLBACK }));
  }

  protected setFilter(id: string): void {
    if (this.activeFilter() === id) return;
    this.activeFilter.set(id);
    this.service.setFilter(id);
    this.analytics.track('filter_applied', {
      entityType: 'place',
      metadata: { surface: 'food_music_listing', filter: id },
    });
  }

  protected toggleSaved(event: Event, id: string): void {
    event.stopPropagation();
    this.service.toggleSaved(id);
  }

  protected onItemCard(id: string): void {
    // Save state synchronously before navigation — guaranteed stable DOM.
    const host      = this.elRef.nativeElement as HTMLElement;
    const scrollEl  = host.querySelector<HTMLElement>('.fm-page__scroll');
    const filtersEl = host.querySelector<HTMLElement>('.fm-page__filters');

    this.scrollState.save(
      scrollEl?.scrollTop  ?? 0,
      filtersEl?.scrollLeft ?? 0,
      id,
    );

    // Food & Music items are place-type content — same route as Explore card clicks.
    this.router.navigate(['/places', id]);
  }

  protected loadMore(): void {
    this.service.loadMore();
  }
}
