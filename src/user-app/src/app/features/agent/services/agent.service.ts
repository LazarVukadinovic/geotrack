import { computed, inject, Injectable, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { AnalyticsService } from '../../../core/services/analytics.service';
import { AuthService } from '../../../core/services/auth.service';
import { DestinationService } from '../../../core/services/destination.service';
import { LocationService } from '../../../core/services/location.service';
import type {
  AgentApiKeyMetadata,
  AgentContextOptions,
  AgentKeyMode,
  AgentProvider,
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
  text: 'Choose an AI provider, then ask GeoTrack about places, stays, or events.',
  suggestionPills: AGENT_SUGGESTION_PILLS,
  timestamp: new Date(),
};

const SAVED_KEY_MISSING_MESSAGE = 'Save an API key for this provider before using saved-key mode.';

interface ApiAgentChatResponse {
  id: string;
  conversationId: string;
  role: string;
  type: string;
  text: string;
  provider: string;
  model: string;
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
  private readonly _providers = signal<AgentProvider[]>([]);
  private readonly _mcp = signal<AgentProvidersResponse['mcp'] | null>(null);
  private readonly _selectedProviderId = signal<string | null>(null);
  private readonly _selectedModelId = signal<string | null>(null);
  private readonly _keyMode = signal<AgentKeyMode>('project-demo');
  private readonly _sessionApiKey = signal('');
  private readonly _statusMessage = signal<string | null>(null);
  private readonly _isSavingKey = signal(false);
  private readonly _isTestingKey = signal(false);
  private readonly _isLoadingProviders = signal(false);

  readonly messages = this._messages.asReadonly();
  readonly isTyping = this._isTyping.asReadonly();
  readonly hasSentMessage = this._hasSentMessage.asReadonly();
  readonly providers = this._providers.asReadonly();
  readonly mcp = this._mcp.asReadonly();
  readonly selectedProviderId = this._selectedProviderId.asReadonly();
  readonly selectedModelId = this._selectedModelId.asReadonly();
  readonly keyMode = this._keyMode.asReadonly();
  readonly sessionApiKey = this._sessionApiKey.asReadonly();
  readonly statusMessage = this._statusMessage.asReadonly();
  readonly isSavingKey = this._isSavingKey.asReadonly();
  readonly isTestingKey = this._isTestingKey.asReadonly();
  readonly isLoadingProviders = this._isLoadingProviders.asReadonly();
  readonly isLoggedIn = this.auth.isLoggedIn;

  readonly enabledProviders = computed(() => this._providers().filter(provider => provider.enabled));
  readonly selectedProvider = computed(() => {
    const selectedId = this._selectedProviderId();
    return this._providers().find(provider => provider.id === selectedId) ?? null;
  });
  readonly selectedModels = computed(() => this.selectedProvider()?.models ?? []);
  readonly availableKeyModes = computed<AgentKeyMode[]>(() => {
    const provider = this.selectedProvider();
    if (!provider?.enabled) return [];

    const modes: AgentKeyMode[] = [];
    if (provider.supportsSavedKey && provider.hasSavedKey) modes.push('saved');
    if (provider.supportsSessionKey) modes.push('session');
    if (provider.projectDemoKeyEnabled) modes.push('project-demo');
    return modes;
  });

  constructor() {
    this.loadProviders();
  }

  loadProviders(): void {
    this._isLoadingProviders.set(true);
    this.http.get<AgentProvidersResponse>('/api/agent/providers').subscribe({
      next: (response) => {
        this._isLoadingProviders.set(false);
        this._providers.set(response.providers ?? []);
        this._mcp.set(response.mcp ?? null);
        this.ensureSelection();
      },
      error: () => {
        this._isLoadingProviders.set(false);
        this._statusMessage.set('Ask Agent settings are unavailable right now.');
      },
    });
  }

  selectProvider(providerId: string): void {
    const provider = this._providers().find(item => item.id === providerId);
    if (!provider) return;
    if (!provider.enabled && this.enabledProviders().length > 0) return;
    this._selectedProviderId.set(provider.id);
    this.chooseDefaultModel();
    this.chooseDefaultKeyMode();
    this._statusMessage.set(null);
  }

  selectModel(modelId: string): void {
    if (!this.selectedModels().some(model => model.id === modelId)) return;
    this._selectedModelId.set(modelId);
  }

  setKeyMode(mode: AgentKeyMode): void {
    if (!this.availableKeyModes().includes(mode)) return;
    this._keyMode.set(mode);
    this._statusMessage.set(null);
  }

  setSessionApiKey(value: string): void {
    this._sessionApiKey.set(value);
  }

  saveApiKey(apiKey: string): void {
    const provider = this.selectedProvider();
    const normalized = apiKey.trim();
    if (!provider || normalized.length < 8) {
      this._statusMessage.set('Enter a valid API key before saving.');
      return;
    }

    this._isSavingKey.set(true);
    this.http.post<AgentApiKeyMetadata>('/api/agent/api-keys', {
      provider: provider.id,
      apiKey: normalized,
    }).subscribe({
      next: (savedKey) => {
        this._isSavingKey.set(false);
        this.applySavedKeyMetadata(savedKey);
        this._statusMessage.set('Saved key updated.');
        this._keyMode.set('saved');
        this.clearAssistantMessagesByText(SAVED_KEY_MISSING_MESSAGE);
        this.loadProviders();
      },
      error: (error: HttpErrorResponse) => {
        this._isSavingKey.set(false);
        this._statusMessage.set(extractSafeError(error, 'Could not save that API key.'));
      },
    });
  }

  deleteSavedKey(): void {
    const provider = this.selectedProvider();
    if (!provider) return;
    const providerId = provider.id;

    this._isSavingKey.set(true);
    this.http.delete(`/api/agent/api-keys/${encodeURIComponent(providerId)}`).subscribe({
      next: () => {
        this._isSavingKey.set(false);
        this.clearSavedKeyMetadata(providerId);
        this.chooseDefaultKeyMode();
        this._statusMessage.set('Saved key deleted.');
        this.loadProviders();
      },
      error: (error: HttpErrorResponse) => {
        this._isSavingKey.set(false);
        this._statusMessage.set(extractSafeError(error, 'Could not delete the saved key.'));
      },
    });
  }

  testKey(apiKey: string, keyModeOverride?: AgentKeyMode): void {
    const provider = this.selectedProvider();
    const model = this._selectedModelId();
    if (!provider || !model) return;
    const keyMode = keyModeOverride ?? this._keyMode();

    this._isTestingKey.set(true);
    this.http.post<{ success: boolean; message: string }>('/api/agent/api-keys/test', {
      provider: provider.id,
      model,
      keyMode,
      apiKey: keyMode === 'session' ? apiKey.trim() : undefined,
    }).subscribe({
      next: (response) => {
        this._isTestingKey.set(false);
        this._statusMessage.set(response.message || 'Provider check completed.');
      },
      error: (error: HttpErrorResponse) => {
        this._isTestingKey.set(false);
        this._statusMessage.set(extractSafeError(error, 'Provider check failed.'));
      },
    });
  }

  sendMessage(text: string): void {
    const trimmed = text.trim();
    if (!trimmed) return;

    const provider = this.selectedProvider();
    const model = this._selectedModelId();
    const keyMode = this._keyMode();
    const sessionApiKey = this._sessionApiKey().trim();
    if (keyMode === 'session') {
      this._sessionApiKey.set('');
    }

    if (!this.auth.isLoggedIn()) {
      this.appendAssistantMessage('Please sign in before using Ask Agent.');
      return;
    }

    if (!provider?.enabled || !model) {
      this.appendAssistantMessage('Select an available AI provider and model first.');
      return;
    }

    if (keyMode === 'saved' && !provider.hasSavedKey) {
      this.appendAssistantMessage(SAVED_KEY_MISSING_MESSAGE);
      return;
    }

    if (keyMode === 'session' && sessionApiKey.length < 8) {
      this.appendAssistantMessage('Enter a temporary API key before sending.');
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
      provider: provider.id,
      model,
      keyMode,
      sessionApiKey: keyMode === 'session' ? sessionApiKey : undefined,
      contextOptions: this.buildSafeContextOptions(),
    }).subscribe({
      next: (res) => {
        this._isTyping.set(false);
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
        this.appendAssistantMessage(extractSafeError(
          error,
          'Ask Agent is unavailable right now. No offline or mock answer was shown.',
        ));
      },
    });
  }

  private ensureSelection(): void {
    const providers = this._providers();
    const currentProvider = this._selectedProviderId();
    const current = providers.find(provider => provider.id === currentProvider);
    const enabledProviders = providers.filter(provider => provider.enabled);
    if (!current || (!current.enabled && enabledProviders.length > 0)) {
      const firstEnabled = enabledProviders[0];
      this._selectedProviderId.set(firstEnabled?.id ?? providers[0]?.id ?? null);
    }

    this.chooseDefaultModel();
    this.chooseDefaultKeyMode();
  }

  private chooseDefaultModel(): void {
    const models = this.selectedModels();
    const current = this._selectedModelId();
    if (current && models.some(model => model.id === current)) return;
    const selected = models.find(model => model.isDefault) ?? models[0] ?? null;
    this._selectedModelId.set(selected?.id ?? null);
  }

  private chooseDefaultKeyMode(): void {
    const modes = this.availableKeyModes();
    const current = this._keyMode();
    if (modes.includes(current)) return;

    const provider = this.selectedProvider();
    if (provider?.hasSavedKey && modes.includes('saved')) {
      this._keyMode.set('saved');
      return;
    }

    if (modes.includes('project-demo')) {
      this._keyMode.set('project-demo');
      return;
    }

    this._keyMode.set(modes[0] ?? 'session');
  }

  private applySavedKeyMetadata(savedKey: AgentApiKeyMetadata): void {
    const providerId = normalizeId(savedKey.provider);
    if (!providerId) return;

    this._providers.update(providers => providers.map(provider =>
      sameId(provider.id, providerId)
        ? {
            ...provider,
            hasSavedKey: true,
            keyLast4: savedKey.keyLast4,
            keyUpdatedAt: savedKey.updatedAt,
          }
        : provider,
    ));
  }

  private clearSavedKeyMetadata(providerId: string): void {
    this._providers.update(providers => providers.map(provider =>
      sameId(provider.id, providerId)
        ? {
            ...provider,
            hasSavedKey: false,
            keyLast4: null,
            keyUpdatedAt: null,
          }
        : provider,
    ));
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

  private clearAssistantMessagesByText(text: string): void {
    this._messages.update(msgs => msgs.filter(message =>
      !(message.role === 'assistant' && message.text === text),
    ));
  }

  private buildSafeContextOptions(): AgentContextOptions | undefined {
    const context: AgentContextOptions = {};
    const destinationId = this.destination.id();
    const destinationName = cleanContextText(this.destination.name());
    const currentLocation = this.location.location();
    const currentCity = cleanContextText(currentLocation?.city || this.location.displayCity());
    const currentLocationName = cleanContextText(currentLocation?.displayLabel);

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

function hashPrompt(text: string): string {
  let hash = 5381;
  for (let i = 0; i < text.length; i++) {
    hash = ((hash << 5) + hash + text.charCodeAt(i)) >>> 0;
  }
  return hash.toString(16).padStart(8, '0');
}

function normalizeId(value: string | null | undefined): string {
  return (value ?? '').trim().toLowerCase();
}

function sameId(left: string, right: string): boolean {
  return normalizeId(left) === normalizeId(right);
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
