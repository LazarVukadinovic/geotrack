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

const FLUSH_INTERVAL_MS = 5_000;
const MAX_BATCH = 25;
/** Hard cap matching the server's MaxBatchSize. */
const SERVER_BATCH_CAP = 50;

/**
 * First-party analytics tracker. Buffers events client-side and flushes them
 * to POST /api/analytics/events on a 5-second timer, when the queue hits 25
 * events, or when the tab becomes hidden / unloads. The auth interceptor
 * attaches the JWT automatically for authenticated users; anonymous sessions
 * carry a sessionStorage-bound UUID that dies with the tab.
 *
 * Failures are swallowed silently — analytics must never break the user flow.
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
  private visibilityHandler: (() => void) | null = null;
  private unloadHandler: (() => void) | null = null;

  constructor() {
    // Threshold flush: when the queue swells past MAX_BATCH between timer
    // ticks (e.g. a burst of card views), flush immediately instead of
    // waiting up to 5s.
    effect(() => {
      if (this._queue().length >= MAX_BATCH) {
        this.flushNow();
      }
    });

    if (typeof window !== 'undefined') {
      // Run timers outside Angular's zone so they don't trigger change detection.
      this.zone.runOutsideAngular(() => {
        this.flushTimer = setInterval(() => this.flushNow(), FLUSH_INTERVAL_MS);
      });

      this.visibilityHandler = () => {
        if (document.visibilityState === 'hidden') {
          this.flushNow();
        }
      };
      document.addEventListener('visibilitychange', this.visibilityHandler);

      // beforeunload is the last-ditch flush. sendBeacon cannot attach an
      // Authorization header, so authenticated events flushed only here may
      // end up tagged anonymous-only. visibilitychange above is the primary
      // path for authenticated users.
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
    this._queue.update((q) => [...q, envelope]);
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
    const queued = this._queue();
    if (queued.length === 0) return;

    // Take up to the server batch cap; keep any overflow for the next flush.
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
   * Best-effort flush on unload. Uses sendBeacon (cannot attach
   * Authorization). Authenticated-only events that hit only this path may
   * end up tagged anonymous-only — accepted trade-off.
   */
  private beaconFlush(): void {
    const queued = this._queue();
    if (queued.length === 0) return;
    if (typeof navigator === 'undefined' || typeof navigator.sendBeacon !== 'function') return;

    const batch: AnalyticsEventBatch = {
      anonymousSessionId: this.anonymousSessionId,
      events: queued.slice(0, SERVER_BATCH_CAP),
    };
    try {
      const blob = new Blob([JSON.stringify(batch)], { type: 'application/json' });
      navigator.sendBeacon(ENDPOINT, blob);
      this._queue.set([]);
    } catch {
      // ignore — analytics must never throw.
    }
  }

  private cleanup(): void {
    if (this.flushTimer !== null) {
      clearInterval(this.flushTimer);
      this.flushTimer = null;
    }
    if (this.visibilityHandler) {
      document.removeEventListener('visibilitychange', this.visibilityHandler);
      this.visibilityHandler = null;
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
