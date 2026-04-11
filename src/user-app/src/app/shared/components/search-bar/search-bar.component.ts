import {
  Component,
  effect,
  ElementRef,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';

@Component({
  selector: 'app-search-bar',
  standalone: true,
  templateUrl: './search-bar.component.html',
  styleUrl: './search-bar.component.scss',
})
export class SearchBarComponent {
  readonly value = input<string>('');
  readonly placeholder = input('Search destinations or activities...');

  readonly valueChange = output<string>();

  private readonly field = viewChild<ElementRef<HTMLInputElement>>('searchField');

  /** Internal buffer for smooth typing; synced from parent `value` input. */
  protected readonly local = signal('');

  constructor() {
    effect(() => {
      this.local.set(this.value());
    });
  }

  /** Focus the input (e.g. when the header search icon is pressed). */
  focusInput(): void {
    const el = this.field()?.nativeElement;
    if (!el) {
      return;
    }
    el.focus();
    el.scrollIntoView({ block: 'center', behavior: 'smooth' });
  }

  protected onInput(event: Event): void {
    const v = (event.target as HTMLInputElement).value;
    this.local.set(v);
    this.valueChange.emit(v);
  }
}
