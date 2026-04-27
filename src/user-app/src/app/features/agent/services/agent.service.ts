import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { AnalyticsService } from '../../../core/services/analytics.service';
import type { ChatMessage, SuggestionPill } from '../models/agent.model';

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
  text: 'Ask Agent can search the GeoTrack catalog. Live AI planning is not connected yet.',
  suggestionPills: AGENT_SUGGESTION_PILLS,
  timestamp: new Date(),
};

interface ApiAgentResponse {
  id: string;
  role: string;
  type: string;
  text?: string;
  cardsTitle?: string;
  cards?: {
    id: number;
    entityType?: string;
    detailUrl?: string | null;
    title: string;
    description?: string;
    image?: string;
    rating?: number | null;
    durationHours?: number | null;
    distanceKm?: number | null;
    isFavorite: boolean;
  }[];
  suggestionPills?: { label: string; prompt: string }[];
  timestamp: string;
}

@Injectable({ providedIn: 'root' })
export class AgentService {
  private readonly http = inject(HttpClient);
  private readonly analytics = inject(AnalyticsService);
  private readonly _messages = signal<ChatMessage[]>([
    { ...AGENT_WELCOME_MESSAGE, timestamp: new Date() },
  ]);
  private readonly _isTyping = signal(false);
  private readonly _hasSentMessage = signal(false);

  readonly messages = this._messages.asReadonly();
  readonly isTyping = this._isTyping.asReadonly();
  readonly hasSentMessage = this._hasSentMessage.asReadonly();

  sendMessage(text: string): void {
    const trimmed = text.trim();
    if (!trimmed) return;

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

    // Privacy: never persist the prompt text via analytics — only its length
    // and a fast non-cryptographic hash for grouping similar prompts.
    this.analytics.track('agent_prompt_sent', {
      metadata: { length: trimmed.length, promptHash: hashPrompt(trimmed) },
    });

    this.http.post<ApiAgentResponse>('/api/agent/message', { text: trimmed }).subscribe({
      next: (res) => {
        this._isTyping.set(false);
        const response: ChatMessage = {
          id: res.id || crypto.randomUUID(),
          role: 'assistant',
          type: (res.type as ChatMessage['type']) || 'text',
          text: res.text ?? '',
          cardsTitle: res.cardsTitle,
          cards: res.cards?.map(c => ({
            id: String(c.id),
            entityType: c.entityType ?? 'place',
            detailUrl: c.detailUrl ?? null,
            title: c.title,
            description: c.description ?? '',
            image: c.image ?? '',
            rating: c.rating ?? null,
            durationHours: c.durationHours ?? null,
            distanceKm: c.distanceKm ?? null,
            isFavorite: c.isFavorite,
          })),
          suggestionPills: res.suggestionPills,
          timestamp: new Date(res.timestamp),
        };
        this._messages.update(msgs => [...msgs, response]);
      },
      error: () => {
        this._isTyping.set(false);
        this._messages.update(msgs => [...msgs, {
          id: crypto.randomUUID(),
          role: 'assistant',
          type: 'text',
          text: 'Ask Agent is unavailable right now. No offline or mock answer was shown.',
          timestamp: new Date(),
        }]);
      },
    });
  }
}

// Cheap djb2 hash. Returns an 8-char hex string. Not cryptographic — only
// good enough to group identical/very-similar prompts in admin analytics.
function hashPrompt(text: string): string {
  let hash = 5381;
  for (let i = 0; i < text.length; i++) {
    hash = ((hash << 5) + hash + text.charCodeAt(i)) >>> 0;
  }
  return hash.toString(16).padStart(8, '0');
}
