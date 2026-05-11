import { Component, inject, input } from '@angular/core';
import { Router } from '@angular/router';
import { AnalyticsService } from '../../../../core/services/analytics.service';
import { EXPLORE_IMAGE_FALLBACK } from '../../../../core/constants/explore-media.constants';
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
  imports: [ImageFallbackDirective, FavoriteButtonComponent],
  templateUrl: './agent-recommendation-card.component.html',
  styleUrl: './agent-recommendation-card.component.scss',
})
export class AgentRecommendationCardComponent {
  readonly card = input.required<AgentRecommendationCard>();
  private readonly router = inject(Router);
  private readonly analytics = inject(AnalyticsService);
  private readonly saveToCollection = inject(SaveToCollectionService);

  protected onCardClicked(): void {
    const c = this.card();
    if (!c.detailRoute) return;

    const entityType = this.analyticsEntityType(c);
    const entityId = Number(c.id);
    this.analytics.track('agent_recommendation_opened', {
      entityType,
      entityId: Number.isFinite(entityId) ? entityId : undefined,
    });
    void this.router.navigateByUrl(c.detailRoute);
  }

  protected canSave(): boolean {
    const c = this.card();
    return c.entityType === 'place' || c.entityType === 'stay' || c.entityType === 'event';
  }

  protected onSaveClicked(): void {
    const c = this.card();
    const entityId = Number(c.id);
    if (!Number.isFinite(entityId) || !this.canSave()) return;

    this.saveToCollection.open({
      entityType: c.entityType === 'event' ? 'event' : 'place',
      entityId,
      title: c.title,
      imageUrl: c.imageUrl ?? undefined,
      surface: 'agent',
      analyticsEntityType: this.saveAnalyticsEntityType(c),
    });
  }

  protected entityLabel(): string {
    const c = this.card();

    if (c.entityType === 'stay') {
      return 'Stay';
    }

    if (c.entityType === 'event') {
      return 'Event';
    }

    return 'Place';
  }

  protected imageSrc(): string {
    return this.card().imageUrl?.trim() || EXPLORE_IMAGE_FALLBACK;
  }

  protected imageAlt(): string {
    return this.card().title ? `${this.card().title} image` : 'GeoTrack result image';
  }

  protected description(): string | null {
    return this.card().description?.trim() || null;
  }

  protected subtitle(): string | null {
    return this.card().subtitle?.trim()
      || this.card().locationName?.trim()
      || this.card().placeType?.trim()
      || this.card().stayType?.trim()
      || this.card().dateText?.trim()
      || null;
  }

  protected reviewText(): string | null {
    const count = this.card().reviewCount;
    if (typeof count !== 'number' || count <= 0) {
      return null;
    }

    return count === 1 ? '1 review' : `${count} reviews`;
  }

  private analyticsEntityType(card: AgentRecommendationCard): AnalyticsEntityType {
    const normalized = this.saveAnalyticsEntityType(card);

    return ALLOWED_ENTITY_TYPES.has(normalized)
      ? normalized
      : 'agent_card';
  }

  private saveAnalyticsEntityType(card: AgentRecommendationCard): SaveAnalyticsEntityType {
    if (card.entityType === 'stay') {
      return 'hotel';
    }

    if (card.entityType === 'event') {
      return 'event';
    }

    return 'place';
  }
}
