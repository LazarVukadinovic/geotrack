import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { StudentService } from '../../services/student';
import { ActivatedRoute, Router } from '@angular/router';

@Component({
  selector: 'app-student-form',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './student-form.html',
})
export class StudentFormComponent implements OnInit {
  editMode = signal(false);
  studentId = signal<number | null>(null);
  forma = signal<any>({ ime: '', prezime: '', indeks: null, upisan: null });

  constructor(
    private studentService: StudentService,
    private router: Router,
    private route: ActivatedRoute
  ) {}

  ngOnInit() {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.editMode.set(true);
      this.studentId.set(+id);
      this.studentService.getStudent(+id).subscribe({
        next: (res: any) => this.forma.set({ ...res }),
        error: (err) => console.error(err)
      });
    }
  }

  sacuvaj() {
    if (this.editMode()) {
      this.studentService.izmeniStudenta(this.studentId()!, this.forma()).subscribe({
        next: () => this.router.navigate(['/studenti']),
        error: (err) => console.error(err)
      });
    } else {
      this.studentService.dodajStudenta(this.forma()).subscribe({
        next: () => this.router.navigate(['/studenti']),
        error: (err) => console.error(err)
      });
    }
  }

  updateForma(field: string, value: any) {
    this.forma.update(f => ({ ...f, [field]: value }));
  }

  otkazi() {
    this.router.navigate(['/studenti']);
  }
}