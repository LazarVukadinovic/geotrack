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
    TestBed.resetTestingModule();
  });

  it('loads provider and model options from the backend', () => {
    flushProviders();

    expect(service.providers().map(provider => provider.id)).toEqual(['gemini']);
    expect(service.selectedProviderId()).toBe('gemini');
    expect(service.selectedModelId()).toBe('gemini-2.5-flash');
    expect(service.availableKeyModes()).toEqual(['session', 'project-demo']);
  });

  it('defaults to the first enabled provider when unavailable providers are returned first', () => {
    flushProviderList([
      agentProvider({
        id: 'claude',
        displayName: 'Claude',
        enabled: false,
        models: [
          { id: 'claude-3-5-sonnet', displayName: 'Claude 3.5 Sonnet', isDefault: true },
        ],
        defaultModel: 'claude-3-5-sonnet',
        supportsSavedKey: false,
        supportsSessionKey: false,
        projectDemoKeyEnabled: false,
      }),
      agentProvider(),
    ]);

    expect(service.selectedProviderId()).toBe('gemini');
    expect(service.selectedProvider()?.enabled).toBe(true);
    expect(service.selectedModelId()).toBe('gemini-2.5-flash');
  });

  it('does not switch to an unavailable provider while an enabled provider exists', () => {
    flushProviderList([
      agentProvider({
        id: 'claude',
        displayName: 'Claude',
        enabled: false,
        models: [
          { id: 'claude-3-5-sonnet', displayName: 'Claude 3.5 Sonnet', isDefault: true },
        ],
        defaultModel: 'claude-3-5-sonnet',
        supportsSavedKey: false,
        supportsSessionKey: false,
        projectDemoKeyEnabled: false,
      }),
      agentProvider(),
    ]);

    service.selectProvider('claude');

    expect(service.selectedProviderId()).toBe('gemini');
    expect(service.selectedModelId()).toBe('gemini-2.5-flash');
  });

  it('renders saved key metadata without exposing the key', () => {
    flushProviders({ hasSavedKey: true, keyLast4: 'abcd' });

    const provider = service.selectedProvider();
    expect(provider?.hasSavedKey).toBe(true);
    expect(provider?.keyLast4).toBe('abcd');
    expect(JSON.stringify(provider)).not.toContain('secret');
  });

  it('sends saved-key chat requests without a browser key value', () => {
    flushProviders({ hasSavedKey: true });
    service.setKeyMode('saved');

    service.sendMessage('Find restaurants near Kotor');

    const req = httpMock.expectOne('/api/agent/chat');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toMatchObject({
      message: 'Find restaurants near Kotor',
      provider: 'gemini',
      model: 'gemini-2.5-flash',
      keyMode: 'saved',
      contextOptions: {
        destinationId: 42,
        currentDestinationName: 'Dubrovnik',
        currentCity: 'Dubrovnik',
        currentLocationName: 'Dubrovnik, Croatia',
      },
    });
    expect(req.request.body.sessionApiKey).toBeUndefined();
    expect(analytics.track).toHaveBeenCalledWith(
      'agent_prompt_sent',
      expect.objectContaining({
        metadata: expect.not.objectContaining({ prompt: expect.any(String) }),
      }),
    );

    req.flush(chatResponse());
  });

  it('omits precise coordinates from chat context', () => {
    flushProviders({ hasSavedKey: true });
    service.setKeyMode('saved');

    service.sendMessage('Show me top attractions');

    const req = httpMock.expectOne('/api/agent/chat');
    expect(req.request.body.contextOptions).toMatchObject({
      destinationId: 42,
      currentDestinationName: 'Dubrovnik',
    });
    expect(req.request.body.contextOptions.latitude).toBeUndefined();
    expect(req.request.body.contextOptions.longitude).toBeUndefined();

    req.flush(chatResponse());
  });

  it('attaches structured result cards from the chat response', () => {
    flushProviders({ hasSavedKey: true });
    service.setKeyMode('saved');

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
    flushProviders({ hasSavedKey: true });
    service.setKeyMode('saved');

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
    flushProviders({ hasSavedKey: true });
    service.setKeyMode('saved');

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

  it('updates saved-key metadata immediately after saving before provider refresh completes', () => {
    flushProviders({ hasSavedKey: false });

    service.saveApiKey('new-gemini-secret-5678');

    const save = httpMock.expectOne('/api/agent/api-keys');
    expect(save.request.method).toBe('POST');
    expect(save.request.body).toMatchObject({
      provider: 'gemini',
      apiKey: 'new-gemini-secret-5678',
    });
    save.flush(apiKeyMetadata());

    expect(service.selectedProvider()?.hasSavedKey).toBe(true);
    expect(service.selectedProvider()?.keyLast4).toBe('5678');
    expect(service.keyMode()).toBe('saved');

    service.sendMessage('Find restaurants near Kotor');
    const chat = httpMock.expectOne('/api/agent/chat');
    expect(chat.request.body).toMatchObject({
      provider: 'gemini',
      model: 'gemini-2.5-flash',
      keyMode: 'saved',
    });
    expect(chat.request.body.sessionApiKey).toBeUndefined();
    chat.flush(chatResponse());

    flushProviderRefresh({ hasSavedKey: true, keyLast4: '5678' });
  });

  it('clears saved-key mode immediately after deleting a saved key', () => {
    flushProviders({ hasSavedKey: true, keyLast4: 'abcd' });
    service.setKeyMode('saved');

    service.deleteSavedKey();

    const deleteReq = httpMock.expectOne('/api/agent/api-keys/gemini');
    expect(deleteReq.request.method).toBe('DELETE');
    deleteReq.flush(null, { status: 204, statusText: 'No Content' });

    expect(service.selectedProvider()?.hasSavedKey).toBe(false);
    expect(service.keyMode()).not.toBe('saved');

    flushProviderRefresh({ hasSavedKey: false, keyLast4: null });
  });

  it('renders provider unavailable chat errors from safe backend messages', () => {
    flushProviders({ hasSavedKey: true });
    service.setKeyMode('saved');

    service.sendMessage('Find events');

    const req = httpMock.expectOne('/api/agent/chat');
    req.flush(
      {
        code: 'provider_unavailable',
        message: 'Gemini is temporarily unavailable. Please try again later.',
      },
      { status: 503, statusText: 'Service Unavailable' },
    );

    expect(service.messages().at(-1)?.text).toBe('Gemini is temporarily unavailable. Please try again later.');
  });

  it('uses a temporary key for one request and does not write it to browser storage', () => {
    const storageSpy = vi.spyOn(Storage.prototype, 'setItem');
    flushProviders({ projectDemoKeyEnabled: false });
    service.setKeyMode('session');
    service.setSessionApiKey('temporary-gemini-key');

    service.sendMessage('Show events this week');

    const req = httpMock.expectOne('/api/agent/chat');
    expect(req.request.body.sessionApiKey).toBe('temporary-gemini-key');
    expect(service.sessionApiKey()).toBe('');
    expect(storageSpy).not.toHaveBeenCalled();

    req.flush(chatResponse());
    storageSpy.mockRestore();
  });

  function flushProviders(overrides: Partial<AgentProvidersResponse['providers'][number]> = {}): void {
    flushProviderList([agentProvider(overrides)]);
  }

  function flushProviderList(providers: AgentProvidersResponse['providers']): void {
    const req = httpMock.expectOne('/api/agent/providers');
    expect(req.request.method).toBe('GET');
    req.flush({
      providers,
      mcp: mcpMetadata(),
    } satisfies AgentProvidersResponse);
  }

  function flushProviderRefresh(overrides: Partial<AgentProvidersResponse['providers'][number]> = {}): void {
    flushProviderList([agentProvider(overrides)]);
  }

  function agentProvider(
    overrides: Partial<AgentProvidersResponse['providers'][number]> = {},
  ): AgentProvidersResponse['providers'][number] {
    return {
      id: 'gemini',
      displayName: 'Gemini',
      enabled: true,
      models: [
        { id: 'gemini-2.5-flash', displayName: 'Gemini 2.5 Flash', isDefault: true },
      ],
      defaultModel: 'gemini-2.5-flash',
      supportsSavedKey: true,
      supportsSessionKey: true,
      projectDemoKeyEnabled: true,
      hasSavedKey: false,
      keyLast4: null,
      keyUpdatedAt: null,
      ...overrides,
    };
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
      provider: 'gemini',
      model: 'gemini-2.5-flash',
      toolsUsed: ['geotrack_search_places'],
      timestamp: new Date().toISOString(),
      ...overrides,
    };
  }

  function apiKeyMetadata() {
    return {
      provider: 'gemini',
      keyLast4: '5678',
      displayName: null,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      lastUsedAt: null,
    };
  }
});
