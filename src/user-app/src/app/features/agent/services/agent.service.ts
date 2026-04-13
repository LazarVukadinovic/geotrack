import { Injectable, signal } from '@angular/core';
import type { ChatMessage } from '../models/agent.model';
import {
  AGENT_WELCOME_MESSAGE,
  MOCK_FALLBACK_RESPONSE,
  MOCK_RESPONSE_ENTRIES,
} from '../data/mock-agent.data';

/**
 * AgentService — owns all chat state for the Ask Agent feature.
 *
 * Architecture note: mock responses are produced by `buildMockResponse()`.
 * To connect a real MCP / REST backend, replace that method with an HTTP call
 * and pipe the Observable into the same `_messages` / `_isTyping` update flow.
 */
@Injectable({ providedIn: 'root' })
export class AgentService {
  private readonly _messages = signal<ChatMessage[]>([
    { ...AGENT_WELCOME_MESSAGE, timestamp: new Date() },
  ]);
  private readonly _isTyping = signal(false);
  private readonly _hasSentMessage = signal(false);

  readonly messages = this._messages.asReadonly();
  readonly isTyping = this._isTyping.asReadonly();
  /** True after the user has sent at least one message — used to hide suggestion pills. */
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

    // Simulate network latency. Replace setTimeout + buildMockResponse()
    // with a real HTTP / WebSocket call when connecting the backend.
    const delay = 1000 + Math.random() * 800;
    setTimeout(() => {
      this._isTyping.set(false);
      const response = this.buildMockResponse(trimmed);
      this._messages.update(msgs => [...msgs, response]);
    }, delay);
  }

  // ── Mock response logic (swap this for real API adapter) ──────────────────

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
