import { inject, Injectable, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { AnalyticsService } from '../../../core/services/analytics.service';
import { AuthService } from '../../../core/services/auth.service';
import { DestinationService } from '../../../core/services/destination.service';
import { LocationService } from '../../../core/services/location.service';
import type {
  AgentContextOptions,
  AgentProvidersResponse,
  AgentResultCard,
  AgentResultEntityType,
  ChatMessage,
  SuggestionPill,
} from '../models/agent.model';

const AGENT_SUGGESTION_PILLS: SuggestionPill[] = [
  { label: 'Top Attractions', prompt: 'Show me top attractions' },
  { label: 'Best Restaurants', prompt: 'Recommend restaurants' },
  { label: 'Stays', prompt: 'Show me places to stay' },
  { label: 'Events', prompt: 'What events are happening?' },
];

const AGENT_WELCOME_MESSAGE: ChatMessage = {
  id: 'welcome',
  role: 'assistant',
  type: 'text',
  text: 'Ask GeoTrack about places, stays, or events.',
  suggestionPills: AGENT_SUGGESTION_PILLS,
  timestamp: new Date(),
};

const ASK_AGENT_UNAVAILABLE_MESSAGE = 'Ask Agent is currently unavailable. Please try again later.';

const LEGACY_AGENT_STORAGE_KEYS = [
  'geotrack_agent_provider',
  'geotrack_agent_model',
  'geotrack_agent_key_mode',
  'geotrack_agent_session_api_key',
  'geotrack_agent_temporary_key',
  'geotrack_ai_provider',
  'geotrack_ai_model',
  'geotrack_ai_key_mode',
];

interface ApiAgentChatResponse {
  id: string;
  conversationId: string;
  role: string;
  type: string;
  text: string;
  toolsUsed?: string[];
  cards?: AgentResultCard[];
  timestamp: string;
}

@Injectable({ providedIn: 'root' })
export class AgentService {
  private readonly http = inject(HttpClient);
  private readonly analytics = inject(AnalyticsService);
  private readonly auth = inject(AuthService);
  private readonly destination = inject(DestinationService);
  private readonly location = inject(LocationService);
  private readonly _messages = signal<ChatMessage[]>([
    { ...AGENT_WELCOME_MESSAGE, timestamp: new Date() },
  ]);
  private readonly _isTyping = signal(false);
  private readonly _hasSentMessage = signal(false);
  private readonly _mcp = signal<AgentProvidersResponse['mcp'] | null>(null);
  private readonly _statusMessage = signal<string | null>(null);
  private readonly _isLoadingAgentInfo = signal(false);
  private conversationId = createConversationId();

  readonly messages = this._messages.asReadonly();
  readonly isTyping = this._isTyping.asReadonly();
  readonly hasSentMessage = this._hasSentMessage.asReadonly();
  readonly mcp = this._mcp.asReadonly();
  readonly statusMessage = this._statusMessage.asReadonly();
  readonly isLoadingAgentInfo = this._isLoadingAgentInfo.asReadonly();
  readonly isLoggedIn = this.auth.isLoggedIn;

  constructor() {
    cleanupLegacyByokStorage();
    this.loadAgentInfo();
  }

  loadAgentInfo(): void {
    this._isLoadingAgentInfo.set(true);
    this.http.get<AgentProvidersResponse>('/api/agent/providers').subscribe({
      next: (response) => {
        this._isLoadingAgentInfo.set(false);
        this._mcp.set(response.mcp ?? null);
      },
      error: () => {
        this._isLoadingAgentInfo.set(false);
        this._statusMessage.set('Ask Agent information is unavailable right now.');
      },
    });
  }

  sendMessage(text: string): void {
    const trimmed = text.trim();
    if (!trimmed) return;

    if (!this.auth.isLoggedIn()) {
      this.appendAssistantMessage('Please sign in before using Ask Agent.');
      return;
    }

    const userMsg: ChatMessage = {
      id: crypto.randomUUID(),
      role: 'user',
      type: 'text',
      text: trimmed,
      timestamp: new Date(),
    };

    this._messages.update(msgs => [...msgs, userMsg]);
    this._hasSentMessage.set(true);
    this._isTyping.set(true);
    this._statusMessage.set(null);

    this.analytics.track('agent_prompt_sent', {
      metadata: { length: trimmed.length, promptHash: hashPrompt(trimmed) },
    });

    this.http.post<ApiAgentChatResponse>('/api/agent/chat', {
      message: trimmed,
      conversationId: this.conversationId,
      contextOptions: this.buildSafeContextOptions(),
    }).subscribe({
      next: (res) => {
        this._isTyping.set(false);
        this.conversationId = cleanContextText(res.conversationId) ?? this.conversationId;
        const cards = normalizeCards(res.cards);
        const response: ChatMessage = {
          id: res.id || crypto.randomUUID(),
          role: 'assistant',
          type: 'text',
          text: res.text ?? '',
          cards,
          timestamp: new Date(res.timestamp),
        };
        this._messages.update(msgs => [...msgs, response]);
      },
      error: (error: HttpErrorResponse) => {
        this._isTyping.set(false);
        this.appendAssistantMessage(extractSafeError(error, ASK_AGENT_UNAVAILABLE_MESSAGE));
      },
    });
  }

  startNewChat(): void {
    this.conversationId = createConversationId();
    this._messages.set([{ ...AGENT_WELCOME_MESSAGE, timestamp: new Date() }]);
    this._hasSentMessage.set(false);
    this._isTyping.set(false);
    this._statusMessage.set(null);
  }

  private appendAssistantMessage(text: string): void {
    this._messages.update(msgs => [...msgs, {
      id: crypto.randomUUID(),
      role: 'assistant',
      type: 'text',
      text,
      timestamp: new Date(),
    }]);
  }

  private buildSafeContextOptions(): AgentContextOptions | undefined {
    const context: AgentContextOptions = {};
    const destinationId = this.destination.id();
    const destinationName = cleanContextText(this.destination.name());
    const currentLocation = this.location.location();
    const currentCity = cleanContextText(currentLocation?.city || this.location.displayCity());
    const currentLocationName = cleanContextText(currentLocation?.displayLabel);

    if (hasUsableCoordinates(currentLocation?.latitude, currentLocation?.longitude)) {
      context.latitude = currentLocation!.latitude;
      context.longitude = currentLocation!.longitude;
      const source = cleanContextText(currentLocation?.source);
      if (source) {
        context.locationSource = source;
      }
    }

    if (typeof destinationId === 'number' && destinationId > 0) {
      context.destinationId = destinationId;
    }

    if (destinationName) {
      context.currentDestinationName = destinationName;
    }

    if (currentCity) {
      context.currentCity = currentCity;
    }

    if (currentLocationName && currentLocationName !== currentCity) {
      context.currentLocationName = currentLocationName;
    }

    const language = typeof navigator !== 'undefined'
      ? cleanContextText(navigator.language)
      : null;
    if (language) {
      context.language = language;
    }

    return Object.keys(context).length > 0 ? context : undefined;
  }
}

function extractSafeError(error: HttpErrorResponse, fallback: string): string {
  if (error.status === 401) return 'Please sign in before using Ask Agent.';
  const message = typeof error.error?.message === 'string' ? error.error.message : '';
  if (error.status === 429) {
    return message.length > 0 && message.length <= 180 ? message : 'Too many requests. Please try again later.';
  }
  return message.length > 0 && message.length <= 180 ? message : fallback;
}

function cleanupLegacyByokStorage(): void {
  const storages = [
    typeof localStorage === 'undefined' ? null : localStorage,
    typeof sessionStorage === 'undefined' ? null : sessionStorage,
  ];

  for (const storage of storages) {
    if (!storage) continue;
    for (const key of LEGACY_AGENT_STORAGE_KEYS) {
      try {
        storage.removeItem(key);
      } catch {
        // Storage may be unavailable in private browsing or locked-down contexts.
      }
    }
  }
}

function hashPrompt(text: string): string {
  let hash = 5381;
  for (let i = 0; i < text.length; i++) {
    hash = ((hash << 5) + hash + text.charCodeAt(i)) >>> 0;
  }
  return hash.toString(16).padStart(8, '0');
}

function cleanContextText(value: string | null | undefined): string | null {
  const cleaned = (value ?? '').trim().replace(/\s+/g, ' ');
  if (!cleaned || cleaned === 'Explore' || cleaned === 'nearby location') return null;
  return cleaned.length <= 100 ? cleaned : cleaned.slice(0, 100);
}

function normalizeCards(cards: AgentResultCard[] | null | undefined): AgentResultCard[] {
  return (cards ?? [])
    .map(card => normalizeCard(card))
    .filter((card): card is AgentResultCard => card !== null)
    .slice(0, 5);
}

function normalizeCard(card: AgentResultCard): AgentResultCard | null {
  const entityType = normalizeEntityType(card.entityType);
  const id = Number(card.id);
  const title = cleanCardText(card.title, 120);
  if (!entityType || !Number.isInteger(id) || id <= 0 || !title) {
    return null;
  }

  const detailRoute = normalizeDetailRoute(card.detailRoute, entityType, id);
  if (!detailRoute) {
    return null;
  }

  return {
    entityType,
    id,
    title,
    subtitle: cleanCardText(card.subtitle, 160),
    description: cleanCardText(card.description, 220),
    imageUrl: cleanImageUrl(card.imageUrl),
    locationName: cleanCardText(card.locationName, 120),
    rating: finiteNumberOrNull(card.rating),
    reviewCount: Number.isInteger(card.reviewCount) && card.reviewCount! >= 0 ? card.reviewCount! : null,
    distanceKm: finiteNumberOrNull(card.distanceKm),
    tags: (card.tags ?? []).map(tag => cleanCardText(tag, 40)).filter((tag): tag is string => !!tag).slice(0, 4),
    dateText: cleanCardText(card.dateText, 80),
    stayType: cleanCardText(card.stayType, 80),
    placeType: cleanCardText(card.placeType, 80),
    detailRoute,
  };
}

function normalizeEntityType(value: string | null | undefined): AgentResultEntityType | null {
  const normalized = (value ?? '').trim().toLowerCase();
  if (normalized === 'place' || normalized === 'stay' || normalized === 'event') {
    return normalized;
  }

  return null;
}

function normalizeDetailRoute(
  detailRoute: string | null | undefined,
  entityType: AgentResultEntityType,
  id: number,
): string | null {
  const expected = entityType === 'place'
    ? `/places/${id}`
    : entityType === 'stay'
      ? `/stays/${id}`
      : `/events/${id}`;
  const route = (detailRoute ?? '').trim();
  return route === expected ? route : expected;
}

function cleanImageUrl(value: string | null | undefined): string | null {
  const cleaned = (value ?? '').trim();
  if (!cleaned || cleaned.length > 2048 || /[\s\u0000-\u001f]/.test(cleaned)) return null;
  const publicPath = normalizePublicImagePath(cleaned);
  if (publicPath) return publicPath;

  try {
    const url = new URL(cleaned);
    if (url.protocol === 'https:') return cleaned;
    if (url.protocol === 'http:' && isLoopbackHost(url.hostname)) {
      return normalizePublicImagePath(`${url.pathname}${url.search}${url.hash}`);
    }
    return null;
  } catch {
    return null;
  }
}

function normalizePublicImagePath(value: string): string | null {
  if (value.includes('\\') || value.startsWith('//')) return null;
  let candidate = value;
  if (candidate.startsWith('media/') || candidate.startsWith('images/')) {
    candidate = `/${candidate}`;
  }

  if (!candidate.startsWith('/media/') && !candidate.startsWith('/images/')) {
    return null;
  }

  const path = candidate.split(/[?#]/, 1)[0] ?? '';
  if (
    /(?:^|\/)\.{1,2}(?:\/|$)/.test(path) ||
    /%2e|%2f|%5c/i.test(path)
  ) {
    return null;
  }

  return candidate;
}

function isLoopbackHost(hostname: string): boolean {
  const normalized = hostname.toLowerCase();
  return normalized === 'localhost' ||
    normalized === '127.0.0.1' ||
    normalized === '::1' ||
    normalized === '[::1]';
}

function cleanCardText(value: string | null | undefined, maxLength: number): string | null {
  const cleaned = (value ?? '').trim().replace(/\s+/g, ' ');
  if (!cleaned) return null;
  return cleaned.length <= maxLength ? cleaned : `${cleaned.slice(0, maxLength)}...`;
}

function finiteNumberOrNull(value: number | null | undefined): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

function createConversationId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }

  return `chat-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
}

function hasUsableCoordinates(latitude: number | null | undefined, longitude: number | null | undefined): boolean {
  return typeof latitude === 'number' &&
    typeof longitude === 'number' &&
    Number.isFinite(latitude) &&
    Number.isFinite(longitude) &&
    latitude >= -90 &&
    latitude <= 90 &&
    longitude >= -180 &&
    longitude <= 180 &&
    (latitude !== 0 || longitude !== 0);
}
