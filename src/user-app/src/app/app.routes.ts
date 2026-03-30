import { Routes } from '@angular/router';
import { Login } from './features/auth/pages/login/login';
// import { Register } from './features/auth/register/register';
// import { Interests } from './features/auth/interests/interests';

export const routes: Routes = [
    { path: '', redirectTo: 'login', pathMatch: 'full' },
    { path: 'login', component: Login }
//   { path: 'register', component: Register },
//   { path: 'interests', component: Interests },
];
