import { Component, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, ActivatedRoute } from '@angular/router';
import { catchError, forkJoin, of } from 'rxjs';
import { TagService } from '../../../../core/services/tag.service';
import { UserService } from '../../../../core/services/user.service';
import { Tag } from '../../../../core/models/tag.model';
import { SettingsSubpageShellComponent } from '../../components/settings-subpage-shell/settings-subpage-shell.component';
import type {
  InferredInterestItem,
  UserInterestItem,
  UserInterestsResponse,
} from '../../../../core/services/tag.service';

@Component({
  selector: 'app-my-interests',
  imports: [CommonModule, SettingsSubpageShellComponent],
  templateUrl: './my-interests.component.html',
  styleUrl: './my-interests.component.css',
})
export class MyInterestsComponent {
  readonly returnTo = signal('/settings');
  readonly isLoading = signal(true);
  readonly isSaving = signal(false);
  readonly loadError = signal('');
  readonly saveError = signal('');
  readonly tags = signal<Tag[]>([]);
  readonly selectedTags = signal<Tag[]>([]);
  readonly inferredTags = signal<InferredInterestItem[]>([]);
  readonly dismissingInferredTagIds = signal<Set<number>>(new Set());
  readonly visibleInferredTags = computed(() => {
    const selectedIds = new Set(this.selectedTags().map(tag => tag.id));
    return this.inferredTags().filter(tag => !selectedIds.has(tag.tagId));
  });
  readonly availableTags = computed(() => {
    const selectedIds = new Set(this.selectedTags().map(tag => tag.id));
    const inferredIds = new Set(this.visibleInferredTags().map(tag => tag.tagId));
    return this.tags().filter(tag => !selectedIds.has(tag.id) && !inferredIds.has(tag.id));
  });
  readonly availableTagGroups = computed(() => {
    const groups = new Map<string, Tag[]>();

    for (const tag of this.availableTags()) {
      const groupName = tag.group?.trim() || 'More interests';
      const groupItems = groups.get(groupName) ?? [];
      groupItems.push(tag);
      groups.set(groupName, groupItems);
    }

    return Array.from(groups.entries()).map(([group, items]) => ({
      group,
      items: [...items].sort((a, b) =>
        (a.sortOrder ?? 0) - (b.sortOrder ?? 0) || a.name.localeCompare(b.name),
      ),
    }));
  });

  constructor(
    private router: Router,
    private route: ActivatedRoute,
    private tagService: TagService,
    private userService: UserService,
  ) {
    this.route.queryParams.subscribe(p => {
      if (p['from']) this.returnTo.set('/' + p['from']);
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

  addInferred(tag: InferredInterestItem): void {
    const fullTag = this.tags().find(t => t.id === tag.tagId);
    if (fullTag) {
      this.addTag(fullTag);
    }
  }

  dismissInferred(tag: InferredInterestItem): void {
    if (this.dismissingInferredTagIds().has(tag.tagId)) return;

    this.dismissingInferredTagIds.update(ids => new Set(ids).add(tag.tagId));
    this.saveError.set('');

    this.tagService.dismissInferredInterest(tag.tagId).subscribe({
      next: () => {
        this.inferredTags.update(tags => tags.filter(t => t.tagId !== tag.tagId));
        this.dismissingInferredTagIds.update(ids => {
          const next = new Set(ids);
          next.delete(tag.tagId);
          return next;
        });
      },
      error: () => {
        this.dismissingInferredTagIds.update(ids => {
          const next = new Set(ids);
          next.delete(tag.tagId);
          return next;
        });
        this.saveError.set('Could not remove that suggested interest. Please try again.');
      },
    });
  }

  isDismissingInferred(tag: InferredInterestItem): boolean {
    return this.dismissingInferredTagIds().has(tag.tagId);
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
            this.router.navigate([this.returnTo()]);
          },
          error: () => {
            this.isSaving.set(false);
            this.router.navigate([this.returnTo()]);
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
    this.router.navigate([this.returnTo()]);
  }

  retryLoad(): void {
    this.loadInterests();
  }

  private loadInterests(): void {
    this.isLoading.set(true);
    this.loadError.set('');
    this.saveError.set('');
    this.selectedTags.set([]);
    this.inferredTags.set([]);
    this.dismissingInferredTagIds.set(new Set());

    forkJoin({
      tags: this.tagService.loadTags(),
      selected: this.tagService.loadUserInterests().pipe(
        catchError(() => of<UserInterestsResponse>({ tags: [], explicitTags: [], inferredTags: [] })),
      ),
    }).subscribe({
      next: ({ tags, selected }) => {
        this.tags.set(tags);
        const explicit: UserInterestItem[] = selected.explicitTags ?? selected.tags;
        const ids = new Set(explicit.map((tag: UserInterestItem) => tag.tagId));
        this.selectedTags.set(tags.filter(tag => ids.has(tag.id)));
        this.inferredTags.set(selected.inferredTags ?? []);
        this.isLoading.set(false);
      },
      error: () => {
        this.tags.set([]);
        this.selectedTags.set([]);
        this.inferredTags.set([]);
        this.isLoading.set(false);
        this.loadError.set('Could not load your interests. Please try again.');
      },
    });
  }
}

