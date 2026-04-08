import { Routes } from '@angular/router';
import { Login } from './features/auth/pages/login/login';
import { Register } from './features/auth/pages/register/register';
import { Interests } from './features/auth/pages/interests/interests';
import { EditProfile } from './features/profile/pages/edit-profile/edit-profile';

export const routes: Routes = [
    { path: '', redirectTo: 'edit_profile', pathMatch: 'full' },
    { path: 'login', component: Login },
    { path: 'register', component: Register },
    { path: 'interests', component: Interests },
    { path: 'edit_profile', component: EditProfile }
];
