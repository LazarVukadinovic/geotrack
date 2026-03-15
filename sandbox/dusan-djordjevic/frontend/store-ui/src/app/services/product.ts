import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Product } from '../models/product';

@Injectable({
  providedIn: 'root'
})
export class ProductService {

  api = "http://localhost:5192/api/products";

  constructor(private http: HttpClient) {}

  getProducts(){
    return this.http.get<Product[]>(this.api);
  }

  addProduct(product:Product){
    return this.http.post(this.api, product);
  }

  updateProduct(id:number, product:Product){
    return this.http.put(`${this.api}/${id}`, product);
  }

  deleteProduct(id:number){
    return this.http.delete(`${this.api}/${id}`);
  }
}