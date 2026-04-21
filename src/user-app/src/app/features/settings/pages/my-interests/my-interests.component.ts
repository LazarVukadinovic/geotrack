import { Component, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, ActivatedRoute } from '@angular/router';
import { catchError, forkJoin, of } from 'rxjs';
import { TagService } from '../../../../core/services/tag.service';
import { UserService } from '../../../../core/services/user.service';
import { Tag } from '../../../../core/models/tag.model';

@Component({
  selector: 'app-my-interests',
  imports: [CommonModule],
  templateUrl: './my-interests.component.html',
  styleUrl: './my-interests.component.css',
})
export class MyInterestsComponent {
  readonly isLoading = signal(true);
  readonly isSaving = signal(false);
  readonly loadError = signal('');
  readonly saveError = signal('');
  readonly tags = signal<Tag[]>([]);
  readonly selectedTags = signal<Tag[]>([]);
  readonly availableTags = computed(() => {
    const selectedIds = new Set(this.selectedTags().map(tag => tag.id));
    return this.tags().filter(tag => !selectedIds.has(tag.id));
  });
  private returnTo = '/settings';

  constructor(
    private router: Router,
    private route: ActivatedRoute,
    private tagService: TagService,
    private userService: UserService,
  ) {
    this.route.queryParams.subscribe(p => {
      if (p['from']) this.returnTo = '/' + p['from'];
    });

    this.loadInterests();
  }

  addTag(tag: Tag): void {
    if (!this.isSelected(tag)) {
      this.selectedTags.update(tags => [...tags, tag]);
    }
  }

  removeTag(tag: Tag): void {
    this.selectedTags.update(tags => tags.filter(t => t.id !== tag.id));
  }

  isSelected(tag: Tag): boolean {
    return this.selectedTags().some(t => t.id === tag.id);
  }

  saveChanges(): void {
    if (this.isSaving() || this.isLoading()) return;
    this.isSaving.set(true);
    this.saveError.set('');

    const tagIds = this.selectedTags().map(t => t.id);

    this.tagService.saveUserTags(tagIds).subscribe({
      next: () => {
        // Refresh canonical user state before navigating so downstream pages
        // read fresh interests from UserService.
        this.userService.load().subscribe({
          next: () => {
            this.isSaving.set(false);
            this.router.navigate([this.returnTo]);
          },
          error: () => {
            this.isSaving.set(false);
            this.router.navigate([this.returnTo]);
          },
        });
      },
      error: () => {
        this.isSaving.set(false);
        this.saveError.set('Could not save your interests. Please try again.');
      },
    });
  }

  goBack(): void {
    this.router.navigate([this.returnTo]);
  }

  retryLoad(): void {
    this.loadInterests();
  }

  private loadInterests(): void {
    this.isLoading.set(true);
    this.loadError.set('');
    this.saveError.set('');
    this.selectedTags.set([]);

    forkJoin({
      tags: this.tagService.loadTags(),
      selected: this.tagService.loadUserInterests().pipe(
        catchError(() => of({ tags: [] })),
      ),
    }).subscribe({
      next: ({ tags, selected }) => {
        this.tags.set(tags);
        const ids = new Set(selected.tags.map(tag => tag.tagId));
        this.selectedTags.set(tags.filter(tag => ids.has(tag.id)));
        this.isLoading.set(false);
      },
      error: () => {
        this.tags.set([]);
        this.selectedTags.set([]);
        this.isLoading.set(false);
        this.loadError.set('Could not load your interests. Please try again.');
      },
    });
  }
}
