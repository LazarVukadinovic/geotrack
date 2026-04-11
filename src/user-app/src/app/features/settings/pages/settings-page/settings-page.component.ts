import { Component } from '@angular/core';
import { PlaceholderViewComponent } from '../../../../shared/components/placeholder-view/placeholder-view.component';

@Component({
  selector: 'app-settings-page',
  standalone: true,
  imports: [PlaceholderViewComponent],
  template: `<app-placeholder-view title="Settings" />`,
})
export class SettingsPageComponent {}
