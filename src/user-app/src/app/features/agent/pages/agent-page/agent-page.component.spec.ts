import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component, signal, type WritableSignal } from '@angular/core';
import { provideRouter, Router } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { EXPLORE_IMAGE_FALLBACK } from '../../../../core/constants/explore-media.constants';
import { AnalyticsService } from '../../../../core/services/analytics.service';
import { SaveToCollectionService } from '../../../../core/services/save-to-collection.service';
import { AgentService } from '../../services/agent.service';
import type { ChatMessage } from '../../models/agent.model';
import { AgentPageComponent } from './agent-page.component';

@Component({ standalone: true, template: '' })
class RouteTargetComponent {}

describe('AgentPageComponent', () => {
  let fixture: ComponentFixture<AgentPageComponent>;
  let messages: WritableSignal<ChatMessage[]>;
  let isLoadingAgentInfo: WritableSignal<boolean>;
  let agentService: {
    sendMessage: ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    vi.clearAllMocks();
    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
      return window.setTimeout(() => callback(performance.now()), 0);
    });
    vi.stubGlobal('cancelAnimationFrame', (id: number) => window.clearTimeout(id));

    messages = signal<ChatMessage[]>([
      {
        id: 'welcome',
        role: 'assistant',
        type: 'text',
        text: 'Ask GeoTrack about places, stays, or events.',
        suggestionPills: [
          { label: 'Top Attractions', prompt: 'Show me top attractions' },
        ],
        timestamp: new Date(),
      },
    ]);
    isLoadingAgentInfo = signal(false);
    agentService = {
      sendMessage: vi.fn(),
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
            mcp: signal({
              enabled: true,
              url: '/api/mcp',
              transport: 'remote-http',
              privateToolsEnabled: false,
            }),
            statusMessage: signal<string | null>(null),
            isLoadingAgentInfo,
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

  it('keeps settings out of the inline chat layout by default', () => {
    const nativeElement = fixture.nativeElement as HTMLElement;

    expect(nativeElement.querySelector('#agent-settings-dialog')).toBeNull();
    expect(nativeElement.querySelector('.agent__settings-button')?.getAttribute('aria-expanded')).toBe('false');
    expect(nativeElement.querySelector('.agent__settings-button')?.getAttribute('aria-label')).toBe('Agent settings');
  });

  it('renders backend-managed Ask Agent status in the header', () => {
    expect(headerStatusText()).toBe('Powered by GeoTrack AI');

    isLoadingAgentInfo.set(true);
    fixture.detectChanges();

    expect(headerStatusText()).toBe('Connecting to GeoTrack AI');
  });

  it('opens settings without provider, model, or key controls and keeps MCP accessible', () => {
    openSettings();

    const nativeElement = fixture.nativeElement as HTMLElement;
    const dialog = nativeElement.querySelector('#agent-settings-dialog') as HTMLElement | null;
    const text = dialog?.textContent ?? '';
    expect(dialog).toBeTruthy();
    expect(dialog?.getAttribute('role')).toBe('dialog');
    expect(text).toContain('Ask Agent is powered by GeoTrack AI.');
    expect(text).toContain('AI responses are generated through the GeoTrack backend.');
    expect(text).toContain('Use GeoTrack in external AI apps');
    expect(text).not.toContain('Provider');
    expect(text).not.toContain('Model');
    expect(text).not.toContain('Temporary key');
    expect(text).not.toContain('Saved key');
    expect(text).not.toContain('Test key');
    expect(text).not.toContain('Claude');

    findButton('Close Agent settings').click();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('#agent-settings-dialog')).toBeNull();
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

  function headerStatusText(): string {
    const nativeElement = fixture.nativeElement as HTMLElement;
    return nativeElement.querySelector('.agent__header-meta')?.textContent?.trim() ?? '';
  }

  function findButton(label: string): HTMLButtonElement {
    const nativeElement = fixture.nativeElement as HTMLElement;
    const button = Array.from(nativeElement.querySelectorAll('button'))
      .find(item => item.textContent?.trim() === label || item.getAttribute('aria-label') === label);
    expect(button).toBeTruthy();
    return button as HTMLButtonElement;
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
});
