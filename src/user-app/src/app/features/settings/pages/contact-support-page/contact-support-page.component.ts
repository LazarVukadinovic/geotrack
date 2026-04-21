import { CommonModule, DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { SettingsSubpageShellComponent } from '../../components/settings-subpage-shell/settings-subpage-shell.component';
import { SupportRequestItem } from '../../models/user-settings.model';
import { UserSettingsService } from '../../services/user-settings.service';

interface SupportCategoryOption {
  value: string;
  label: string;
  helper: string;
}

const SUPPORT_CATEGORIES: SupportCategoryOption[] = [
  {
    value: 'account-access',
    label: 'Account access',
    helper: 'Sign-in issues, password trouble, or missing profile details.',
  },
  {
    value: 'saved-places-and-collections',
    label: 'Saved places',
    helper: 'Collections, favorites, or anything that is not sticking the way you expect.',
  },
  {
    value: 'location-and-maps',
    label: 'Location and maps',
    helper: 'Wrong city, map pins that feel off, or nearby content that does not match your trip.',
  },
  {
    value: 'ask-agent',
    label: 'Ask Agent',
    helper: 'Responses that felt incorrect, incomplete, or not useful for planning.',
  },
  {
    value: 'events-and-stays',
    label: 'Events and stays',
    helper: 'Travel listings, event details, or place information that needs review.',
  },
  {
    value: 'privacy-and-data',
    label: 'Privacy and data',
    helper: 'Questions about stored data, location use, or account cleanup.',
  },
];

@Component({
  selector: 'app-contact-support-page',
  standalone: true,
  imports: [CommonModule, DatePipe, SettingsSubpageShellComponent],
  templateUrl: './contact-support-page.component.html',
  styleUrl: './contact-support-page.component.css',
})
export class ContactSupportPageComponent {
  private readonly settingsService = inject(UserSettingsService);

  protected readonly categories = SUPPORT_CATEGORIES;
  protected readonly selectedCategory = signal<string>(SUPPORT_CATEGORIES[0].value);
  protected readonly subject = signal('');
  protected readonly message = signal('');
  protected readonly requests = signal<SupportRequestItem[]>([]);
  protected readonly isLoading = signal(true);
  protected readonly isSaving = signal(false);
  protected readonly hasSubmitted = signal(false);
  protected readonly feedbackMessage = signal('');
  protected readonly errorMessage = signal('');

  protected readonly selectedCategoryHelper = computed(() =>
    this.categories.find((category) => category.value === this.selectedCategory())?.helper ?? '',
  );

  protected readonly subjectError = computed(() => {
    if (!this.hasSubmitted() && !this.subject()) return '';
    const subject = this.subject().trim();

    if (subject.length === 0) {
      return 'Add a short subject so the team can triage your request quickly.';
    }

    if (subject.length < 6) {
      return 'Use at least 6 characters for the subject.';
    }

    return '';
  });

  protected readonly messageError = computed(() => {
    if (!this.hasSubmitted() && !this.message()) return '';
    const message = this.message().trim();

    if (message.length === 0) {
      return 'Tell us what happened and where in the app you noticed it.';
    }

    if (message.length < 20) {
      return 'Add a little more detail so your issue can be reproduced.';
    }

    return '';
  });

  protected readonly canSubmit = computed(() =>
    !this.isSaving() &&
    this.subjectError() === '' &&
    this.messageError() === '' &&
    this.subject().trim().length > 0 &&
    this.message().trim().length > 0,
  );

  constructor() {
    this.settingsService.getSupportRequests().subscribe({
      next: (response) => {
        this.requests.set(response.requests);
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
        this.errorMessage.set('We could not load your previous support requests right now.');
      },
    });
  }

  protected selectCategory(value: string): void {
    this.selectedCategory.set(value);
  }

  protected submit(): void {
    this.hasSubmitted.set(true);
    this.feedbackMessage.set('');
    this.errorMessage.set('');

    if (!this.canSubmit()) {
      return;
    }

    this.isSaving.set(true);

    this.settingsService.createSupportRequest({
      category: this.selectedCategory(),
      subject: this.subject().trim(),
      message: this.message().trim(),
    }).subscribe({
      next: (request) => {
        this.isSaving.set(false);
        this.requests.update((items) => [request, ...items.filter((item) => item.id !== request.id)].slice(0, 5));
        this.subject.set('');
        this.message.set('');
        this.hasSubmitted.set(false);
        this.feedbackMessage.set(`Support request saved. Reference ${request.referenceCode}.`);
      },
      error: (error: { error?: { message?: string } }) => {
        this.isSaving.set(false);
        this.errorMessage.set(error.error?.message ?? 'We could not save your support request right now.');
      },
    });
  }

  protected categoryLabel(value: string): string {
    return this.categories.find((category) => category.value === value)?.label ?? 'Support request';
  }
}
