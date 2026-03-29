import { Component, input, output } from '@angular/core';
import type { ExplorePromoCard } from '../../models/explore-extras.model';

@Component({
  selector: 'app-promo-card',
  standalone: true,
  templateUrl: './promo-card.component.html',
  styleUrl: './promo-card.component.scss',
})
export class PromoCardComponent {
  readonly promo = input.required<ExplorePromoCard>();
  readonly cta = output<void>();
}

