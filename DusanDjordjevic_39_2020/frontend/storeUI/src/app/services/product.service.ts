import { Injectable } from "@angular/core";
import { HttpClient } from "@angular/common/http";
import { Product } from "../models/product";

@Injectable({providedIn: "root"})
export class ProductService {
  apiUrl = "http://localhost:5087/products";

  constructor(private http: HttpClient) {}

    getProducts() {
        return this.http.get<Product[]>(this.apiUrl);
    }

    addProduct(product: Product) {
        return this.http.post<Product>(this.apiUrl, product);
    }

    updateProduct(product: Product) {
        return this.http.put<Product>(`${this.apiUrl}/${product.id}`, product);
    }

    deleteProduct(id: number) {
        return this.http.delete(`${this.apiUrl}/${id}`);
    }
}
