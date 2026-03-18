import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterOutlet } from '@angular/router';
import { TodoService } from './services/todo.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {

  title = signal('Todo aplikacija');
  todos: any[] = [];
  newTodo: string = '';

  constructor(private todoService: TodoService) {}

  ngOnInit() {
    this.loadTodos();
  }

  loadTodos() {
    this.todoService.getTodos().subscribe(data => {
      this.todos = data.map(t => ({ ...t, editText: t.text, editing: false }));
    });
  }

  addTodo() {
    if (this.newTodo.trim() === '') return;

    const text = this.newTodo;
    const tempId = Date.now();

    this.todos.push({ id: tempId, text, editText: text, editing: false });
    this.newTodo = '';

    this.todoService.addTodo({ text }).subscribe({
      next: (created: any) => {
        const index = this.todos.findIndex(t => t.id === tempId);
        if (index !== -1) this.todos[index] = { ...created, editText: created.text, editing: false };
      },
      error: () => {
        this.todos = this.todos.filter(t => t.id !== tempId);
        this.newTodo = text;
      }
    });
  }

  deleteTodo(id: number) {
    const deleted = this.todos.find(t => t.id === id);
    this.todos = this.todos.filter(t => t.id !== id);

    this.todoService.deleteTodo(id).subscribe({
      error: () => {
        if (deleted) this.todos.push(deleted);
      }
    });
  }

  startEdit(todo: any) {
    todo.editing = true;
    todo.editText = todo.text;
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