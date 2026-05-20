import { Component, OnInit, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { catchError, forkJoin, of } from 'rxjs';
import { TagService, UserInterestItem } from '../../../../core/services/tag.service';
import { UserService } from '../../../../core/services/user.service';
import { AnalyticsService } from '../../../../core/services/analytics.service';
import { Tag } from '../../../../core/models/tag.model';

@Component({
  selector: 'app-interests-page',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './interests-page.component.html',
  styleUrl: './interests-page.component.css',
})
export class InterestsPageComponent implements OnInit {
  readonly tags = signal<Tag[]>([]);
  readonly selectedTagIds = signal<Set<number>>(new Set());
  readonly isLoadingTags = signal(true);
  readonly isSaving = signal(false);
  readonly loadError = signal('');
  readonly saveError = signal('');
  readonly selectedCount = computed(() => this.selectedTagIds().size);
  readonly loadingPillIndexes = [0, 1, 2, 3, 4, 5];

  constructor(
    private router: Router,
    private tagService: TagService,
    private userService: UserService,
    private analytics: AnalyticsService,
  ) {}

  ngOnInit(): void {
    this.loadInterests();
  }

  toggleTag(tag: Tag): void {
    if (this.isLoadingTags() || this.isSaving()) return;

    this.selectedTagIds.update((current) => {
      const next = new Set(current);

      if (next.has(tag.id)) {
        next.delete(tag.id);
      } else {
        next.add(tag.id);
      }

      return next;
    });
  }

  isSelected(tag: Tag): boolean {
    return this.selectedTagIds().has(tag.id);
  }

  onFinish(): void {
    const selectedTagIds = [...this.selectedTagIds()];
    if (selectedTagIds.length === 0 || this.isLoadingTags() || this.isSaving()) return;
    this.completeOnboarding(selectedTagIds);
  }

  onSkip(): void {
    if (this.isSaving()) return;
    this.completeOnboarding([]);
  }

  goBack(): void {
    this.router.navigate(['/profile']);
  }

  retryLoad(): void {
    this.loadInterests();
  }

  private completeOnboarding(tagIds: number[]): void {
    this.isSaving.set(true);
    this.saveError.set('');

    this.tagService.saveUserTags(tagIds).subscribe({
      next: () => {
        this.analytics.track('interests_completed', {
          metadata: { tagCount: tagIds.length },
        });
        // Reload canonical user state so route guards and downstream screens
        // read the cleared onboarding flag immediately.
        this.userService.load().subscribe({
          next: () => {
            this.isSaving.set(false);
            this.router.navigate(['/explore']);
          },
          error: () => {
            this.isSaving.set(false);
            this.router.navigate(['/explore']);
          },
        });
      },
      error: () => {
        this.isSaving.set(false);
        this.saveError.set('Could not finish onboarding. Please try again.');
      },
    });
  }

  private loadInterests(): void {
    this.isLoadingTags.set(true);
    this.loadError.set('');
    this.saveError.set('');

    forkJoin({
      tags: this.tagService.loadTags(),
      selected: this.tagService.loadUserInterests().pipe(
        catchError(() => of({ tags: [] as UserInterestItem[] })),
      ),
    }).subscribe({
      next: ({ tags, selected }) => {
        this.tags.set(tags);

        const availableIds = new Set(tags.map(tag => tag.id));
        this.selectedTagIds.set(new Set(
          selected.tags
            .map(tag => tag.tagId)
            .filter(tagId => availableIds.has(tagId)),
        ));

        this.isLoadingTags.set(false);
      },
      error: () => {
        this.tags.set([]);
        this.selectedTagIds.set(new Set());
        this.isLoadingTags.set(false);
        this.loadError.set('Could not load interests right now. Please try again.');
      },
    });
  }
}
