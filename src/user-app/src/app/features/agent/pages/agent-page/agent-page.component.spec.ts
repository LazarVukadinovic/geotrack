import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component, computed, signal, type WritableSignal } from '@angular/core';
import { provideRouter, Router } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { EXPLORE_IMAGE_FALLBACK } from '../../../../core/constants/explore-media.constants';
import { AnalyticsService } from '../../../../core/services/analytics.service';
import { SaveToCollectionService } from '../../../../core/services/save-to-collection.service';
import { AgentService } from '../../services/agent.service';
import type { AgentKeyMode, AgentProvider, ChatMessage } from '../../models/agent.model';
import { AgentPageComponent } from './agent-page.component';

@Component({ standalone: true, template: '' })
class RouteTargetComponent {}

describe('AgentPageComponent', () => {
  let fixture: ComponentFixture<AgentPageComponent>;
  let messages: WritableSignal<ChatMessage[]>;
  let agentService: {
    providers: WritableSignal<AgentProvider[]>;
    selectedProviderId: WritableSignal<string | null>;
    selectedModelId: WritableSignal<string | null>;
    keyMode: WritableSignal<AgentKeyMode>;
    sessionApiKey: WritableSignal<string>;
    saveApiKey: ReturnType<typeof vi.fn>;
    deleteSavedKey: ReturnType<typeof vi.fn>;
    testKey: ReturnType<typeof vi.fn>;
    sendMessage: ReturnType<typeof vi.fn>;
    setKeyMode: ReturnType<typeof vi.fn>;
    setSessionApiKey: ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    vi.clearAllMocks();
    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
      return window.setTimeout(() => callback(performance.now()), 0);
    });
    vi.stubGlobal('cancelAnimationFrame', (id: number) => window.clearTimeout(id));

    const providers = signal<AgentProvider[]>([agentProvider({ hasSavedKey: true, keyLast4: 'abcd' })]);
    const selectedProviderId = signal<string | null>('gemini');
    const selectedModelId = signal<string | null>('gemini-2.5-flash');
    const keyMode = signal<AgentKeyMode>('saved');
    const sessionApiKey = signal('');
    messages = signal<ChatMessage[]>([
      {
        id: 'welcome',
        role: 'assistant',
        type: 'text',
        text: 'Choose an AI provider, then ask GeoTrack about places, stays, or events.',
        suggestionPills: [
          { label: 'Top Attractions', prompt: 'Show me top attractions' },
        ],
        timestamp: new Date(),
      },
    ]);
    const selectedProvider = computed(() =>
      providers().find(provider => provider.id === selectedProviderId()) ?? null,
    );
    const selectedModels = computed(() => selectedProvider()?.models ?? []);
    const availableKeyModes = computed<AgentKeyMode[]>(() => {
      const provider = selectedProvider();
      if (!provider?.enabled) return [];
      const modes: AgentKeyMode[] = [];
      if (provider.supportsSavedKey) modes.push('saved');
      if (provider.supportsSessionKey) modes.push('session');
      if (provider.projectDemoKeyEnabled) modes.push('project-demo');
      return modes;
    });

    agentService = {
      providers,
      selectedProviderId,
      selectedModelId,
      keyMode,
      sessionApiKey,
      saveApiKey: vi.fn(),
      deleteSavedKey: vi.fn(),
      testKey: vi.fn(),
      sendMessage: vi.fn(),
      setKeyMode: vi.fn((mode: AgentKeyMode) => keyMode.set(mode)),
      setSessionApiKey: vi.fn((value: string) => sessionApiKey.set(value)),
    };

    await TestBed.configureTestingModule({
      imports: [AgentPageComponent],
      providers: [
        provideRouter([
          { path: 'places/:id', component: RouteTargetComponent },
          { path: 'stays/:id', component: RouteTargetComponent },
          { path: 'events/:id', component: RouteTargetComponent },
        ]),
        { provide: AnalyticsService, useValue: { track: vi.fn() } },
        { provide: SaveToCollectionService, useValue: { open: vi.fn() } },
        {
          provide: AgentService,
          useValue: {
            messages,
            isTyping: signal(false),
            hasSentMessage: signal(false),
            providers,
            mcp: signal({
              enabled: true,
              url: '/api/mcp',
              transport: 'remote-http',
              privateToolsEnabled: false,
            }),
            selectedProviderId,
            selectedModelId,
            keyMode,
            sessionApiKey,
            statusMessage: signal<string | null>(null),
            isSavingKey: signal(false),
            isTestingKey: signal(false),
            isLoadingProviders: signal(false),
            selectedProvider,
            selectedModels,
            availableKeyModes,
            selectProvider: vi.fn((providerId: string) => {
              if (providers().some(provider => provider.id === providerId)) {
                selectedProviderId.set(providerId);
              }
            }),
            selectModel: vi.fn((modelId: string) => selectedModelId.set(modelId)),
            setKeyMode: agentService.setKeyMode,
            setSessionApiKey: agentService.setSessionApiKey,
            saveApiKey: agentService.saveApiKey,
            deleteSavedKey: agentService.deleteSavedKey,
            testKey: agentService.testKey,
            sendMessage: agentService.sendMessage,
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AgentPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    TestBed.resetTestingModule();
  });

  it('keeps provider and key controls out of the inline chat layout by default', () => {
    const nativeElement = fixture.nativeElement as HTMLElement;

    expect(nativeElement.querySelector('#agent-settings-dialog')).toBeNull();
    expect(nativeElement.querySelector('.agent__field')).toBeNull();
    expect(nativeElement.querySelector('.agent__settings-button')?.getAttribute('aria-label')).toBe('Agent settings');
  });

  it('opens and closes the settings popup from the header button', () => {
    openSettings();

    let nativeElement = fixture.nativeElement as HTMLElement;
    const dialog = nativeElement.querySelector('#agent-settings-dialog') as HTMLElement | null;
    expect(dialog).toBeTruthy();
    expect(dialog?.getAttribute('role')).toBe('dialog');
    expect(dialog?.textContent).toContain('Provider');
    expect(dialog?.textContent).toContain('Model');
    expect(dialog?.textContent).toContain('Use GeoTrack in external AI apps');

    findButton('Close Agent settings').click();
    fixture.detectChanges();
    nativeElement = fixture.nativeElement as HTMLElement;
    expect(nativeElement.querySelector('#agent-settings-dialog')).toBeNull();
  });

  it('keeps saved-key save, test, and delete controls working inside the popup', () => {
    openSettings();

    const input = queryInput('Paste key to save or replace');
    input.value = 'new-provider-key';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    findButton('Test key').click();
    findButton('Save').click();
    findButton('Delete').click();

    expect(agentService.testKey).toHaveBeenCalledWith('new-provider-key', 'session');
    expect(agentService.saveApiKey).toHaveBeenCalledWith('new-provider-key');
    expect(agentService.deleteSavedKey).toHaveBeenCalledTimes(1);
  });

  it('keeps temporary keys in component state and never writes them to browser storage', () => {
    const storageSpy = vi.spyOn(Storage.prototype, 'setItem');
    openSettings();

    findButton('Temporary key').click();
    fixture.detectChanges();

    const input = queryInput('Used once, not saved');
    input.value = 'temporary-agent-key';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    findButton('Test').click();

    expect(agentService.setKeyMode).toHaveBeenCalledWith('session');
    expect(agentService.setSessionApiKey).toHaveBeenCalledWith('temporary-agent-key');
    expect(agentService.testKey).toHaveBeenCalledWith('temporary-agent-key');
    expect(storageSpy).not.toHaveBeenCalled();
    storageSpy.mockRestore();
  });

  it('still sends chat messages from the composer', () => {
    const nativeElement = fixture.nativeElement as HTMLElement;
    const input = nativeElement.querySelector('#agent-message-input') as HTMLInputElement;
    input.value = 'Find restaurants near Kotor';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    (nativeElement.querySelector('.agent__send-btn') as HTMLButtonElement).click();

    expect(agentService.sendMessage).toHaveBeenCalledWith('Find restaurants near Kotor');
  });

  it('renders assistant text without an empty card area', () => {
    messages.set([
      assistantMessage({
        id: 'text-only',
        text: 'I found a few GeoTrack matches.',
      }),
    ]);
    fixture.detectChanges();

    const nativeElement = fixture.nativeElement as HTMLElement;
    expect(nativeElement.textContent).toContain('I found a few GeoTrack matches.');
    expect(nativeElement.querySelector('.agent__cards-section')).toBeNull();
  });

  it('renders place, stay, and event cards under assistant messages', () => {
    messages.set([
      assistantMessage({
        id: 'with-cards',
        text: 'Here are GeoTrack matches for Dubrovnik.',
        cards: [
          resultCard({ entityType: 'place', id: 11, title: 'Catalog Beach', placeType: 'Beach', detailRoute: '/places/11' }),
          resultCard({ entityType: 'stay', id: 22, title: 'Catalog Hotel', stayType: 'Hotel', detailRoute: '/stays/22' }),
          resultCard({ entityType: 'event', id: 33, title: 'Catalog Festival', dateText: 'Jun 1, 2026', detailRoute: '/events/33' }),
        ],
      }),
    ]);
    fixture.detectChanges();

    const nativeElement = fixture.nativeElement as HTMLElement;
    const cards = Array.from(nativeElement.querySelectorAll('.agent-rec-card'));
    expect(cards).toHaveLength(3);
    expect(nativeElement.textContent).toContain('Catalog Beach');
    expect(nativeElement.textContent).toContain('Catalog Hotel');
    expect(nativeElement.textContent).toContain('Catalog Festival');
    expect(nativeElement.textContent).toContain('Place');
    expect(nativeElement.textContent).toContain('Stay');
    expect(nativeElement.textContent).toContain('Event');
  });

  it('renders a result card image with title alt text when imageUrl exists', () => {
    messages.set([
      assistantMessage({
        id: 'image-card',
        text: 'One matching place.',
        cards: [
          resultCard({
            entityType: 'place',
            id: 11,
            title: 'Catalog Beach',
            imageUrl: '/media/seed/catalog-beach.svg',
            detailRoute: '/places/11',
          }),
        ],
      }),
    ]);
    fixture.detectChanges();

    const nativeElement = fixture.nativeElement as HTMLElement;
    const image = nativeElement.querySelector('.agent-rec-card__img') as HTMLImageElement;

    expect(image.getAttribute('src')).toBe('/media/seed/catalog-beach.svg');
    expect(image.getAttribute('alt')).toBe('Catalog Beach image');
  });

  it('renders the fallback image when a result card has no imageUrl', () => {
    messages.set([
      assistantMessage({
        id: 'fallback-card',
        text: 'One matching place.',
        cards: [
          resultCard({
            entityType: 'place',
            id: 12,
            title: 'Image-Free Place',
            imageUrl: null,
            detailRoute: '/places/12',
          }),
        ],
      }),
    ]);
    fixture.detectChanges();

    const nativeElement = fixture.nativeElement as HTMLElement;
    const image = nativeElement.querySelector('.agent-rec-card__img') as HTMLImageElement;

    expect(image.getAttribute('src')).toBe(EXPLORE_IMAGE_FALLBACK);
    expect(image.getAttribute('alt')).toBe('Image-Free Place image');
  });

  it('does not render a card container for empty card arrays', () => {
    messages.set([
      assistantMessage({
        id: 'empty-cards',
        text: 'No matching GeoTrack results were found.',
        cards: [],
      }),
    ]);
    fixture.detectChanges();

    const nativeElement = fixture.nativeElement as HTMLElement;
    expect(nativeElement.querySelector('.agent__cards-section')).toBeNull();
  });

  it('navigates to the detail route when a card is clicked', () => {
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    messages.set([
      assistantMessage({
        id: 'click-card',
        text: 'One matching place.',
        cards: [
          resultCard({ entityType: 'place', id: 44, title: 'Catalog Walls', detailRoute: '/places/44' }),
        ],
      }),
    ]);
    fixture.detectChanges();

    const nativeElement = fixture.nativeElement as HTMLElement;
    (nativeElement.querySelector('.agent-rec-card') as HTMLElement).click();

    expect(navigateSpy).toHaveBeenCalledWith('/places/44');
  });

  it('renders user message bubbles with the wider user layout and safe wrapping', () => {
    messages.set([
      {
        id: 'user-message',
        role: 'user',
        type: 'text',
        text: 'beach in dubrovnik',
        timestamp: new Date(),
      },
      {
        id: 'long-user-message',
        role: 'user',
        type: 'text',
        text: 'superlongunbrokenagentquerythatneedstowrapsafelywithoutoverflowingthechatlayout',
        timestamp: new Date(),
      },
    ]);
    fixture.detectChanges();

    const nativeElement = fixture.nativeElement as HTMLElement;
    const host = nativeElement.querySelector('.agent__bubble-row--user app-agent-message-bubble') as HTMLElement;
    const bubble = nativeElement.querySelector('.bubble--user') as HTMLElement;
    const text = nativeElement.querySelector('.bubble--user .bubble__text') as HTMLElement;

    expect(host).toBeTruthy();
    expect(bubble).toBeTruthy();
    expect(text.textContent).toContain('beach in dubrovnik');
    expect(getComputedStyle(text).overflowWrap).toBe('anywhere');
    expect(getComputedStyle(bubble).maxWidth).toBe('100%');
  });

  function openSettings(): void {
    const nativeElement = fixture.nativeElement as HTMLElement;
    (nativeElement.querySelector('.agent__settings-button') as HTMLButtonElement).click();
    fixture.detectChanges();
  }

  function findButton(label: string): HTMLButtonElement {
    const nativeElement = fixture.nativeElement as HTMLElement;
    const button = Array.from(nativeElement.querySelectorAll('button'))
      .find(item => item.textContent?.trim() === label || item.getAttribute('aria-label') === label);
    expect(button).toBeTruthy();
    return button as HTMLButtonElement;
  }

  function queryInput(placeholder: string): HTMLInputElement {
    const nativeElement = fixture.nativeElement as HTMLElement;
    const input = nativeElement.querySelector(`input[placeholder="${placeholder}"]`) as HTMLInputElement | null;
    expect(input).toBeTruthy();
    return input as HTMLInputElement;
  }

  function assistantMessage(overrides: Partial<ChatMessage> = {}): ChatMessage {
    return {
      id: 'assistant-message',
      role: 'assistant',
      type: 'text',
      text: 'Assistant response',
      timestamp: new Date(),
      ...overrides,
    };
  }

  function resultCard(overrides: NonNullable<ChatMessage['cards']>[number]): NonNullable<ChatMessage['cards']>[number] {
    return {
      subtitle: 'Dubrovnik',
      description: 'A compact GeoTrack result card.',
      imageUrl: null,
      locationName: 'Dubrovnik',
      rating: 4.5,
      reviewCount: 10,
      tags: [],
      distanceKm: null,
      dateText: null,
      stayType: null,
      placeType: null,
      ...overrides,
    };
  }

  function agentProvider(overrides: Partial<AgentProvider> = {}): AgentProvider {
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
});
