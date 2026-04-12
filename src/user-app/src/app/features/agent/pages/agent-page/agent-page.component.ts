import { Component, effect, ElementRef, inject, signal, viewChild } from '@angular/core';
import { AgentService } from '../../services/agent.service';
import { AgentMessageBubbleComponent } from '../../components/agent-message-bubble/agent-message-bubble.component';
import { AgentSuggestionPillsComponent } from '../../components/agent-suggestion-pills/agent-suggestion-pills.component';
import { AgentRecommendationCardComponent } from '../../components/agent-recommendation-card/agent-recommendation-card.component';
import type { SuggestionPill } from '../../models/agent.model';

@Component({
  selector: 'app-agent-page',
  standalone: true,
  imports: [
    AgentMessageBubbleComponent,
    AgentSuggestionPillsComponent,
    AgentRecommendationCardComponent,
  ],
  templateUrl: './agent-page.component.html',
  styleUrl: './agent-page.component.scss',
})
export class AgentPageComponent {
  protected readonly agentService = inject(AgentService);
  protected readonly inputValue = signal('');

  private readonly messagesContainer =
    viewChild<ElementRef<HTMLElement>>('messagesContainer');

  constructor() {
    // Scroll to bottom whenever messages or typing state changes.
    //
    // iOS-specific note — double requestAnimationFrame is required:
    //   • First rAF: Angular has updated the DOM (new message element exists)
    //     but iOS has NOT yet committed the layout pass. scrollHeight at this
    //     point may still reflect the pre-message size.
    //   • Second rAF: iOS has committed layout. scrollHeight is now correct.
    //
    // A single rAF is reliable on desktop Chrome (synchronous layout) but
    // frequently reads a stale scrollHeight on iPhone Safari.
    effect(() => {
      this.agentService.messages();
      this.agentService.isTyping();
      requestAnimationFrame(() => {
        requestAnimationFrame(() => this.scrollToBottom());
      });
    });
  }

  protected onSend(): void {
    const text = this.inputValue().trim();
    if (!text) return;
    this.inputValue.set('');
    this.agentService.sendMessage(text);
  }

  protected onPillSelected(pill: SuggestionPill): void {
    this.agentService.sendMessage(pill.prompt);
  }

  protected onInputKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.onSend();
    }
  }

  protected onInputChange(event: Event): void {
    this.inputValue.set((event.target as HTMLInputElement).value);
  }

  private scrollToBottom(): void {
    const el = this.messagesContainer()?.nativeElement;
    if (!el) return;
    // Assign scrollTop directly (not via scrollTo + behavior:'smooth').
    // CSS scroll-behavior: smooth is removed from .agent__messages because on
    // iOS Safari, smooth-scroll animations are async — a second programmatic
    // scrollTop write during an in-progress animation is silently ignored,
    // which leaves newly added messages off-screen on real iPhone.
    el.scrollTop = el.scrollHeight;
  }
}
