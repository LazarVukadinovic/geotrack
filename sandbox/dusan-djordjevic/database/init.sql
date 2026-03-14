CREATE TABLE products(
	id SERIAL PRIMARY KEY,
	name TEXT,
	price NUMERIC,
	category TEXT
);

INSERT INTO products(name, price, category)
VALUES
('Lenovo Laptop', 1200, 'Laptop'),
('MacBook Pro', 2500, 'Laptop'),
('Iphone 17', 1100, 'Phone'),
('Samsung Galaxy S26 Ultra', 1300, 'Phone'),
('Logitech Mouse', 40, 'Accessory');