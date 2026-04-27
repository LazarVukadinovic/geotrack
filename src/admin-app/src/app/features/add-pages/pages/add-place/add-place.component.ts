import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component'; 
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component'; 

interface DayHours {
  name: string;
  open: boolean;
  openTime: string;
  closeTime: string;
}

@Component({
  selector: 'app-new-place',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './add-place.component.html',
  styleUrl: './add-place.component.css'
})
export class NewPlaceComponent {
  placeType = 'Museum';
  opsEnabled = false;

  openTimes = ['08:00 AM', '09:00 AM', '10:00 AM', '11:00 AM', '12:00 PM'];
  closeTimes = ['04:00 PM', '05:00 PM', '06:00 PM', '07:00 PM', '08:00 PM', '09:00 PM'];

  days: DayHours[] = [
    { name: 'Monday',    open: true,  openTime: '09:00 AM', closeTime: '05:00 PM' },
    { name: 'Tuesday',   open: true,  openTime: '09:00 AM', closeTime: '05:00 PM' },
    { name: 'Wednesday', open: true,  openTime: '09:00 AM', closeTime: '05:00 PM' },
    { name: 'Thursday',  open: true,  openTime: '09:00 AM', closeTime: '05:00 PM' },
    { name: 'Friday',    open: true,  openTime: '09:00 AM', closeTime: '05:00 PM' },
    { name: 'Saturday',  open: true,  openTime: '10:00 AM', closeTime: '08:00 PM' },
    { name: 'Sunday',    open: false, openTime: '09:00 AM', closeTime: '05:00 PM' },
  ];

  classItems = [
    { label: 'Coastal & Marine',    checked: true  },
    { label: 'Mountain & Forest',   checked: true  },
    { label: 'Urban Center',        checked: false },
    { label: 'Historical Site',     checked: false },
    { label: 'National Park Access',checked: false },
  ];
}