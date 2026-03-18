import { Component, OnInit,NgZone,ChangeDetectionStrategy,ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { StudentService } from './services/student';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule,FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.css',
  changeDetection: ChangeDetectionStrategy.Default
})
export class AppComponent implements OnInit {
  studenti: any[] = []; 
  editMode = false;
  forma: any = { ime: '', prezime: '', indeks: null, upisan: null };
  constructor(private studentService: StudentService,private zone: NgZone,private cdr: ChangeDetectorRef) {}

  ngOnInit() {
    this.ucitajPodatke();
  }

 ucitajPodatke() {
  this.studentService.getStudenti().subscribe({
    next: (res: any) => {
      this.studenti = [...res];
      this.cdr.detectChanges();
      console.log('Podaci stigli:', this.studenti);
    },
    error: (err) => console.error('Greška:', err)
  });
}
  /*obrisiStudenta(id: number) {
  if (confirm('Da li želite da obrišete studenta?')) {
    this.studentService.obrisiStudenta(id).subscribe({
      next: () => {
        this.ucitajPodatke();
      },
      error: (err) => console.error('Greška brisanje:', err)
    });
  }
}*/

obrisiStudenta(id: number) {
  console.log('Brišem ID:', id);
  this.studentService.obrisiStudenta(id).subscribe({
    next: () => {
      console.log('Obrisano!');
      this.ucitajPodatke();
    },
    error: (err) => console.error('Greška brisanje:', err)
  });
}

  izmeniStudenta(s: any) {
  this.editMode = true;
  this.forma = { ...s };
}

otkazi() {
  this.editMode = false;
  this.forma = { ime: '', prezime: '', indeks: null, upisan: null };
}

sacuvaj() {
  if (this.editMode) {
    this.studentService.izmeniStudenta(this.forma.id, this.forma).subscribe({
      next: () => {
        this.ucitajPodatke();
        this.otkazi();
      },
      error: (err) => console.error('Greška izmena:', err)
    });
  } else {
    this.studentService.dodajStudenta(this.forma).subscribe({
      next: () => {
        this.ucitajPodatke();
        this.otkazi();
      },
      error: (err) => console.error('Greška dodavanje:', err)
    });
  }
}
}