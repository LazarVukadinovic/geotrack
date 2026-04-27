import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component'; 
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component'; 

@Component({
  selector: 'app-new-event',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './add-event.component.html',
  styleUrl: './add-event.component.css'
})
export class NewEventComponent {
  eventTitle = 'Summer Festival';
  venueType = 'location';
  allDay = false;
  freeEvent = false;
  recurring = false;

  classItems = [
    { label: 'Music',           checked: true  },
    { label: 'Outdoor',         checked: true  },
    { label: 'Culture',         checked: false },
    { label: 'Family Friendly', checked: false },
  ];
}