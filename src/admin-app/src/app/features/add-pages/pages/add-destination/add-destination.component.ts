import { Component, ElementRef, OnDestroy, OnInit, ViewChild, AfterViewInit, NgZone } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';

declare const L: any;

interface ClassItem {
  label: string;
  checked: boolean;
}

@Component({
  selector: 'app-add-destination',
  standalone: true,
  imports: [CommonModule, FormsModule, DecimalPipe, SidebarComponent, TopbarComponent],
  templateUrl: './add-destination.component.html',
  styles: [`
    .rte-field { position: relative; }
    .rte-ph {
      position: absolute;
      top: 0; left: 0; right: 0;
      padding: 0.5rem 0.75rem;
      color: #9ca3af;
      font-size: 0.875rem;
      pointer-events: none;
      user-select: none;
      line-height: 1.5rem;
    }
  `]
})
export class AddDestinationComponent implements OnInit, AfterViewInit, OnDestroy {

  constructor(private zone: NgZone) {}

  // ── ViewChild refs ──────────────────────────────────────────────────────────
  @ViewChild('editorRef')         editorRef!:            ElementRef<HTMLDivElement>;
  

  // ── Basic fields ────────────────────────────────────────────────────────────
  destinationName = '';

  // ── Rich text editor ────────────────────────────────────────────────────────
  isBold        = false;
  isItalic      = false;
  isUnderline   = false;
  editorIsEmpty = true;

  execFormat(command: 'bold' | 'italic' | 'underline'): void {
    document.execCommand(command, false, undefined);
    this.editorRef.nativeElement.focus();
    this.syncToolbarState();
  }

  onEditorChange(): void { this.syncToolbarState(); this.syncEmptyState(); }
  onEditorFocus():  void { this.syncToolbarState(); this.syncEmptyState(); }
  onEditorBlur():   void {
    this.syncToolbarState();
    const el = this.editorRef?.nativeElement;
    if (el && (el.innerHTML === '<br>' || el.innerHTML === '&nbsp;' || el.innerHTML.trim() === '')) {
      el.innerHTML = '';
    }
    this.syncEmptyState();
  }

  private syncEmptyState(): void {
    const el = this.editorRef?.nativeElement;
    if (!el) return;
    const text = el.innerText ?? el.textContent ?? '';
    this.editorIsEmpty = text.trim() === '' && !el.querySelector('img');
  }

  private syncToolbarState(): void {
    this.isBold      = document.queryCommandState('bold');
    this.isItalic    = document.queryCommandState('italic');
    this.isUnderline = document.queryCommandState('underline');
  }

  

  // ── Lifecycle ────────────────────────────────────────────────────────────────
  ngOnInit(): void {}

  ngAfterViewInit(): void {
    
  }

  ngOnDestroy(): void {
   
  }
}