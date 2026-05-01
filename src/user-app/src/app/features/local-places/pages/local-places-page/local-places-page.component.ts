import {
  AfterViewInit,
  Component,
  effect,
  ElementRef,
  inject,
  signal,
} from '@angular/core';
import { Router } from '@angular/router';
import { LocalPlacesService } from '../../services/local-places.service';
import { LocalPlacesScrollStateService } from '../../services/local-places-scroll-state.service';
import { EXPLORE_IMAGE_FALLBACK } from '../../../../core/constants/explore-media.constants';
import { EmptyStateComponent } from '../../../../shared/components/empty-state/empty-state.component';
import { PageTopNavComponent } from '../../../../shared/components/page-top-nav/page-top-nav.component';

@Component({
  selector: 'app-local-places-page',
  standalone: true,
  imports: [EmptyStateComponent, PageTopNavComponent],
  templateUrl: './local-places-page.component.html',
  styleUrl: './local-places-page.component.scss',
})
export class LocalPlacesPageComponent implements AfterViewInit {
  private readonly service     = inject(LocalPlacesService);
  private readonly router      = inject(Router);
  private readonly scrollState = inject(LocalPlacesScrollStateService);
  private readonly elRef       = inject(ElementRef<HTMLElement>);

  protected readonly categories = this.service.categories;
  protected readonly loadError  = this.service.loadError;
  protected readonly loadMoreError = this.service.loadMoreError;
  protected readonly loading = this.service.loading;
  protected readonly loadingMore = this.service.loadingMore;
  protected readonly hasMore = this.service.hasMore;

  protected readonly imgSrcById = signal<Record<string, string>>({});
  private readonly fallbackUsed = new Set<string>();

  constructor() {
    effect(() => {
      const next: Record<string, string> = {};
      for (const cat of this.service.categories()) {
        next[cat.id] = cat.image;
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
        const scrollEl = host.querySelector<HTMLElement>('.lp-page__scroll');
        if (scrollEl && state.scrollTop > 0) {
          scrollEl.scrollTop = state.scrollTop;
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

  protected onTileClick(categoryKey: string): void {
    const host     = this.elRef.nativeElement as HTMLElement;
    const scrollEl = host.querySelector<HTMLElement>('.lp-page__scroll');
    this.scrollState.save(scrollEl?.scrollTop ?? 0);

    this.router.navigate(['/local-places', categoryKey]);
  }

  protected loadMore(): void {
    this.service.loadMore();
  }
}
