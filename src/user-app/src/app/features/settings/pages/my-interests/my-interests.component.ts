import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, ActivatedRoute } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { TagService } from '../../../../core/services/tag.service';
import { Tag } from '../../../../core/models/tag.model';

interface InterestItem {
  tagId: number;
  name: string;
  slug: string;
}

interface UserInterestsResponse {
  tags: InterestItem[];
}

@Component({
  selector: 'app-my-interests',
  imports: [CommonModule],
  templateUrl: './my-interests.component.html',
  styleUrl: './my-interests.component.css',
})
export class MyInterestsComponent {
  isSaving = false;
  tags: Tag[] = [];
  selectedTags: Tag[] = [];
  private returnTo = '/settings';

  constructor(
    private router: Router,
    private route: ActivatedRoute,
    private tagService: TagService,
    private http: HttpClient,
  ) {
    this.route.queryParams.subscribe(p => {
      if (p['from']) this.returnTo = '/' + p['from'];
    });

    this.tagService.loadTags().subscribe(tags => {
      this.tags = tags;
      this.loadSavedInterests();
    });
  }

  private loadSavedInterests(): void {
    // Load from backend first, fallback to localStorage
    this.http.get<UserInterestsResponse>('/api/user/me/interests').subscribe({
      next: (res) => {
        const ids = res.tags.map(t => t.tagId);
        this.selectedTags = this.tags.filter(t => ids.includes(t.id));
        // Sync localStorage
        localStorage.setItem('userInterests', JSON.stringify(ids));
      },
      error: () => {
        const saved = localStorage.getItem('userInterests');
        if (saved) {
          const ids: number[] = JSON.parse(saved);
          this.selectedTags = this.tags.filter(t => ids.includes(t.id));
        }
      },
    });
  }

  get availableTags(): Tag[] {
    const selectedIds = this.selectedTags.map(t => t.id);
    return this.tags.filter(t => !selectedIds.includes(t.id));
  }

  addTag(tag: Tag): void {
    if (!this.isSelected(tag)) {
      this.selectedTags = [...this.selectedTags, tag];
    }
  }

  removeTag(tag: Tag): void {
    this.selectedTags = this.selectedTags.filter(t => t.id !== tag.id);
  }

  isSelected(tag: Tag): boolean {
    return this.selectedTags.some(t => t.id === tag.id);
  }

  saveChanges(): void {
    if (this.isSaving) return;
    this.isSaving = true;

    const tagIds = this.selectedTags.map(t => t.id);

    this.tagService.saveUserTags(tagIds).subscribe({
      next: () => {
        localStorage.setItem('userInterests', JSON.stringify(tagIds));
        this.isSaving = false;
        this.router.navigate([this.returnTo]);
      },
      error: () => {
        localStorage.setItem('userInterests', JSON.stringify(tagIds));
        this.isSaving = false;
        this.router.navigate([this.returnTo]);
      },
    });
  }

  goBack(): void {
    this.router.navigate([this.returnTo]);
  }
}
