import { Component } from '@angular/core';
import { PlaceholderViewComponent } from '../../../../shared/components/placeholder-view/placeholder-view.component';

@Component({
  selector: 'app-map-page',
  standalone: true,
  imports: [PlaceholderViewComponent],
  template: `<app-placeholder-view title="Map" />`,
})
export class MapPageComponent {}
