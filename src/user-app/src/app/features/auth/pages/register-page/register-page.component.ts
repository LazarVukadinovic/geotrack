import { Component } from '@angular/core';
import { PlaceholderViewComponent } from '../../../../shared/components/placeholder-view/placeholder-view.component';

@Component({
  selector: 'app-register-page',
  standalone: true,
  imports: [PlaceholderViewComponent],
  template: `<app-placeholder-view title="Register" />`,
})
export class RegisterPageComponent {}
