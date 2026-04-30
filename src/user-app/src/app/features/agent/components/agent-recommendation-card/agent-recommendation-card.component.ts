import { Component, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AnalyticsService } from '../../../../core/services/analytics.service';
import { SaveToCollectionService } from '../../../../core/services/save-to-collection.service';
import { ImageFallbackDirective } from '../../../../shared/directives/image-fallback.directive';
import { FavoriteButtonComponent } from '../../../../shared/components/favorite-button/favorite-button.component';
import type { AnalyticsEntityType } from '../../../../core/services/analytics.types';
import type { SaveAnalyticsEntityType } from '../../../../core/services/save-to-collection.service';
import type { AgentRecommendationCard } from '../../models/agent.model';

const ALLOWED_ENTITY_TYPES = new Set<AnalyticsEntityType>([
  'place', 'hotel', 'event', 'destination', 'region', 'collection', 'agent_card',
]);

@Component({
  selector: 'app-agent-recommendation-card',
  standalone: true,
  imports: [RouterLink, ImageFallbackDirective, FavoriteButtonComponent],
  templateUrl: './agent-recommendation-card.component.html',
  styleUrl: './agent-recommendation-card.component.scss',
})
export class AgentRecommendationCardComponent {
  readonly card = input.required<AgentRecommendationCard>();
  private readonly analytics = inject(AnalyticsService);
  private readonly saveToCollection = inject(SaveToCollectionService);

  protected onCardClicked(): void {
    const c = this.card();
    const entityType = this.analyticsEntityType(c);
    const entityId = parseInt(c.id, 10);
    this.analytics.track('agent_recommendation_opened', {
      entityType,
      entityId: isNaN(entityId) ? undefined : entityId,
    });
  }

  protected canSave(): boolean {
    const c = this.card();
    return c.entityType === 'place' || c.entityType === 'event';
  }

  protected onSaveClicked(): void {
    const c = this.card();
    const entityId = parseInt(c.id, 10);
    if (isNaN(entityId) || !this.canSave()) return;

    this.saveToCollection.open({
      entityType: c.entityType === 'event' ? 'event' : 'place',
      entityId,
      title: c.title,
      imageUrl: c.image,
      surface: 'agent',
      analyticsEntityType: this.saveAnalyticsEntityType(c),
    });
  }

  protected entityLabel(): string {
    const c = this.card();

    if (c.detailUrl?.startsWith('/stays/')) {
      return 'Stay';
    }

    if (c.entityType === 'event') {
      return 'Event';
    }

    if (c.entityType === 'destination') {
      return 'Destination';
    }

    return 'Place';
  }

  private analyticsEntityType(card: AgentRecommendationCard): AnalyticsEntityType {
    const normalized = this.saveAnalyticsEntityType(card);

    return ALLOWED_ENTITY_TYPES.has(normalized)
      ? normalized
      : 'agent_card';
  }

  private saveAnalyticsEntityType(card: AgentRecommendationCard): SaveAnalyticsEntityType {
    if (card.detailUrl?.startsWith('/stays/')) {
      return 'hotel';
    }

    if (card.entityType === 'event') {
      return 'event';
    }

    if (card.entityType === 'destination') {
      return 'destination';
    }

    return 'place';
  }
}
