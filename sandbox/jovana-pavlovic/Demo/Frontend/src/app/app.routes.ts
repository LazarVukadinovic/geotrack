import { Routes } from '@angular/router';
import { TodoPage } from './todo/todo-page';
import { About } from './about/about';

export const routes: Routes = [
  { path: '', component: TodoPage },
  { path: 'about', component: About }
];