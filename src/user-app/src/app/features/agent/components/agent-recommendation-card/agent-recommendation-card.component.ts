import { Component, input, output, signal } from '@angular/core';
import type { AgentRecommendationCard } from '../../models/agent.model';

@Component({
  selector: 'app-agent-recommendation-card',
  standalone: true,
  imports: [],
  templateUrl: './agent-recommendation-card.component.html',
  styleUrl: './agent-recommendation-card.component.scss',
})
export class AgentRecommendationCardComponent {
  readonly card = input.required<AgentRecommendationCard>();
  readonly favoriteToggled = output<string>();
  readonly viewActivated = output<string>();

  protected readonly isFavorite = signal(false);

  protected onFavoriteToggle(): void {
    this.isFavorite.update(v => !v);
    this.favoriteToggled.emit(this.card().id);
  }

  protected onView(): void {
    this.viewActivated.emit(this.card().id);
  }
}
