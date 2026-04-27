import { Component, computed, inject, signal, NgZone, OnDestroy } from '@angular/core';
import { Location } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { CollectionsService } from '../../services/collections';

const DISMISS_THRESHOLD = 120;

@Component({
  selector: 'app-collection-detail',
  standalone: true,
  imports: [],
  templateUrl: './collection-detail.component.html',
})
export class CollectionDetailComponent {
  private readonly location    = inject(Location);
  private readonly route       = inject(ActivatedRoute);
  private readonly collService = inject(CollectionsService);
  private readonly ngZone      = inject(NgZone);

  private readonly id = Number(this.route.snapshot.paramMap.get('id') ?? 0);

  protected readonly collection = computed(() =>
    this.collService.collections().find(c => c.id === this.id)
  );
  protected readonly loadState = computed(() =>
    this.collService.getDetailLoadState(this.id)
  );

  constructor() {
    if (this.id > 0) this.collService.loadDetails(this.id);
  }

  protected readonly menuOpen   = signal(false);
  protected readonly renameOpen = signal(false);
  protected readonly renameValue = signal('');
  
  protected readonly menuDragOffset  = signal(0);
  protected readonly menuIsDragging  = signal(false);
  protected readonly renameDragOffset  = signal(0);
  protected readonly renameIsDragging  = signal(false);

  private touchStartY = 0;
  private activeDrag: 'menu' | 'rename' | null = null;
    private readonly boundMove = this.onDocTouchMove.bind(this);
  private readonly boundEnd  = this.onDocTouchEnd.bind(this);
    private attachDocListeners(): void {
    this.ngZone.runOutsideAngular(() => {
      document.addEventListener('touchmove', this.boundMove, { passive: false });
      document.addEventListener('touchend',  this.boundEnd,  { passive: false });
    });
  }
    private detachDocListeners(): void {
    document.removeEventListener('touchmove', this.boundMove);
    document.removeEventListener('touchend',  this.boundEnd);
  }

  ngOnDestroy(): void {
    this.detachDocListeners();
  }
  protected goBack(): void { this.location.back(); }

  protected onRemovePlace(itemId: number): void {
    this.collService.removePlace(this.id, itemId);
  }

  protected onMenuOpen(): void {
    this.menuOpen.set(true);
  }
  protected closeMenu(): void {
    this.menuDragOffset.set(0);
    this.menuOpen.set(false);
  }
  protected openRename(): void {
    this.renameValue.set(this.collection()?.title ?? '');
    this.menuDragOffset.set(0);
    this.menuOpen.set(false);
    this.renameOpen.set(true);
  }
  protected closeRename(): void {
    this.renameDragOffset.set(0);
    this.renameOpen.set(false);
  }
  protected onRenameSave(): void {
    const trimmed = this.renameValue().trim();
    if (!trimmed) return;
    this.collService.renameCollection(this.id, trimmed);
    this.closeRename();
  }
  protected onDelete(): void {
    this.collService.deleteCollection(this.id);
    this.menuOpen.set(false);
    this.location.back();
  }

  protected reloadCollection(): void {
    if (this.id > 0) {
      this.collService.loadDetails(this.id);
    }
  }
  protected onMenuTouchStart(e: TouchEvent): void {
    this.activeDrag = 'menu';
    this.touchStartY = e.touches[0].clientY;
    this.menuIsDragging.set(true);
    this.attachDocListeners();
  }

  protected onRenameTouchStart(e: TouchEvent): void {
    this.activeDrag = 'rename';
    this.touchStartY = e.touches[0].clientY;
    this.renameIsDragging.set(true);
    this.attachDocListeners();
  }

  protected onDocTouchMove(e: TouchEvent): void {
    const delta = e.touches[0].clientY - this.touchStartY;
    if (delta <= 0) return;
    e.preventDefault();
    this.ngZone.run(() => {
      if (this.activeDrag === 'menu')   this.menuDragOffset.set(delta);
      if (this.activeDrag === 'rename') this.renameDragOffset.set(delta);
    });
  }

  private onDocTouchEnd(_e: TouchEvent): void {
    this.detachDocListeners();

    this.ngZone.run(() => {
      if (this.activeDrag === 'menu') {
        this.menuIsDragging.set(false);
        if (this.menuDragOffset() >= DISMISS_THRESHOLD) {
          this.closeMenu();
        } else {
          this.menuDragOffset.set(0);
        }
      } else if (this.activeDrag === 'rename') {
        this.renameIsDragging.set(false);
        if (this.renameDragOffset() >= DISMISS_THRESHOLD) {
          this.closeRename();
        } else {
          this.renameDragOffset.set(0);
        }
      }
      this.activeDrag = null;
    });
  }
}
