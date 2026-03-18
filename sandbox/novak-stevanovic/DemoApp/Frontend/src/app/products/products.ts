import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Product } from '../models/product';
import { ProductService } from '../services/product';

@Component({
  selector: 'app-products',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './products.html',
  styleUrl: './products.css'
})
export class Products implements OnInit {
  products: Product[] = [];

  form: Product = {
    id: 0,
    name: '',
    price: 0,
    description: ''
  };

  editMode = false;

  constructor(private productService: ProductService) {}

  ngOnInit(): void {
    this.loadProducts();
  }

  loadProducts(): void {
    this.productService.getAll().subscribe({
      next: (data: Product[]) => this.products = data,
      error: (err: unknown) => console.error(err)
    });
  }

  save(): void {
    if (this.editMode) {
      this.productService.update(this.form).subscribe({
        next: () => {
          this.resetForm();
          this.loadProducts();
        },
        error: (err: unknown) => console.error(err)
      });
    } else {
      this.productService.create(this.form).subscribe({
        next: () => {
          this.resetForm();
          this.loadProducts();
        },
        error: (err: unknown) => console.error(err)
      });
    }
  }

  edit(product: Product): void {
    this.form = { ...product };
    this.editMode = true;
  }

  remove(id: number): void {
    this.productService.delete(id).subscribe({
      next: () => this.loadProducts(),
      error: (err: unknown) => console.error(err)
    });
  }

  resetForm(): void {
    this.form = {
      id: 0,
      name: '',
      price: 0,
      description: ''
    };
    this.editMode = false;
  }
}