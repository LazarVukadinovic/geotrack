import { Routes } from '@angular/router';
import { StudentListComponent } from './components/student-list/student-list';
import { StudentFormComponent } from './components/student-form/student-form';

export const routes: Routes = [
  { path: '', redirectTo: '/studenti', pathMatch: 'full' },
  { path: 'studenti', component: StudentListComponent },
  { path: 'studenti/novi', component: StudentFormComponent },
  { path: 'studenti/izmeni/:id', component: StudentFormComponent },
];

