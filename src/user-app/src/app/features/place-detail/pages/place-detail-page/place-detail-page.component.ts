import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { map } from 'rxjs/operators';
import { PlaceholderViewComponent } from '../../../../shared/components/placeholder-view/placeholder-view.component';

@Component({
  selector: 'app-place-detail-page',
  standalone: true,
  imports: [PlaceholderViewComponent],
  template: `<app-placeholder-view [title]="'Place · ' + (placeId() || '…')" />`,
})
export class PlaceDetailPageComponent {
  private readonly route = inject(ActivatedRoute);
  protected readonly placeId = toSignal(
    this.route.paramMap.pipe(map((p) => p.get('id') ?? '')),
    { initialValue: '' },
  );
}
