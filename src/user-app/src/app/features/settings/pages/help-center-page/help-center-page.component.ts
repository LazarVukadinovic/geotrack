import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { SettingsSubpageShellComponent } from '../../components/settings-subpage-shell/settings-subpage-shell.component';

interface HelpTopic {
  id: string;
  question: string;
  answer: string;
}

const HELP_TOPICS: HelpTopic[] = [
  {
    id: 'maps',
    question: 'Why does my map or nearby content look different from another city?',
    answer: 'GeoTrack tailors explore results to the destination you are browsing and, when allowed, to the location saved on your device. If the city feels off, clear the saved location in Privacy Settings and try again.',
  },
  {
    id: 'collections',
    question: 'How do collections and saved places work?',
    answer: 'Collections help you group places you want to revisit later, like cafes for a weekend trip or landmarks for a walking route. You can rename collections, remove items, and revisit them from your profile.',
  },
  {
    id: 'interests',
    question: 'How do I improve recommendations?',
    answer: 'The fastest way is to update My Interests and keep saving places that match your taste. GeoTrack uses those signals to tune destination ideas, local favorites, and travel suggestions.',
  },
  {
    id: 'agent',
    question: 'What is Ask Agent best used for?',
    answer: 'Ask Agent works well for trip ideas, local planning, and quick comparisons between neighborhoods, places, and events. If a response feels off, include that context in Contact Support so the team can review it.',
  },
  {
    id: 'account',
    question: 'What should I do if I cannot access my account?',
    answer: 'Start with Change Password in Settings. If you still cannot sign in or you notice account data that does not look right, send a support request from Contact Support and include the city or trip flow where the issue happened.',
  },
];

@Component({
  selector: 'app-help-center-page',
  standalone: true,
  imports: [CommonModule, SettingsSubpageShellComponent],
  templateUrl: './help-center-page.component.html',
  styleUrl: './help-center-page.component.css',
})
export class HelpCenterPageComponent {
  private readonly router = inject(Router);

  protected readonly topics = HELP_TOPICS;
  protected readonly openTopicId = signal<string | null>(HELP_TOPICS[0]?.id ?? null);

  protected toggleTopic(id: string): void {
    this.openTopicId.update((current) => current === id ? null : id);
  }

  protected openSupport(): void {
    void this.router.navigate(['/settings/contact-support']);
  }

  protected openPrivacy(): void {
    void this.router.navigate(['/settings/privacy-settings']);
  }
}
