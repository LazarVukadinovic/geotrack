import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { TagService } from '../../../../core/services/tag.service';
import { Tag } from '../../../../core/models/tag.model';

@Component({
  selector: 'app-interests-page',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './interests-page.component.html',
  styleUrl: './interests-page.component.css',
})
export class InterestsPageComponent implements OnInit {
  tags: Tag[] = [];
  selectedTagIds: Set<number> = new Set();
  isLoading = false;

  constructor(
    private router: Router,
    private tagService: TagService,
  ) {}

  ngOnInit(): void {
    this.tagService.loadTags().subscribe(tags => {
      this.tags = tags;
    });
  }

  toggleTag(tag: Tag): void {
    if (this.selectedTagIds.has(tag.id)) {
      this.selectedTagIds.delete(tag.id);
    } else {
      this.selectedTagIds.add(tag.id);
    }
  }

  isSelected(tag: Tag): boolean {
    return this.selectedTagIds.has(tag.id);
  }

  get selectedCount(): number {
    return this.selectedTagIds.size;
  }

  onFinish(): void {
    if (this.selectedCount === 0) return;

    this.isLoading = true;

    const tagIds = [...this.selectedTagIds];

    this.tagService.saveUserTags(tagIds).subscribe({
      next: () => {
        localStorage.setItem('userInterests', JSON.stringify(tagIds));
        this.isLoading = false;
        this.router.navigate(['/']);
      },
      error: () => {
        localStorage.setItem('userInterests', JSON.stringify(tagIds));
        this.isLoading = false;
        this.router.navigate(['/']);
      },
    });
  }

  onSkip(): void {
    this.router.navigate(['/']);
  }

  goBack(): void {
    this.router.navigate(['/register']);
  }
}
