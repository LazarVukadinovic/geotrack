import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import type { ChatMessage } from '../models/agent.model';
import {
  AGENT_WELCOME_MESSAGE,
  MOCK_FALLBACK_RESPONSE,
  MOCK_RESPONSE_ENTRIES,
} from '../data/mock-agent.data';

interface ApiAgentResponse {
  id: string;
  role: string;
  type: string;
  text?: string;
  cardsTitle?: string;
  cards?: {
    id: number;
    title: string;
    description?: string;
    image?: string;
    rating: number;
    durationHours: number;
    distanceKm: number;
    isFavorite: boolean;
  }[];
  suggestionPills?: { label: string; prompt: string }[];
  timestamp: string;
}

@Injectable({ providedIn: 'root' })
export class AgentService {
  private readonly http = inject(HttpClient);
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
            title: c.title,
            description: c.description ?? '',
            image: c.image ?? '',
            rating: c.rating,
            durationHours: c.durationHours,
            distanceKm: c.distanceKm,
            isFavorite: c.isFavorite,
          })),
          suggestionPills: res.suggestionPills,
          timestamp: new Date(res.timestamp),
        };
        this._messages.update(msgs => [...msgs, response]);
      },
      error: () => {
        this._isTyping.set(false);
        // Fallback to mock response
        const response = this.buildMockResponse(trimmed);
        this._messages.update(msgs => [...msgs, response]);
      },
    });
  }

  private buildMockResponse(userText: string): ChatMessage {
    const lower = userText.toLowerCase();
    const entry = MOCK_RESPONSE_ENTRIES.find(e =>
      e.keywords.some(kw => lower.includes(kw)),
    );
    const payload = entry ? entry.build() : { ...MOCK_FALLBACK_RESPONSE };
    return {
      ...payload,
      id: crypto.randomUUID(),
      timestamp: new Date(),
    };
  }
}
