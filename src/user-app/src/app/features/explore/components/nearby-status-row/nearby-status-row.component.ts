import { Component, input } from '@angular/core';
import type { NearbyStatus } from '../../models/explore-extras.model';

@Component({
  selector: 'app-nearby-status-row',
  standalone: true,
  templateUrl: './nearby-status-row.component.html',
  styleUrl: './nearby-status-row.component.scss',
})
export class NearbyStatusRowComponent {
  readonly status = input.required<NearbyStatus>();
}

