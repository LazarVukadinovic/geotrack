import { Component } from '@angular/core';
import { RouterOutlet, RouterLink } from '@angular/router';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink],
  template: `
    <nav style="text-align:center; padding: 10px; background:#eee;">
      <a routerLink="/" style="margin-right:20px;">Todo lista</a>
      <a routerLink="/about">O aplikaciji</a>
    </nav>
    <router-outlet />
  `
})
export class Shell {}