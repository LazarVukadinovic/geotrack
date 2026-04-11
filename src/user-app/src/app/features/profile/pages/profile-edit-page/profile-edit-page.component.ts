import { Component } from '@angular/core';
import { PlaceholderViewComponent } from '../../../../shared/components/placeholder-view/placeholder-view.component';

@Component({
  selector: 'app-profile-edit-page',
  standalone: true,
  imports: [PlaceholderViewComponent],
  template: `<app-placeholder-view title="Edit profile" />`,
})
export class ProfileEditPageComponent {}
