import { Component, effect, input, output, signal } from '@angular/core';
import { EXPLORE_IMAGE_FALLBACK } from '../../../../core/constants/explore-media.constants';
import { EmptyStateComponent } from '../../../../shared/components/empty-state/empty-state.component';
import type { ExploreNearbyItem } from '../../models/explore-extras.model';
import { SectionHeaderComponent } from '../../../../shared/components/section-header/section-header.component';
import { FavoriteButtonComponent } from '../../../../shared/components/favorite-button/favorite-button.component';

@Component({
  selector: 'app-popular-nearby-slider',
  standalone: true,
  imports: [SectionHeaderComponent, EmptyStateComponent, FavoriteButtonComponent],
  templateUrl: './popular-nearby-slider.component.html',
  styleUrl: './popular-nearby-slider.component.scss',
})
export class PopularNearbySliderComponent {
  readonly items = input.required<ExploreNearbyItem[]>();
  readonly showFavoriteButton = input(false);

  readonly bookmarkToggled = output<string>();
  readonly cardActivated   = output<string>();
  readonly sectionMore     = output<void>();

  protected readonly imgSrcById = signal<Record<string, string>>({});
  private readonly fallbackUsed = new Set<string>();

  constructor() {
    effect(() => {
      const next: Record<string, string> = {};
      for (const item of this.items()) {
        next[item.id] = item.image;
      }
      this.imgSrcById.set(next);
      this.fallbackUsed.clear();
    });
  }

  protected onImgError(id: string): void {
    if (this.fallbackUsed.has(id)) return;
    this.fallbackUsed.add(id);
    this.imgSrcById.update((m) => ({ ...m, [id]: EXPLORE_IMAGE_FALLBACK }));
  }

  protected onCardClick(id: string): void {
    this.cardActivated.emit(id);
  }

  protected toggleBookmark(id: string): void {
    this.bookmarkToggled.emit(id);
  }
}
