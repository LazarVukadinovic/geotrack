import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { AnalyticsService } from './analytics.service';
import { AnalyticsPreferencesService } from './analytics-preferences.service';
import { DestinationService } from './destination.service';

describe('AnalyticsService', () => {
  let service: AnalyticsService;
  let httpMock: HttpTestingController;
  let analyticsPreferences: AnalyticsPreferencesService;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        DestinationService,
      ],
    });
    service = TestBed.inject(AnalyticsService);
    httpMock = TestBed.inject(HttpTestingController);
    analyticsPreferences = TestBed.inject(AnalyticsPreferencesService);
  });

  afterEach(() => {
    httpMock.verify();
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
    TestBed.resetTestingModule();
  });

  it('queues a track call without flushing immediately', () => {
    service.track('page_view');
    expect(service.queueSize()).toBe(1);
    httpMock.expectNone('/api/interests/events');
  });

  it('flushNow posts the queued events and empties the queue', () => {
    service.track('page_view');
    service.track('detail_view', { entityType: 'place', entityId: 42 });

    service.flushNow();

    const req = httpMock.expectOne('/api/interests/events');
    expect(req.request.method).toBe('POST');
    const body = req.request.body as { events: { eventType: string }[]; anonymousSessionId: string };
    expect(body.events).toHaveLength(2);
    expect(body.events[0].eventType).toBe('page_view');
    expect(body.events[1].eventType).toBe('detail_view');
    expect(body.anonymousSessionId).toMatch(/[0-9a-f-]{36}/i);
    expect(req.request.headers.get('X-Anonymous-Session')).toBe(body.anonymousSessionId);
    expect(service.queueSize()).toBe(0);
    req.flush(null);
  });

  it('uses keepalive fetch with X-Anonymous-Session when the page is hidden', () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true });
    vi.stubGlobal('fetch', fetchMock);

    service.track('page_view');
    expect(service.queueSize()).toBe(1);

    Object.defineProperty(document, 'visibilityState', { value: 'hidden', configurable: true });
    document.dispatchEvent(new Event('visibilitychange'));
    Object.defineProperty(document, 'visibilityState', { value: 'visible', configurable: true });

    httpMock.expectNone('/api/interests/events');
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0][0]).toBe('/api/interests/events');

    const init = fetchMock.mock.calls[0][1] as RequestInit;
    const headers = init.headers as Record<string, string>;
    const body = JSON.parse(init.body as string) as {
      anonymousSessionId: string;
      events: { eventType: string }[];
    };

    expect(init.method).toBe('POST');
    expect(init.keepalive).toBe(true);
    expect(init.credentials).toBe('include');
    expect(headers['Content-Type']).toBe('application/json');
    expect(headers['X-Anonymous-Session']).toBe(body.anonymousSessionId);
    expect(body.events[0].eventType).toBe('page_view');
    expect(service.queueSize()).toBe(0);
  });

  it('flushNow is a no-op when the queue is empty', () => {
    service.flushNow();
    httpMock.expectNone('/api/interests/events');
  });

  it('does not queue or post events when analytics is disabled', () => {
    analyticsPreferences.setEnabled(false);
    TestBed.inject(ApplicationRef).tick();

    service.track('page_view');
    expect(service.queueSize()).toBe(0);

    service.flushNow();
    httpMock.expectNone('/api/interests/events');
  });

  it('clears already queued events when analytics is disabled', () => {
    service.track('page_view');
    expect(service.queueSize()).toBe(1);

    analyticsPreferences.setEnabled(false);
    TestBed.inject(ApplicationRef).tick();

    expect(service.queueSize()).toBe(0);
    service.flushNow();
    httpMock.expectNone('/api/interests/events');
  });

  it('threshold-flushes once the queue reaches 25 events', () => {
    for (let i = 0; i < 25; i++) {
      service.track('favorite_added', { entityType: 'place', entityId: i + 1 });
    }

    // Force the threshold-flush effect to run; effects fire asynchronously
    // by default, so a synchronous expectOne would race them.
    TestBed.inject(ApplicationRef).tick();

    const req = httpMock.expectOne('/api/interests/events');
    expect((req.request.body as { events: unknown[] }).events).toHaveLength(25);
    req.flush(null);
    expect(service.queueSize()).toBe(0);
  });

  it('persists the anonymous session id across calls within the tab', () => {
    const stored = sessionStorage.getItem('geotrack_anon_session_id');
    expect(stored).toBeTruthy();
    expect(stored).toMatch(/[0-9a-f-]{36}/i);

    service.track('login_completed');
    service.flushNow();
    const req1 = httpMock.expectOne('/api/interests/events');
    const sid1 = (req1.request.body as { anonymousSessionId: string }).anonymousSessionId;
    req1.flush(null);

    service.track('register_completed');
    service.flushNow();
    const req2 = httpMock.expectOne('/api/interests/events');
    const sid2 = (req2.request.body as { anonymousSessionId: string }).anonymousSessionId;
    req2.flush(null);

    expect(sid1).toBe(sid2);
    expect(sid1).toBe(stored);
  });

  it('normalizes numeric route segments to :id in the route field', () => {
    const router = TestBed.inject(Router);
    Object.defineProperty(router, 'url', { value: '/places/42?ref=foo', configurable: true });

    service.track('detail_view', { entityType: 'place', entityId: 42 });
    service.flushNow();

    const req = httpMock.expectOne('/api/interests/events');
    const body = req.request.body as { events: { route?: string }[] };
    expect(body.events[0].route).toBe('/places/:id');
    req.flush(null);
  });

  it('normalizes outbound partner link metadata without sending the full url', () => {
    service.trackOutboundLink('Booking.com', 'https://www.booking.com/hotel/hr/example.html?secret=1', {
      entityType: 'hotel',
      entityId: 7,
    });

    const req = httpMock.expectOne('/api/interests/events');
    const body = req.request.body as { events: { eventType: string; metadata?: Record<string, unknown> }[] };
    expect(body.events[0].eventType).toBe('outbound_link_clicked');
    expect(body.events[0].metadata).toEqual({
      provider: 'booking',
      partnerKey: 'booking',
      host: 'booking.com',
    });
    req.flush(null);
  });

  it('marks non-booking partner links as external with a provider slug', () => {
    service.trackOutboundLink('Direct Booking', 'https://direct.example/stays/7', {
      entityType: 'hotel',
      entityId: 7,
    });

    const req = httpMock.expectOne('/api/interests/events');
    const body = req.request.body as { events: { eventType: string; metadata?: Record<string, unknown> }[] };
    expect(body.events[0].metadata).toEqual({
      provider: 'direct-booking',
      partnerKey: 'external',
      host: 'direct.example',
    });
    req.flush(null);
  });

  it('suppresses repeated page views for the same normalized route inside the cooldown window', () => {
    const router = TestBed.inject(Router);
    Object.defineProperty(router, 'url', { value: '/places/42?ref=foo', configurable: true });

    service.track('page_view');
    service.track('page_view');

    expect(service.queueSize()).toBe(1);
    service.flushNow();

    const req = httpMock.expectOne('/api/interests/events');
    const body = req.request.body as { events: { eventType: string; route?: string }[] };
    expect(body.events).toHaveLength(1);
    expect(body.events[0].eventType).toBe('page_view');
    expect(body.events[0].route).toBe('/places/:id');
    req.flush(null);
  });

  it('does not suppress high-value outbound clicks', () => {
    service.trackOutboundLink('Booking.com', 'https://www.booking.com/hotel/hr/example.html', {
      entityType: 'hotel',
      entityId: 7,
    });
    let req = httpMock.expectOne('/api/interests/events');
    req.flush(null);

    service.trackOutboundLink('Booking.com', 'https://www.booking.com/hotel/hr/example.html', {
      entityType: 'hotel',
      entityId: 7,
    });
    req = httpMock.expectOne('/api/interests/events');
    const body = req.request.body as { events: { eventType: string }[] };
    expect(body.events).toHaveLength(1);
    expect(body.events[0].eventType).toBe('outbound_link_clicked');
    req.flush(null);
  });

  it('sends page duration for the active entity view when navigation leaves it', () => {
    const router = TestBed.inject(Router);
    Object.defineProperty(router, 'url', { value: '/places/42?ref=foo', configurable: true });
    let now = 10_000;
    vi.spyOn(Date, 'now').mockImplementation(() => now);

    service.track('detail_view', { entityType: 'place', entityId: 42 });
    now += 15_000;
    service.endCurrentView('navigation');
    service.flushNow();

    const req = httpMock.expectOne('/api/interests/events');
    const body = req.request.body as {
      events: {
        eventType: string;
        entityType?: string;
        entityId?: number;
        route?: string;
        durationMs?: number;
        metadata?: Record<string, unknown>;
      }[];
    };
    const duration = body.events.find(event => event.eventType === 'page_duration');

    expect(duration).toEqual(expect.objectContaining({
      entityType: 'place',
      entityId: 42,
      route: '/places/:id',
      durationMs: 15_000,
      metadata: {
        sourceEventType: 'detail_view',
        endReason: 'navigation',
      },
    }));
    req.flush(null);
  });

  it('caps excessive page duration before queueing it', () => {
    let now = 10_000;
    vi.spyOn(Date, 'now').mockImplementation(() => now);

    service.track('detail_view', { entityType: 'hotel', entityId: 7 });
    now += 60 * 60 * 1000;
    service.endCurrentView('navigation');
    service.flushNow();

    const req = httpMock.expectOne('/api/interests/events');
    const body = req.request.body as { events: { eventType: string; durationMs?: number }[] };
    expect(body.events.find(event => event.eventType === 'page_duration')?.durationMs).toBe(30 * 60 * 1000);
    req.flush(null);
  });

  it('swallows errors so tracking never breaks the caller', () => {
    service.track('page_view');
    service.flushNow();
    const req = httpMock.expectOne('/api/interests/events');

    // Simulating a 500 must not throw — the .pipe(catchError) inside the
    // service should turn it into a silent observable.
    expect(() => req.flush('boom', { status: 500, statusText: 'Server Error' })).not.toThrow();
    expect(service.queueSize()).toBe(0);
  });
});
