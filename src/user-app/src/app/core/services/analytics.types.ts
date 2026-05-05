/**
 * Canonical client-side mirror of the backend analytics taxonomy.
 * Stable strings — must match the C# constants in
 * `Geotrack.Application/Analytics/AnalyticsEventTypes.cs`. Adding a new event
 * type is safe; renaming an existing one breaks stored rows and admin queries.
 */
export type AnalyticsEventType =
  | 'page_view'
  | 'detail_view'
  | 'search_performed'
  | 'filter_applied'
  | 'map_marker_opened'
  | 'directions_started'
  | 'outbound_link_clicked'
  | 'favorite_added'
  | 'favorite_removed'
  | 'collection_created'
  | 'collection_item_added'
  | 'collection_item_removed'
  | 'review_created'
  | 'contribution_created'
  | 'contribution_updated'
  | 'contribution_deleted'
  | 'agent_prompt_sent'
  | 'agent_recommendation_opened'
  | 'login_completed'
  | 'register_completed'
  | 'interests_completed'
  | 'logout_completed';

export type AnalyticsEntityType =
  | 'place'
  | 'hotel'
  | 'event'
  | 'destination'
  | 'region'
  | 'collection'
  | 'agent_card';

/**
 * Caller-supplied payload for AnalyticsService.track(). Everything is
 * optional; the service enriches with route, destinationId, and timestamps.
 */
export interface TrackPayload {
  entityType?: AnalyticsEntityType;
  entityId?: number;
  destinationId?: number;
  /**
   * Free-form key/value bag persisted as JSONB on the server. Keep small —
   * payload over ~4KB serialized is dropped by the server.
   */
  metadata?: Record<string, unknown>;
}

/**
 * Wire shape sent to POST /api/analytics/events. Server overrides UserId from
 * the JWT (never trusted from client) and OccurredAt (server time).
 */
export interface AnalyticsEventEnvelope {
  eventType: AnalyticsEventType;
  entityType?: AnalyticsEntityType;
  entityId?: number;
  destinationId?: number;
  route?: string;
  metadata?: Record<string, unknown>;
  clientOccurredAt: string;
}

export interface AnalyticsEventBatch {
  anonymousSessionId: string;
  events: AnalyticsEventEnvelope[];
}
