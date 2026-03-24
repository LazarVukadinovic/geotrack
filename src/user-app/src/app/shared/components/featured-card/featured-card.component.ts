import { Component, effect, input, output, signal } from '@angular/core';
import { EXPLORE_IMAGE_FALLBACK } from '../../../core/constants/explore-media.constants';
import type { ExploreItem } from '../../../core/models/explore-item.model';
import { categoryBadgeLabel } from '../../utils/explore-display';
import { IconButtonComponent } from '../icon-button/icon-button.component';

@Component({
  selector: 'app-featured-card',
  standalone: true,
  imports: [IconButtonComponent],
  templateUrl: './featured-card.component.html',
  styleUrl: './featured-card.component.scss',
})
export class FeaturedCardComponent {
  readonly item = input.required<ExploreItem>();

  readonly bookmarkToggled = output<string>();
  readonly moreClicked = output<string>();

  protected readonly badgeFor = categoryBadgeLabel;
  protected readonly imgSrc = signal<string>('');
  private imgFallbackUsed = false;

  constructor() {
    effect(() => {
      const url = this.item().image;
      this.imgSrc.set(url);
      this.imgFallbackUsed = false;
    });
  }

  protected onImageError(): void {
    if (this.imgFallbackUsed) {
      return;
    }
    this.imgFallbackUsed = true;
    this.imgSrc.set(EXPLORE_IMAGE_FALLBACK);
  }

  protected toggleBookmark(): void {
    this.bookmarkToggled.emit(this.item().id);
  }

  protected more(event: Event): void {
    event.stopPropagation();
    this.moreClicked.emit(this.item().id);
  }
}
