import { Component, computed, effect, ElementRef, HostListener, inject, signal, viewChild } from '@angular/core';
import { AgentService } from '../../services/agent.service';
import { AgentMessageBubbleComponent } from '../../components/agent-message-bubble/agent-message-bubble.component';
import { AgentSuggestionPillsComponent } from '../../components/agent-suggestion-pills/agent-suggestion-pills.component';
import { AgentRecommendationCardComponent } from '../../components/agent-recommendation-card/agent-recommendation-card.component';
import type { AgentKeyMode, SuggestionPill } from '../../models/agent.model';

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
  protected readonly apiKeyDraft = signal('');
  protected readonly settingsOpen = signal(false);
  protected readonly keyModeOptions: readonly { value: AgentKeyMode; label: string }[] = [
    { value: 'saved', label: 'Saved key' },
    { value: 'session', label: 'Temporary key' },
    { value: 'project-demo', label: 'Project demo' },
  ];
  protected readonly headerStatus = computed(() => {
    if (this.agentService.isLoadingProviders()) return 'Loading providers';

    const provider = this.agentService.selectedProvider();
    if (!provider) return 'No provider available';
    if (!provider.enabled) return `${provider.displayName} unavailable`;

    const model = this.agentService.selectedModels()
      .find(item => item.id === this.agentService.selectedModelId());
    const mode = this.keyModeOptions.find(item => item.value === this.agentService.keyMode())?.label;
    return [provider.displayName, model?.displayName, mode].filter(Boolean).join(' / ');
  });

  private readonly messagesContainer =
    viewChild<ElementRef<HTMLElement>>('messagesContainer');
  private readonly settingsButton =
    viewChild<ElementRef<HTMLButtonElement>>('settingsButton');
  private readonly settingsDialog =
    viewChild<ElementRef<HTMLElement>>('settingsDialog');

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

  protected openSettings(): void {
    this.settingsOpen.set(true);
    requestAnimationFrame(() => this.settingsDialog()?.nativeElement.focus());
  }

  protected closeSettings(): void {
    if (!this.settingsOpen()) return;
    this.settingsOpen.set(false);
    requestAnimationFrame(() => this.settingsButton()?.nativeElement.focus());
  }

  @HostListener('document:keydown.escape', ['$event'])
  protected onDocumentEscape(event: Event): void {
    if (!this.settingsOpen()) return;
    event.preventDefault();
    this.closeSettings();
  }

  protected onPillSelected(pill: SuggestionPill): void {
    this.agentService.sendMessage(pill.prompt);
  }

  protected onProviderChange(event: Event): void {
    this.apiKeyDraft.set('');
    this.agentService.selectProvider((event.target as HTMLSelectElement).value);
  }

  protected onModelChange(event: Event): void {
    this.agentService.selectModel((event.target as HTMLSelectElement).value);
  }

  protected onKeyModeSelected(mode: AgentKeyMode): void {
    this.apiKeyDraft.set('');
    this.agentService.setKeyMode(mode);
  }

  protected onSessionKeyChange(event: Event): void {
    this.agentService.setSessionApiKey((event.target as HTMLInputElement).value);
  }

  protected onApiKeyDraftChange(event: Event): void {
    this.apiKeyDraft.set((event.target as HTMLInputElement).value);
  }

  protected onSaveApiKey(): void {
    const value = this.apiKeyDraft().trim();
    if (!value) return;
    this.apiKeyDraft.set('');
    this.agentService.saveApiKey(value);
  }

  protected onDeleteApiKey(): void {
    this.apiKeyDraft.set('');
    this.agentService.deleteSavedKey();
  }

  protected onTestKey(): void {
    const key = this.agentService.keyMode() === 'session'
      ? this.agentService.sessionApiKey()
      : this.apiKeyDraft();
    this.agentService.testKey(key);
  }

  protected onTestSavedKey(): void {
    const draft = this.apiKeyDraft().trim();
    if (draft) {
      this.agentService.testKey(draft, 'session');
      return;
    }

    this.agentService.testKey('', 'saved');
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
