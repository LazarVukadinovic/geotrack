import { Component, computed, effect, ElementRef, HostListener, inject, signal, viewChild } from '@angular/core';
import { Router } from '@angular/router';
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
  private readonly router = inject(Router);
  protected readonly inputValue = signal('');
  protected readonly settingsOpen = signal(false);
  protected readonly authPromptOpen = signal(false);
  protected readonly isAuthenticated = computed(() => this.agentService.isLoggedIn());
  protected readonly inputPlaceholder = computed(() => this.isAuthenticated()
    ? 'Search the GeoTrack catalog...'
    : 'Sign in to use Ask Agent');
  protected readonly headerStatus = computed(() => {
    return this.agentService.isLoadingAgentInfo()
      ? 'Connecting to GeoTrack AI'
      : 'Powered by GeoTrack AI';
  });

  private readonly messagesContainer =
    viewChild<ElementRef<HTMLElement>>('messagesContainer');
  private readonly settingsButton =
    viewChild<ElementRef<HTMLButtonElement>>('settingsButton');
  private readonly settingsDialog =
    viewChild<ElementRef<HTMLElement>>('settingsDialog');
  private readonly authDialog =
    viewChild<ElementRef<HTMLElement>>('authDialog');

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
    if (!this.isAuthenticated()) {
      this.openAuthPrompt();
      return;
    }

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

  protected openAuthPrompt(): void {
    if (this.authPromptOpen()) return;
    this.authPromptOpen.set(true);
    requestAnimationFrame(() => this.authDialog()?.nativeElement.focus());
  }

  protected closeAuthPrompt(): void {
    if (!this.authPromptOpen()) return;
    this.authPromptOpen.set(false);
  }

  protected goToLogin(): void {
    this.closeAuthPrompt();
    void this.router.navigate(['/login']);
  }

  protected goToRegister(): void {
    this.closeAuthPrompt();
    void this.router.navigate(['/register']);
  }

  @HostListener('document:keydown.escape', ['$event'])
  protected onDocumentEscape(event: Event): void {
    if (this.authPromptOpen()) {
      event.preventDefault();
      this.closeAuthPrompt();
      return;
    }

    if (!this.settingsOpen()) return;
    event.preventDefault();
    this.closeSettings();
  }

  @HostListener('document:keydown.tab', ['$event'])
  protected onDocumentTab(event: Event): void {
    if (!this.settingsOpen() && !this.authPromptOpen()) return;

    const keyboardEvent = event as KeyboardEvent;
    const dialog = this.authPromptOpen()
      ? this.authDialog()?.nativeElement
      : this.settingsDialog()?.nativeElement;
    if (!dialog) return;

    const focusable = this.getFocusableElements(dialog);
    if (focusable.length === 0) {
      event.preventDefault();
      dialog.focus();
      return;
    }

    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    const active = document.activeElement as HTMLElement | null;

    if (keyboardEvent.shiftKey && (!active || active === first || !dialog.contains(active))) {
      keyboardEvent.preventDefault();
      last.focus();
      return;
    }

    if (!keyboardEvent.shiftKey && (!active || active === last || !dialog.contains(active))) {
      keyboardEvent.preventDefault();
      first.focus();
    }
  }

  protected onPillSelected(pill: SuggestionPill): void {
    if (!this.isAuthenticated()) {
      this.openAuthPrompt();
      return;
    }

    this.agentService.sendMessage(pill.prompt);
  }

  protected onInputKeydown(event: KeyboardEvent): void {
    if (!this.isAuthenticated()) {
      event.preventDefault();
      this.inputValue.set('');
      this.openAuthPrompt();
      return;
    }

    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.onSend();
    }
  }

  protected onInputChange(event: Event): void {
    if (!this.isAuthenticated()) {
      (event.target as HTMLInputElement).value = '';
      this.inputValue.set('');
      this.openAuthPrompt();
      return;
    }

    this.inputValue.set((event.target as HTMLInputElement).value);
  }

  protected onComposerClick(): void {
    if (!this.isAuthenticated()) {
      this.openAuthPrompt();
    }
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

  private getFocusableElements(dialog: HTMLElement): HTMLElement[] {
    return Array.from(dialog.querySelectorAll<HTMLElement>([
      'button:not([disabled])',
      'summary',
      'a[href]',
      'input:not([disabled])',
      'select:not([disabled])',
      'textarea:not([disabled])',
      '[tabindex]:not([tabindex="-1"])',
    ].join(','))).filter((el) => el.getAttribute('aria-hidden') !== 'true');
  }
}
