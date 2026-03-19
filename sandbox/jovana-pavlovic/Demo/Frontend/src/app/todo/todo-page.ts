import { Component, signal, ChangeDetectorRef } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TodoService } from '../services/todo.service';


@Component({
  selector: 'app-todo-page',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './todo-page.html',
  styleUrl: './todo-page.css'
})
export class TodoPage {

  title = signal('Todo aplikacija');
  
  newTodo: string = '';

  constructor(public todoService: TodoService, private cdr: ChangeDetectorRef) {}

  ngOnInit() {
    if (this.todoService.todos.length === 0) {
      this.loadTodos();
    }
  }

  loadTodos() {
  this.todoService.getTodos().subscribe(data => {
    this.todoService.todos = data.map(t => ({
      ...t,
      editText: t.text,
      editing: false
    }));
  });
}

  addTodo() {
    if (this.newTodo.trim() === '') return;

    const text = this.newTodo;
    const tempId = Date.now();

    this.newTodo = '';

    this.todoService.todos.push({ id: tempId, text, editText: text, editing: false });
  

    this.todoService.addTodo({ text }).subscribe({
      next: (created: any) => {
        const index = this.todoService.todos.findIndex(t => t.id === tempId);
        if (index !== -1) this.todoService.todos[index] = { ...created, editText: created.text, editing: false };
      },
      error: () => {
        this.todoService.todos = this.todoService.todos.filter(t => t.id !== tempId);
        this.newTodo = text;
      }
    });
  }

  deleteTodo(id: number) {
    // const deleted = this.todoService.todos.find(t => t.id === id);
    // this.todoService.todos = this.todoService.todos.filter(t => t.id !== id);

    // this.todoService.deleteTodo(id).subscribe({
    //   error: () => {
    //     if (deleted) this.todoService.todos.push(deleted);
    //   }
    // });
    const index = this.todoService.todos.findIndex(t => t.id === id);
    if (index === -1) return;
  
    const deleted = this.todoService.todos[index];
    this.todoService.todos.splice(index, 1);

    this.todoService.deleteTodo(id).subscribe({
      error: () => {
        if (deleted) this.todoService.todos.push(deleted);
      }
    });
  }

  startEdit(todo: any) {
    todo.editing = true;
    todo.editText = todo.text;
    this.cdr.detectChanges();
  }

  updateTodo(todo: any) {
    const oldText = todo.text;
    todo.text = todo.editText;
    todo.editing = false;

    this.todoService.updateTodo(todo.id, { ...todo, text: todo.editText }).subscribe({
      error: () => {
        todo.text = oldText;
        todo.editText = oldText;
      }
    });
  }

  cancelEdit(todo: any) {
    todo.editText = todo.text;
    todo.editing = false;
  }
}