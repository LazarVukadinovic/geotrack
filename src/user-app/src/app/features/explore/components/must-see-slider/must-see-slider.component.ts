import { Component, effect, input, output, signal } from '@angular/core';
import { EXPLORE_IMAGE_FALLBACK } from '../../../../core/constants/explore-media.constants';
import { EmptyStateComponent } from '../../../../shared/components/empty-state/empty-state.component';
import type { ExploreAttraction } from '../../models/explore-extras.model';
import { SectionHeaderComponent } from '../../../../shared/components/section-header/section-header.component';

@Component({
  selector: 'app-must-see-slider',
  standalone: true,
  imports: [SectionHeaderComponent, EmptyStateComponent],
  templateUrl: './must-see-slider.component.html',
  styleUrl: './must-see-slider.component.scss',
})
export class MustSeeSliderComponent {
  readonly items = input.required<ExploreAttraction[]>();

  readonly cardActivated = output<string>();
  readonly sectionMore   = output<void>();

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

  protected onCardClick(id: string): void {
    this.cardActivated.emit(id);
  }

  protected onImgError(id: string): void {
    if (this.fallbackUsed.has(id)) return;
    this.fallbackUsed.add(id);
    this.imgSrcById.update((m) => ({ ...m, [id]: EXPLORE_IMAGE_FALLBACK }));
  }
}
