import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AnalyticsService } from '../../../core/services/analytics.service';
import { AuthService } from '../../../core/services/auth.service';
import { DestinationService } from '../../../core/services/destination.service';
import { LocationService } from '../../../core/services/location.service';
import type { AgentProvidersResponse } from '../models/agent.model';
import { AgentService } from './agent.service';

describe('AgentService', () => {
  let service: AgentService;
  let httpMock: HttpTestingController;
  let analytics: { track: ReturnType<typeof vi.fn> };
  let isLoggedIn: ReturnType<typeof signal<boolean>>;
  let destinationId: ReturnType<typeof signal<number | null>>;
  let destinationName: ReturnType<typeof signal<string>>;
  let currentLocation: ReturnType<typeof signal<{
    latitude: number;
    longitude: number;
    city: string;
    region: string;
    country: string;
    displayLabel: string;
    source: 'manual';
    cachedAt: number;
  } | null>>;

  beforeEach(() => {
    analytics = { track: vi.fn() };
    isLoggedIn = signal(true);
    destinationId = signal<number | null>(42);
    destinationName = signal('Dubrovnik');
    currentLocation = signal({
      latitude: 42.65,
      longitude: 18.09,
      city: 'Dubrovnik',
      region: '',
      country: 'Croatia',
      displayLabel: 'Dubrovnik, Croatia',
      source: 'manual' as const,
      cachedAt: Date.now(),
    });

    localStorage.setItem('geotrack_agent_provider', 'gemini');
    sessionStorage.setItem('geotrack_agent_session_api_key', 'legacy-session-key');

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AnalyticsService, useValue: analytics },
        { provide: AuthService, useValue: { isLoggedIn } },
        {
          provide: DestinationService,
          useValue: {
            id: destinationId,
            name: destinationName,
          },
        },
        {
          provide: LocationService,
          useValue: {
            location: currentLocation,
            displayCity: signal('Dubrovnik'),
          },
        },
      ],
    });

    service = TestBed.inject(AgentService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
    sessionStorage.clear();
    TestBed.resetTestingModule();
  });

  it('loads non-sensitive Ask Agent metadata and clears old agent key browser state', () => {
    flushAgentInfo();

    expect(service.mcp()).toEqual(mcpMetadata());
    expect(localStorage.getItem('geotrack_agent_provider')).toBeNull();
    expect(sessionStorage.getItem('geotrack_agent_session_api_key')).toBeNull();
  });

  it('sends chat requests without provider, model, or API key fields', () => {
    flushAgentInfo();

    service.sendMessage('Find restaurants near Kotor');

    const req = httpMock.expectOne('/api/agent/chat');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toMatchObject({
      message: 'Find restaurants near Kotor',
      contextOptions: {
        latitude: 42.65,
        longitude: 18.09,
        locationSource: 'manual',
        destinationId: 42,
        currentDestinationName: 'Dubrovnik',
        currentCity: 'Dubrovnik',
        currentLocationName: 'Dubrovnik, Croatia',
      },
    });
    expect(req.request.body.provider).toBeUndefined();
    expect(req.request.body.model).toBeUndefined();
    expect(req.request.body.keyMode).toBeUndefined();
    expect(req.request.body.sessionApiKey).toBeUndefined();
    expect(req.request.body.apiKey).toBeUndefined();
    expect(analytics.track).toHaveBeenCalledWith(
      'agent_prompt_sent',
      expect.objectContaining({
        metadata: expect.not.objectContaining({ prompt: expect.any(String) }),
      }),
    );

    req.flush(chatResponse());
  });

  it('does not send chat requests or render backend auth errors when logged out', () => {
    flushAgentInfo();
    isLoggedIn.set(false);

    service.sendMessage('sta je skijanje');

    httpMock.expectNone('/api/agent/chat');
    expect(analytics.track).not.toHaveBeenCalledWith(
      'agent_prompt_sent',
      expect.anything(),
    );
    expect(service.messages().map(message => message.text)).not.toContain('Please sign in before using Ask Agent.');
  });

  it('includes available location coordinates in chat context', () => {
    flushAgentInfo();

    service.sendMessage('Show me top attractions');

    const req = httpMock.expectOne('/api/agent/chat');
    expect(req.request.body.contextOptions).toMatchObject({
      destinationId: 42,
      currentDestinationName: 'Dubrovnik',
      latitude: 42.65,
      longitude: 18.09,
      locationSource: 'manual',
    });

    req.flush(chatResponse());
  });

  it('sends a stable conversation id on follow-up chat requests', () => {
    flushAgentInfo();

    service.sendMessage('Find restaurants in Dubrovnik');
    const first = httpMock.expectOne('/api/agent/chat');
    expect(first.request.body.conversationId).toEqual(expect.any(String));
    const conversationId = first.request.body.conversationId;
    first.flush(chatResponse({ conversationId }));

    service.sendMessage('And beaches?');
    const followUp = httpMock.expectOne('/api/agent/chat');
    expect(followUp.request.body.conversationId).toBe(conversationId);
    followUp.flush(chatResponse({ conversationId }));
  });

  it('reuses the same conversation id for rapid sends before the first response returns', () => {
    flushAgentInfo();

    service.sendMessage('plaze');
    const first = httpMock.expectOne('/api/agent/chat');
    const conversationId = first.request.body.conversationId;
    expect(conversationId).toEqual(expect.any(String));

    service.sendMessage('dubrovnik');
    const followUp = httpMock.expectOne('/api/agent/chat');
    expect(followUp.request.body.conversationId).toBe(conversationId);

    first.flush(chatResponse({ conversationId }));
    followUp.flush(chatResponse({ conversationId }));
  });

  it('starting a new chat creates a new conversation id', () => {
    flushAgentInfo();

    service.sendMessage('Find restaurants in Dubrovnik');
    const first = httpMock.expectOne('/api/agent/chat');
    const firstConversationId = first.request.body.conversationId;
    first.flush(chatResponse({ conversationId: firstConversationId }));

    service.startNewChat();
    service.sendMessage('Find beaches in Dubrovnik');
    const next = httpMock.expectOne('/api/agent/chat');

    expect(next.request.body.conversationId).toEqual(expect.any(String));
    expect(next.request.body.conversationId).not.toBe(firstConversationId);
    next.flush(chatResponse({ conversationId: next.request.body.conversationId }));
  });

  it('attaches structured result cards from the chat response', () => {
    flushAgentInfo();

    service.sendMessage('Show me beaches in Dubrovnik');

    const req = httpMock.expectOne('/api/agent/chat');
    req.flush(chatResponse({
      cards: [
        {
          entityType: 'place',
          id: 12,
          title: 'Catalog Beach',
          subtitle: 'Beach, Dubrovnik',
          description: 'A public GeoTrack place result.',
          imageUrl: '/media/seed/catalog-beach.svg',
          locationName: 'Dubrovnik',
          rating: 4.6,
          reviewCount: 18,
          tags: ['Beaches'],
          placeType: 'Beach',
          detailRoute: '/places/12',
        },
      ],
    }));

    const assistant = service.messages().at(-1);
    expect(assistant?.role).toBe('assistant');
    expect(assistant?.text).toBe('Try the public waterfront places.');
    expect(assistant?.cards).toEqual([
      expect.objectContaining({
        entityType: 'place',
        id: 12,
        title: 'Catalog Beach',
        detailRoute: '/places/12',
        imageUrl: '/media/seed/catalog-beach.svg',
      }),
    ]);
  });

  it('normalizes safe public image paths from result cards', () => {
    flushAgentInfo();

    service.sendMessage('Show me beaches in Dubrovnik');

    const req = httpMock.expectOne('/api/agent/chat');
    req.flush(chatResponse({
      cards: [
        {
          entityType: 'place',
          id: 12,
          title: 'Catalog Beach',
          imageUrl: 'http://localhost:5003/media/seed/catalog-beach.svg',
          detailRoute: '/places/12',
        },
        {
          entityType: 'event',
          id: 13,
          title: 'Catalog Festival',
          imageUrl: 'images/explore-image-fallback.svg',
          detailRoute: '/events/13',
        },
      ],
    }));

    const assistant = service.messages().at(-1);
    expect(assistant?.cards?.map(card => card.imageUrl)).toEqual([
      '/media/seed/catalog-beach.svg',
      '/images/explore-image-fallback.svg',
    ]);
  });

  it('drops unsafe result card image URLs so the card can fall back', () => {
    flushAgentInfo();

    service.sendMessage('Show me beaches in Dubrovnik');

    const req = httpMock.expectOne('/api/agent/chat');
    req.flush(chatResponse({
      cards: [
        {
          entityType: 'place',
          id: 12,
          title: 'Catalog Beach',
          imageUrl: 'javascript:alert(1)',
          detailRoute: '/places/12',
        },
      ],
    }));

    expect(service.messages().at(-1)?.cards?.[0]?.imageUrl).toBeNull();
  });

  it('keeps no-result responses from inheriting previous cards', () => {
    flushAgentInfo();

    service.sendMessage('Show me beaches in Dubrovnik');
    const first = httpMock.expectOne('/api/agent/chat');
    first.flush(chatResponse({
      cards: [
        {
          entityType: 'place',
          id: 12,
          title: 'Catalog Beach',
          detailRoute: '/places/12',
        },
      ],
    }));

    service.sendMessage('Show me volcano observatories');
    const second = httpMock.expectOne('/api/agent/chat');
    second.flush(chatResponse({ conversationId: 'conversation-1', cards: [] }));

    expect(service.messages().at(-1)?.cards).toEqual([]);
  });

  it('renders backend unavailable chat errors without asking for a key', () => {
    flushAgentInfo();

    service.sendMessage('Find events');

    const req = httpMock.expectOne('/api/agent/chat');
    req.flush(
      {
        code: 'agent_unavailable',
        message: 'Ask Agent is currently unavailable. Please try again later.',
      },
      { status: 503, statusText: 'Service Unavailable' },
    );

    const message = service.messages().at(-1)?.text ?? '';
    expect(message).toBe('Ask Agent is currently unavailable. Please try again later.');
    expect(message.toLowerCase()).not.toContain('key');
  });

  function flushAgentInfo(): void {
    const req = httpMock.expectOne('/api/agent/providers');
    expect(req.request.method).toBe('GET');
    req.flush({
      mcp: mcpMetadata(),
    } satisfies AgentProvidersResponse);
  }

  function mcpMetadata(): AgentProvidersResponse['mcp'] {
    return {
      enabled: true,
      url: '/api/mcp',
      transport: 'remote-http',
      privateToolsEnabled: false,
    };
  }

  function chatResponse(overrides: Partial<{
    conversationId: string;
    cards: Array<{
      entityType: 'place' | 'stay' | 'event';
      id: number;
      title: string;
      subtitle?: string | null;
      description?: string | null;
      imageUrl?: string | null;
      locationName?: string | null;
      rating?: number | null;
      reviewCount?: number | null;
      tags?: string[];
      dateText?: string | null;
      stayType?: string | null;
      placeType?: string | null;
      detailRoute: string;
    }>;
  }> = {}) {
    return {
      id: 'response-1',
      conversationId: 'conversation-1',
      role: 'assistant',
      type: 'text',
      text: 'Try the public waterfront places.',
      toolsUsed: ['geotrack_search_places'],
      timestamp: new Date().toISOString(),
      ...overrides,
    };
  }
});
