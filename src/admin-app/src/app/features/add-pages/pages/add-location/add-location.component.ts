import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component'; 

@Component({
  selector: 'app-add-location',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './add-location.component.html',
  styleUrl: './add-location.component.css'
})
export class NewLocationComponent {
  classItems = [
    { label: 'Nature',   checked: true  },
    { label: 'History',  checked: true  },
    { label: 'Beaches',  checked: false },
    { label: 'Hiking',   checked: false },
    { label: 'Culture',  checked: false },
  ];
}