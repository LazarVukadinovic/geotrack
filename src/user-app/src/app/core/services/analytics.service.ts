import { DestroyRef, Injectable, NgZone, computed, effect, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { catchError, of } from 'rxjs';
import { DestinationService } from './destination.service';
import {
  AnalyticsEventBatch,
  AnalyticsEventEnvelope,
  AnalyticsEventType,
  TrackPayload,
} from './analytics.types';

const SESSION_KEY = 'geotrack_anon_session_id';
const ENDPOINT = '/api/analytics/events';

const PERIODIC_FLUSH_INTERVAL_MS = 15_000;
const NORMAL_FLUSH_DELAY_MS = 2_500;
const SOON_FLUSH_DELAY_MS = 750;
const MAX_BATCH = 25;
/** Hard cap matching the server's MaxBatchSize. */
const SERVER_BATCH_CAP = 50;
const MAX_DEDUPE_ENTRIES = 200;

type FlushMode = 'normal' | 'soon' | 'immediate';

interface CollectionRule {
  cooldownMs?: number;
  coalescePending?: boolean;
  includeMetadata?: boolean;
  flush?: FlushMode;
}

const COLLECTION_RULES: Partial<Record<AnalyticsEventType, CollectionRule>> = {
  page_view: { cooldownMs: 30_000, coalescePending: true },
  detail_view: { cooldownMs: 120_000, coalescePending: true },
  map_marker_opened: { cooldownMs: 20_000, coalescePending: true },
  directions_started: { cooldownMs: 30_000, coalescePending: true, flush: 'soon' },
  search_performed: { cooldownMs: 15_000, coalescePending: true, includeMetadata: true, flush: 'soon' },
  filter_applied: { cooldownMs: 60_000, coalescePending: true, includeMetadata: true, flush: 'soon' },
  outbound_link_clicked: { flush: 'immediate' },
  logout_completed: { flush: 'immediate' },
  favorite_added: { flush: 'soon' },
  favorite_removed: { flush: 'soon' },
  collection_created: { flush: 'soon' },
  collection_item_added: { flush: 'soon' },
  collection_item_removed: { flush: 'soon' },
  review_created: { flush: 'soon' },
  contribution_created: { flush: 'soon' },
  contribution_updated: { flush: 'soon' },
  contribution_deleted: { flush: 'soon' },
  agent_recommendation_opened: { flush: 'soon' },
  login_completed: { flush: 'soon' },
  register_completed: { flush: 'soon' },
  interests_completed: { flush: 'soon' },
};

/**
 * First-party analytics tracker. Buffers events client-side, suppresses obvious
 * short-window duplicates, coalesces pending noisy events, and flushes to
 * POST /api/analytics/events when the queue is old enough, high-value events
 * arrive, the queue hits 25 events, or the page is hidden/unloaded. The auth
 * interceptor attaches the JWT automatically for authenticated users; anonymous
 * sessions carry a sessionStorage-bound UUID that dies with the tab.
 *
 * Failures are swallowed silently; analytics must never break the user flow.
 * Callers should never wrap track() in try/catch.
 */
@Injectable({ providedIn: 'root' })
export class AnalyticsService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly destinationService = inject(DestinationService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly zone = inject(NgZone);

  private readonly _queue = signal<AnalyticsEventEnvelope[]>([]);
  /** Exposed for tests; not used by production callers. */
  readonly queueSize = computed(() => this._queue().length);

  private readonly anonymousSessionId: string = this.initSessionId();
  private flushTimer: ReturnType<typeof setInterval> | null = null;
  private scheduledFlushTimer: ReturnType<typeof setTimeout> | null = null;
  private scheduledFlushDelayMs: number | null = null;
  private visibilityHandler: (() => void) | null = null;
  private pagehideHandler: (() => void) | null = null;
  private unloadHandler: (() => void) | null = null;
  private readonly recentFingerprints = new Map<string, number>();

  constructor() {
    effect(() => {
      if (this._queue().length >= MAX_BATCH) {
        this.flushNow();
      }
    });

    if (typeof window !== 'undefined') {
      this.zone.runOutsideAngular(() => {
        this.flushTimer = setInterval(() => this.flushNow(), PERIODIC_FLUSH_INTERVAL_MS);
      });

      this.visibilityHandler = () => {
        if (document.visibilityState === 'hidden') {
          this.flushNow();
        }
      };
      document.addEventListener('visibilitychange', this.visibilityHandler);

      // pagehide/beforeunload are last-ditch flushes. sendBeacon cannot attach
      // an Authorization header, so authenticated events flushed only here may
      // end up tagged anonymous-only. visibilitychange above is the primary
      // path for authenticated users.
      this.pagehideHandler = () => this.beaconFlush();
      window.addEventListener('pagehide', this.pagehideHandler);
      this.unloadHandler = () => this.beaconFlush();
      window.addEventListener('beforeunload', this.unloadHandler);
    }

    this.destroyRef.onDestroy(() => this.cleanup());
  }

  track(eventType: AnalyticsEventType, payload?: TrackPayload): void {
    const envelope: AnalyticsEventEnvelope = {
      eventType,
      entityType: payload?.entityType,
      entityId: payload?.entityId,
      destinationId: payload?.destinationId ?? this.destinationService.id() ?? undefined,
      route: this.normalizeRoute(this.router.url),
      metadata: payload?.metadata,
      clientOccurredAt: new Date().toISOString(),
    };

    const rule = COLLECTION_RULES[eventType];
    const includeMetadata = rule?.includeMetadata === true;
    const fingerprint = this.buildFingerprint(envelope, includeMetadata);
    const now = Date.now();

    if (rule?.cooldownMs && this.isWithinDedupeWindow(fingerprint, now, rule.cooldownMs)) {
      return;
    }

    if (rule?.cooldownMs) {
      this.rememberFingerprint(fingerprint, now);
    }

    this._queue.update((q) => {
      if (!rule?.coalescePending) {
        return [...q, envelope];
      }

      const existingIndex = q.findIndex((queued) =>
        this.buildFingerprint(queued, includeMetadata) === fingerprint);

      if (existingIndex < 0) {
        return [...q, envelope];
      }

      return q.map((queued, index) => index === existingIndex ? envelope : queued);
    });

    this.scheduleFlush(rule?.flush ?? 'normal');
  }

  trackOutboundLink(
    providerName: string | null | undefined,
    url: string | null | undefined,
    payload?: Omit<TrackPayload, 'metadata'> & { metadata?: Record<string, unknown> },
  ): void {
    this.track('outbound_link_clicked', {
      ...payload,
      metadata: {
        ...payload?.metadata,
        provider: this.normalizeProvider(providerName, url),
        host: this.extractHost(url),
      },
    });
  }

  flushNow(): void {
    this.clearScheduledFlush();

    const queued = this._queue();
    if (queued.length === 0) return;

    const toSend = queued.slice(0, SERVER_BATCH_CAP);
    const remaining = queued.slice(SERVER_BATCH_CAP);
    this._queue.set(remaining);

    const batch: AnalyticsEventBatch = {
      anonymousSessionId: this.anonymousSessionId,
      events: toSend,
    };

    this.http
      .post(ENDPOINT, batch, {
        headers: { 'X-Anonymous-Session': this.anonymousSessionId },
      })
      .pipe(catchError(() => of(null)))
      .subscribe();
  }

  /**
   * Best-effort flush on unload. Uses sendBeacon (cannot attach Authorization).
   * Authenticated-only events that hit only this path may end up tagged
   * anonymous-only; accepted trade-off.
   */
  private beaconFlush(): void {
    this.clearScheduledFlush();

    const queued = this._queue();
    if (queued.length === 0) return;
    if (typeof navigator === 'undefined' || typeof navigator.sendBeacon !== 'function') return;

    const toSend = queued.slice(0, SERVER_BATCH_CAP);
    const remaining = queued.slice(SERVER_BATCH_CAP);
    const batch: AnalyticsEventBatch = {
      anonymousSessionId: this.anonymousSessionId,
      events: toSend,
    };

    try {
      const blob = new Blob([JSON.stringify(batch)], { type: 'application/json' });
      if (navigator.sendBeacon(ENDPOINT, blob)) {
        this._queue.set(remaining);
      }
    } catch {
      // ignore; analytics must never throw.
    }
  }

  private cleanup(): void {
    this.clearScheduledFlush();

    if (this.flushTimer !== null) {
      clearInterval(this.flushTimer);
      this.flushTimer = null;
    }
    if (this.visibilityHandler) {
      document.removeEventListener('visibilitychange', this.visibilityHandler);
      this.visibilityHandler = null;
    }
    if (this.pagehideHandler) {
      window.removeEventListener('pagehide', this.pagehideHandler);
      this.pagehideHandler = null;
    }
    if (this.unloadHandler) {
      window.removeEventListener('beforeunload', this.unloadHandler);
      this.unloadHandler = null;
    }
  }

  private initSessionId(): string {
    if (typeof window === 'undefined') {
      return this.randomUuid();
    }
    try {
      const existing = sessionStorage.getItem(SESSION_KEY);
      if (existing && existing.length > 0) return existing;
      const fresh = this.randomUuid();
      sessionStorage.setItem(SESSION_KEY, fresh);
      return fresh;
    } catch {
      return this.randomUuid();
    }
  }

  private randomUuid(): string {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
      return crypto.randomUUID();
    }
    // RFC4122-ish fallback for environments without crypto.randomUUID (rare).
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
      const r = (Math.random() * 16) | 0;
      const v = c === 'x' ? r : (r & 0x3) | 0x8;
      return v.toString(16);
    });
  }

  private normalizeProvider(providerName: string | null | undefined, url: string | null | undefined): string {
    const source = `${providerName ?? ''} ${this.extractHost(url) ?? ''}`.toLowerCase();
    if (source.includes('booking')) return 'booking';
    if (source.includes('airbnb')) return 'airbnb';

    const nameSlug = (providerName ?? '')
      .trim()
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '');

    return nameSlug || 'other';
  }

  private extractHost(url: string | null | undefined): string | null {
    if (!url) return null;
    try {
      return new URL(url).hostname.replace(/^www\./, '').toLowerCase();
    } catch {
      return null;
    }
  }

  private scheduleFlush(mode: FlushMode): void {
    if (mode === 'immediate') {
      this.flushNow();
      return;
    }

    if (typeof window === 'undefined') {
      return;
    }

    const delayMs = mode === 'soon' ? SOON_FLUSH_DELAY_MS : NORMAL_FLUSH_DELAY_MS;
    if (this.scheduledFlushTimer !== null && this.scheduledFlushDelayMs !== null) {
      if (this.scheduledFlushDelayMs <= delayMs) {
        return;
      }
      this.clearScheduledFlush();
    }

    this.zone.runOutsideAngular(() => {
      this.scheduledFlushDelayMs = delayMs;
      this.scheduledFlushTimer = setTimeout(() => {
        this.scheduledFlushTimer = null;
        this.scheduledFlushDelayMs = null;
        this.flushNow();
      }, delayMs);
    });
  }

  private clearScheduledFlush(): void {
    if (this.scheduledFlushTimer !== null) {
      clearTimeout(this.scheduledFlushTimer);
      this.scheduledFlushTimer = null;
      this.scheduledFlushDelayMs = null;
    }
  }

  private isWithinDedupeWindow(fingerprint: string, now: number, cooldownMs: number): boolean {
    this.pruneFingerprints(now);
    const previous = this.recentFingerprints.get(fingerprint);
    return previous !== undefined && now - previous < cooldownMs;
  }

  private rememberFingerprint(fingerprint: string, now: number): void {
    this.recentFingerprints.set(fingerprint, now);
    this.pruneFingerprints(now);
  }

  private pruneFingerprints(now: number): void {
    for (const [key, timestamp] of this.recentFingerprints) {
      if (now - timestamp > 5 * 60_000) {
        this.recentFingerprints.delete(key);
      }
    }

    while (this.recentFingerprints.size > MAX_DEDUPE_ENTRIES) {
      const oldest = this.recentFingerprints.keys().next().value as string | undefined;
      if (!oldest) break;
      this.recentFingerprints.delete(oldest);
    }
  }

  private buildFingerprint(envelope: AnalyticsEventEnvelope, includeMetadata: boolean): string {
    return [
      envelope.eventType,
      envelope.entityType ?? '',
      envelope.entityId ?? '',
      envelope.destinationId ?? '',
      envelope.route ?? '',
      includeMetadata ? this.metadataFingerprint(envelope.metadata) : '',
    ].join('|');
  }

  private metadataFingerprint(metadata: Record<string, unknown> | undefined): string {
    if (!metadata) return '';

    try {
      return Object.keys(metadata)
        .sort()
        .map((key) => `${key}:${this.fingerprintValue(metadata[key])}`)
        .join(',');
    } catch {
      return '';
    }
  }

  private fingerprintValue(value: unknown): string {
    if (value === null || value === undefined) return '';
    if (Array.isArray(value)) {
      return `[${value.map((item) => this.fingerprintValue(item)).join(',')}]`;
    }
    if (typeof value === 'object') {
      const record = value as Record<string, unknown>;
      return `{${Object.keys(record)
        .sort()
        .map((key) => `${key}:${this.fingerprintValue(record[key])}`)
        .join(',')}}`;
    }
    return String(value);
  }

  /**
   * Normalize a runtime URL like `/places/42?ref=foo` into a route template
   * like `/places/:id` so admin queries can group by route shape rather
   * than by every individual id.
   */
  private normalizeRoute(url: string): string {
    if (!url) return '';
    const noQuery = url.split('?')[0].split('#')[0];
    return noQuery
      .split('/')
      .map((segment) => (/^\d+$/.test(segment) ? ':id' : segment))
      .join('/');
  }
}
