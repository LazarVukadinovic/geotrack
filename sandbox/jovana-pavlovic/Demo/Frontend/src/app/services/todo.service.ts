import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';

@Injectable({
  providedIn: 'root'
})
export class TodoService {

  api = "http://localhost:5151/api/todo";

  constructor(private http: HttpClient) {}

  getTodos(){
    return this.http.get<any[]>(this.api);
  }

  addTodo(todo:any){
    return this.http.post(this.api, todo);
  }
  updateTodo(id:number,todo:any){
  return this.http.put(`${this.api}/${id}`,todo);
  }

  deleteTodo(id:number){
    return this.http.delete(`${this.api}/${id}`);
  }

}