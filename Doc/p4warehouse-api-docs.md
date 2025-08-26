# P4 Warehouse API - Products Endpoint
## Official API Documentation

---

## CONNECTION SETUP

### Base URL
```
https://api.p4warehouse.com
```

### Authentication
- **Header Name:** `ApiKey`
- **Header Value:** Your API key from Gateway API key setting

### Required Headers
```
ApiKey: YOUR_API_KEY_HERE
Content-Type: application/json
Accept: application/json
```

---

## PRODUCTS ENDPOINT (`/products`)

---

## GET ALL PRODUCTS

### Endpoint
```http
GET /products
```

### Query Parameters
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| Sku | string | No | Filter by SKU |
| ClientId | uuid | No | Filter by client ID |
| Page | integer | No | Page number for pagination |
| PageSize | integer | No | Number of items per page |

### Example Request
```http
GET https://api.p4warehouse.com/products?Page=1&PageSize=50
Headers:
  ApiKey: YOUR_API_KEY_HERE
  Accept: application/json
```

### Example Request with Filters
```http
GET https://api.p4warehouse.com/products?Sku=SKU123&ClientId=f47ac10b-58cc-4372-a567-0e02b2c3d479&Page=1&PageSize=20
Headers:
  ApiKey: YOUR_API_KEY_HERE
  Accept: application/json
```

### Response (200 OK)
```json
[
  {
    "description": "Blue Widget - High Quality",
    "upc": "123456789012",
    "barcodeType": "EAN13",
    "barcodeValue": "1234567890123",
    "referenceNumber": "REF-001",
    "id": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
    "client": {
      "description": "Main Warehouse Client",
      "ssccCompanyId": "1234567",
      "id": "6ba7b810-9dad-11d1-80b4-00c04fd430c8",
      "name": "Acme Corp"
    },
    "sku": "SKU123"
  },
  {
    "description": "Red Widget - Premium",
    "upc": "987654321098",
    "barcodeType": "UPCA",
    "barcodeValue": "987654321098",
    "referenceNumber": "REF-002",
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "client": {
      "description": "Main Warehouse Client",
      "ssccCompanyId": "1234567",
      "id": "6ba7b810-9dad-11d1-80b4-00c04fd430c8",
      "name": "Acme Corp"
    },
    "sku": "SKU456"
  }
]
```

---

## GET PRODUCT BY ID

### Endpoint
```http
GET /products/{id}
```

### Path Parameters
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| id | uuid | Yes | Product ID (GUID format) |

### Example Request
```http
GET https://api.p4warehouse.com/products/f47ac10b-58cc-4372-a567-0e02b2c3d479
Headers:
  ApiKey: YOUR_API_KEY_HERE
  Accept: application/json
```

### Response (200 OK)
```json
{
  "description": "Blue Widget - High Quality",
  "upc": "123456789012",
  "barcodeType": "EAN13",
  "barcodeValue": "1234567890123",
  "referenceNumber": "REF-001",
  "id": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "client": {
    "description": "Main Warehouse Client",
    "ssccCompanyId": "1234567",
    "id": "6ba7b810-9dad-11d1-80b4-00c04fd430c8",
    "name": "Acme Corp"
  },
  "sku": "SKU123",
  "isLotControlled": false,
  "lotPattern": null,
  "isSerialControlled": false,
  "isBillOfMaterial": false,
  "serialPattern": null,
  "isExpiryControlled": false,
  "isDecimalControlled": false,
  "isPacksizeControlled": true,
  "palletTie": 10,
  "palletHeight": 5,
  "height": 10.5,
  "length": 15.0,
  "width": 8.0,
  "weight": 2.5,
  "dimsLengthUnitOfMeasure": "IN",
  "dimsWeightUnitOfMeasure": "LB",
  "unitOfMeasure": "EA",
  "category": "Widgets",
  "freightClass": "Class50",
  "image": "https://example.com/images/blue-widget.jpg",
  "nmfc": "156600",
  "commodityDescription": "Plastic Widgets",
  "htsCode": "3926.90.9990",
  "countryOfOrigin": "US",
  "packsizes": [
    {
      "name": "Case",
      "eachCount": 12,
      "barcodeValue": "10123456789012",
      "barcodeType": "Code128",
      "height": 12.0,
      "width": 16.0,
      "length": 20.0,
      "weight": 30.0,
      "palletTie": 8,
      "palletHeight": 4
    }
  ],
  "productComponents": []
}
```

### Response (404 Not Found)
```json
"Product not found"
```

---

## CREATE PRODUCT

### Endpoint
```http
POST /products
```

### Request Body
```json
{
  "description": "Green Widget - Eco Friendly",
  "upc": "555666777888",
  "barcodeType": "EAN13",
  "barcodeValue": "5556667778889",
  "referenceNumber": "REF-003",
  "clientId": "6ba7b810-9dad-11d1-80b4-00c04fd430c8",
  "sku": "SKU789",
  "isLotControlled": false,
  "lotPattern": null,
  "isSerialControlled": false,
  "isBillOfMaterial": false,
  "serialPattern": null,
  "isExpiryControlled": false,
  "isDecimalControlled": false,
  "isPacksizeControlled": false,
  "palletTie": 12,
  "palletHeight": 6,
  "height": 9.0,
  "length": 14.0,
  "width": 7.5,
  "weight": 2.2,
  "dimsLengthUnitOfMeasure": "IN",
  "dimsWeightUnitOfMeasure": "LB",
  "unitOfMeasure": "EA",
  "category": "Widgets",
  "freightClass": "Class50",
  "image": null,
  "nmfc": "156600",
  "commodityDescription": "Plastic Widgets",
  "htsCode": "3926.90.9990",
  "countryOfOrigin": "US",
  "packsizes": [],
  "productComponents": []
}
```

### Response (200 OK)
Returns the created product in ProductGetList format:
```json
{
  "description": "Green Widget - Eco Friendly",
  "upc": "555666777888",
  "barcodeType": "EAN13",
  "barcodeValue": "5556667778889",
  "referenceNumber": "REF-003",
  "id": "a8c9b756-2345-4567-8901-234567890123",
  "client": {
    "description": "Main Warehouse Client",
    "ssccCompanyId": "1234567",
    "id": "6ba7b810-9dad-11d1-80b4-00c04fd430c8",
    "name": "Acme Corp"
  },
  "sku": "SKU789"
}
```

### Response (406 Not Acceptable)
```json
"Validation error message"
```

---

## UPDATE PRODUCT

### Endpoint
```http
PUT /products
```

### Request Body
```json
{
  "id": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "description": "Blue Widget - Updated Description",
  "upc": "123456789012",
  "barcodeType": "EAN13",
  "barcodeValue": "1234567890123",
  "referenceNumber": "REF-001-UPD",
  "sku": "SKU123",
  "isLotControlled": false,
  "lotPattern": null,
  "isSerialControlled": false,
  "isBillOfMaterial": false,
  "serialPattern": null,
  "isExpiryControlled": false,
  "isDecimalControlled": false,
  "isPacksizeControlled": true,
  "palletTie": 10,
  "palletHeight": 5,
  "height": 10.5,
  "length": 15.0,
  "width": 8.0,
  "weight": 2.6,
  "dimsLengthUnitOfMeasure": "IN",
  "dimsWeightUnitOfMeasure": "LB",
  "unitOfMeasure": "EA",
  "category": "Widgets",
  "freightClass": "Class50",
  "image": null,
  "nmfc": "156600",
  "commodityDescription": "Plastic Widgets",
  "htsCode": "3926.90.9990",
  "countryOfOrigin": "US",
  "packsizes": [],
  "productComponents": []
}
```

### Response (200 OK)
Returns the updated product in ProductGetList format

### Response (406 Not Acceptable)
```json
"Validation error message"
```

---

## DELETE PRODUCT

### Endpoint
```http
DELETE /products/{id}
```

### Path Parameters
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| id | uuid | Yes | Product ID to delete |

### Example Request
```http
DELETE https://api.p4warehouse.com/products/f47ac10b-58cc-4372-a567-0e02b2c3d479
Headers:
  ApiKey: YOUR_API_KEY_HERE
```

### Response (200 OK)
No content returned

### Response (406 Not Acceptable)
```json
"Cannot delete product with existing inventory"
```

---

## FIELD DEFINITIONS

### BarcodeType Enum
- `Code128`
- `ITF14`
- `EAN13`
- `UPCA`

### FreightClass Enum
- `Class50`
- `Class55`
- `Class60`
- `Class65`
- `Class70`
- `Class77p5`
- `Class85`
- `Class92p5`
- `Class100`
- `Class110`
- `Class125`
- `Class150`
- `Class175`
- `Class200`
- `Class250`
- `Class300`
- `Class400`
- `Class500`

### UnitOfMeasure
Common values include:
- `EA` - Each
- `CS` - Case
- `PLT` - Pallet
- `IN` - Inches
- `LB` - Pounds
- `KG` - Kilograms
- `M` - Meters

### Product Control Flags
- `isLotControlled` - Requires lot number tracking
- `isSerialControlled` - Requires serial number tracking
- `isExpiryControlled` - Requires expiry date tracking
- `isDecimalControlled` - Allows decimal quantities
- `isPacksizeControlled` - Uses multiple pack sizes
- `isBillOfMaterial` - Product has components

---

## TESTING WITH CURL

### Get All Products
```bash
curl -X GET "https://api.p4warehouse.com/products?Page=1&PageSize=10" \
  -H "ApiKey: YOUR_API_KEY_HERE" \
  -H "Accept: application/json"
```

### Get Product by ID
```bash
curl -X GET "https://api.p4warehouse.com/products/f47ac10b-58cc-4372-a567-0e02b2c3d479" \
  -H "ApiKey: YOUR_API_KEY_HERE" \
  -H "Accept: application/json"
```

### Create Product
```bash
curl -X POST "https://api.p4warehouse.com/products" \
  -H "ApiKey: YOUR_API_KEY_HERE" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json" \
  -d '{
    "sku": "SKU999",
    "description": "Test Product",
    "clientId": "6ba7b810-9dad-11d1-80b4-00c04fd430c8",
    "isLotControlled": false,
    "isSerialControlled": false,
    "isExpiryControlled": false,
    "isDecimalControlled": false,
    "isPacksizeControlled": false,
    "isBillOfMaterial": false
  }'
```

### Update Product
```bash
curl -X PUT "https://api.p4warehouse.com/products" \
  -H "ApiKey: YOUR_API_KEY_HERE" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json" \
  -d '{
    "id": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
    "sku": "SKU123",
    "description": "Updated Description",
    "isLotControlled": false,
    "isSerialControlled": false,
    "isExpiryControlled": false,
    "isDecimalControlled": false,
    "isPacksizeControlled": false,
    "isBillOfMaterial": false
  }'
```

### Delete Product
```bash
curl -X DELETE "https://api.p4warehouse.com/products/f47ac10b-58cc-4372-a567-0e02b2c3d479" \
  -H "ApiKey: YOUR_API_KEY_HERE"
```