import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-about',
  standalone: true,
  imports: [RouterLink],
  template: `
    <div style="text-align:center; margin-top: 50px;">
      <h1>O aplikaciji</h1>
      <p>Ovo je jednostavna Todo aplikacija napravljena uz Angular i .NET.</p>
      <a routerLink="/">← Nazad na Todo listu</a>
    </div>
  `
})
export class About {}