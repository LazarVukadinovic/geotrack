import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { StudentService } from '../../services/student';

@Component({
  selector: 'app-student-list',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './student-list.html',
})
export class StudentListComponent implements OnInit {
  studenti = signal<any[]>([]);

  constructor(private studentService: StudentService, private router: Router) {}

  ngOnInit() {
    this.ucitajPodatke();
  }

  ucitajPodatke() {
    this.studentService.getStudenti().subscribe({
      next: (res: any) => this.studenti.set(Array.isArray(res) ? res : Object.values(res)),
      error: (err) => console.error(err)
    });
  }

  obrisiStudenta(id: number) {
    this.studentService.obrisiStudenta(id).subscribe({
      next: () => this.ucitajPodatke(),
      error: (err) => console.error(err)
    });
  }

  izmeniStudenta(id: number) {
    this.router.navigate(['/studenti/izmeni', id]);
  }

  noviStudent() {
    this.router.navigate(['/studenti/novi']);
  }
}