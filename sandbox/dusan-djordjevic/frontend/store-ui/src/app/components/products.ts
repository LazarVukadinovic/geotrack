import { Component, OnInit } from '@angular/core';
import { ProductService } from '../services/product';
import { Product } from '../models/product';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ChangeDetectorRef } from '@angular/core';

@Component({
  selector: 'app-products',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './products.html'
})
export class ProductsComponent implements OnInit {

  products: Product[] = [];

  newProduct: Product = {
    name: '',
    price: 0,
    category: ''
  };

  editingProduct: Product | null = null;

  constructor(
    private productService: ProductService,
    private cdr: ChangeDetectorRef
  ){}

  ngOnInit(): void {
    this.loadProducts();
  }

  loadProducts(){
    this.productService.getProducts()
      .subscribe(data => {
        this.products = data;
        this.cdr.markForCheck();
      });
  }

  addProduct(){

    // VALIDACIJA
    if(!this.newProduct.name || !this.newProduct.category || this.newProduct.price <= 0){
      alert("Please enter all product data.");
      return;
    }

    this.productService.addProduct(this.newProduct)
      .subscribe(() => {
        this.loadProducts();

        this.newProduct = {
          name: '',
          price: 0,
          category: ''
        };
      });
  }

  deleteProduct(id:number){
    this.productService.deleteProduct(id)
      .subscribe(() => this.loadProducts());
  }

  editProduct(product: Product){
    this.editingProduct = { ...product };
  }

  updateProduct(){

    if(!this.editingProduct) return;

    if(!this.editingProduct.name || !this.editingProduct.category || this.editingProduct.price <= 0){
      alert("All fields must be filled.");
      return;
    }

    if(this.editingProduct.id){
      this.productService.updateProduct(this.editingProduct.id, this.editingProduct)
        .subscribe(() => {
          this.editingProduct = null;
          this.loadProducts();
        });
    }
  }

}