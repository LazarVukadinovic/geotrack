import { HttpClient } from '@angular/common/http';
import { Injectable,inject } from '@angular/core';
import { Observable } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class StudentService {
  // Port 7015 
  private http = inject(HttpClient);
  private apiUrl = 'https://localhost:7015/api/Student'; 

  

  getStudenti(): Observable<any> { 
  return this.http.get<any>(this.apiUrl);
}

getStudent(id: number): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/${id}`);
  }

  
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