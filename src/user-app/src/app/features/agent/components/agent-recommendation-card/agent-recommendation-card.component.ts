import { Component, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AnalyticsService } from '../../../../core/services/analytics.service';
import type { AnalyticsEntityType } from '../../../../core/services/analytics.types';
import type { AgentRecommendationCard } from '../../models/agent.model';

const ALLOWED_ENTITY_TYPES = new Set<AnalyticsEntityType>([
  'place', 'hotel', 'event', 'destination', 'region', 'collection', 'agent_card',
]);

@Component({
  selector: 'app-agent-recommendation-card',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './agent-recommendation-card.component.html',
  styleUrl: './agent-recommendation-card.component.scss',
})
export class AgentRecommendationCardComponent {
  readonly card = input.required<AgentRecommendationCard>();
  private readonly analytics = inject(AnalyticsService);

  protected onCardClicked(): void {
    const c = this.card();
    const entityType = ALLOWED_ENTITY_TYPES.has(c.entityType as AnalyticsEntityType)
      ? (c.entityType as AnalyticsEntityType)
      : 'agent_card';
    const entityId = parseInt(c.id, 10);
    this.analytics.track('agent_recommendation_opened', {
      entityType,
      entityId: isNaN(entityId) ? undefined : entityId,
    });
  }
}
