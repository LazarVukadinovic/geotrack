import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class StudentService {
  // Port mora biti 7015 kao što se vidi na tvojoj Swagger slici
  private apiUrl = 'https://localhost:7015/api/Student'; 

  constructor(private http: HttpClient) { }

  // Čitanje svih studenata
 /* getStudenti(): Observable<any[]> {
    return this.http.get<any[]>(this.apiUrl);
  }*/
  getStudenti(): Observable<any> {  // promeni any[] u any
  return this.http.get<any>(this.apiUrl);
}

  // Brisanje studenta po ID-u
  obrisiStudenta(id: number): Observable<any> {
    return this.http.delete(`${this.apiUrl}/${id}`, { responseType: 'text' });
  }

  dodajStudenta(student: any): Observable<any> {
  return this.http.post(this.apiUrl, student, { responseType: 'text' });
}

  izmeniStudenta(id: number, student: any): Observable<any> {
  return this.http.put(`${this.apiUrl}/${id}`, student, { responseType: 'text' });
}
}