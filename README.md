# LinkLab API

Backend API for **LinkLab**, a full-stack creative collaboration platform where photographers, models, videographers, and other creatives can publish collaboration opportunities, apply to projects, organise moodboards, and share completed work through galleries.

This repository contains the **ASP.NET Core backend** for LinkLab.

- **Frontend:** [linklab-web](https://github.com/tanbeoi/linklab-web)
- **Live Application:** [linklab-web.vercel.app](https://linklab-web.vercel.app/dashboard)

---

## Overview

LinkLab connects creatives throughout the lifecycle of a project:

```text
Collaboration Post
        ↓
Applications
        ↓
Accepted Collaborators
        ↓
Moodboard / Planning
        ↓
Gallery / Final Work
```

The API handles authentication, collaboration posts, applications, gallery permissions, image metadata, and direct Amazon S3 uploads.

---

## Architecture

```text
        Next.js Frontend
              |
              | REST API
              v
       ASP.NET Core API
          /         \
         /           \
 PostgreSQL        Amazon S3
 App Data          Image Storage
```

Images are uploaded directly from the frontend to a private S3 bucket using **pre-signed URLs**, while PostgreSQL stores application and image metadata.

---

## Features

- Email and password authentication using JWT
- Public collaboration posts with pagination
- Applications that project owners can accept or reject
- User-specific submitted and received application views
- Private and published galleries
- Gallery ownership and access control
- Direct image uploads to Amazon S3 using pre-signed URLs
- PostgreSQL persistence with Entity Framework Core
- Swagger/OpenAPI documentation
- Global error handling using Problem Details
- Integration testing with xUnit, `WebApplicationFactory`, and SQLite in-memory

---

## Tech Stack

### Backend
- .NET 10
- ASP.NET Core Web API
- C#

### Data
- Entity Framework Core
- PostgreSQL
- Npgsql

### Authentication
- JWT Bearer Authentication
- BCrypt password hashing

### Cloud
- Amazon S3
- Pre-signed upload URLs

### Testing & Documentation
- xUnit
- `WebApplicationFactory`
- SQLite in-memory testing
- Swagger / OpenAPI

---

## Project Structure

```text
linklab-api/
|-- LinkLab.Api/          # API source, controllers, DTOs, domain models, migrations
|-- LinkLab.Api.Tests/    # Integration tests and disposable test database setup
|-- linklab-api.sln       # .NET solution
|-- LICENSE
`-- README.md
```

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- PostgreSQL
- AWS account
- Private Amazon S3 bucket
- IAM credentials with access to the S3 bucket
- `dotnet-ef`

Install the EF Core CLI if required:

```bash
dotnet tool install --global dotnet-ef
```

---

## Local Setup

### 1. Clone the repository

```bash
git clone https://github.com/tanbeoi/linklab-api.git
cd linklab-api
```

### 2. Configure local secrets

LinkLab uses .NET user secrets for development configuration.

Replace the placeholder values below with your own:

```bash
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=linklab;Username=postgres;Password=YOUR_PASSWORD" --project LinkLab.Api

dotnet user-secrets set "Jwt:Issuer" "LinkLab.Api" --project LinkLab.Api
dotnet user-secrets set "Jwt:Audience" "LinkLab.Web" --project LinkLab.Api
dotnet user-secrets set "Jwt:Key" "REPLACE_WITH_A_LONG_RANDOM_SECRET" --project LinkLab.Api

dotnet user-secrets set "AWS:AccessKeyId" "YOUR_ACCESS_KEY_ID" --project LinkLab.Api
dotnet user-secrets set "AWS:SecretAccessKey" "YOUR_SECRET_ACCESS_KEY" --project LinkLab.Api
dotnet user-secrets set "AWS:Region" "ap-southeast-2" --project LinkLab.Api
dotnet user-secrets set "AWS:BucketName" "YOUR_PRIVATE_BUCKET_NAME" --project LinkLab.Api
```

Do not commit database passwords, JWT secrets, or AWS credentials.

### 3. Create the database and apply migrations

```bash
createdb linklab

dotnet restore

dotnet ef database update \
  --project LinkLab.Api \
  --startup-project LinkLab.Api
```

### 4. Start the API

```bash
dotnet run --project LinkLab.Api
```

Development endpoints:

- API: `http://localhost:5125`
- Swagger: `http://localhost:5125/swagger`
- Health Check: `http://localhost:5125/api/health`

The local frontend origin allowed by CORS is:

```text
http://localhost:3000
```

---

## Authentication

Register or log in to receive a JWT.

Protected API requests must include:

```http
Authorization: Bearer YOUR_JWT
```

Swagger also supports authenticated requests through the **Authorize** button.

---

## API Overview

| Method | Route | Access | Purpose |
| --- | --- | --- | --- |
| `GET` | `/api/health` | Public | Check API health |
| `POST` | `/api/auth/register` | Public | Register and receive a JWT |
| `POST` | `/api/auth/login` | Public | Log in and receive a JWT |
| `GET` | `/api/auth/me` | Authenticated | Get the current user |
| `GET` | `/api/posts` | Public | List collaboration posts |
| `GET` | `/api/posts/mine` | Authenticated | List the current user's posts |
| `GET` | `/api/posts/{id}` | Public | Get one collaboration post |
| `POST` | `/api/posts` | Authenticated | Create a collaboration post |
| `POST` | `/api/posts/{postId}/apply` | Authenticated | Apply to a post |
| `GET` | `/api/posts/{postId}/applications` | Post owner | View applications for a post |
| `GET` | `/api/applications/mine` | Authenticated | View submitted applications |
| `GET` | `/api/applications/mine/accepted` | Authenticated | View accepted collaborations |
| `GET` | `/api/applications/received` | Authenticated | View applications received |
| `POST` | `/api/applications/{id}/accept` | Post owner | Accept an application |
| `POST` | `/api/applications/{id}/reject` | Post owner | Reject an application |
| `GET` | `/api/posts/{id}/moodboard` | Public | Get linked published moodboard |
| `GET` | `/api/galleries` | Public | List published galleries |
| `GET` | `/api/galleries/mine` | Authenticated | List the current user's galleries |
| `POST` | `/api/galleries` | Authenticated | Create a gallery |
| `GET` | `/api/galleries/{galleryId}/photos` | Conditional | View gallery photos |
| `POST` | `/api/galleries/{galleryId}/photos/upload-url` | Gallery owner | Generate S3 upload URL |
| `POST` | `/api/galleries/{galleryId}/photos` | Gallery owner | Save uploaded photo metadata |
| `POST` | `/api/galleries/{galleryId}/publish` | Gallery owner | Publish gallery |
| `POST` | `/api/galleries/{galleryId}/unpublish` | Gallery owner | Make gallery private |

Paginated endpoints support:

```text
page
pageSize
```

Example:

```http
GET /api/posts?page=2&pageSize=10
```

---

## Photo Upload Flow

LinkLab uses direct browser-to-S3 uploads so image files do not pass through the API server.

```text
Frontend
   |
   | 1. Request upload URL
   v
API
   |
   | 2. Return pre-signed S3 URL
   v
Frontend
   |
   | 3. Upload image directly
   v
Amazon S3
   |
   | 4. Save metadata
   v
API → PostgreSQL
```

The complete flow is:

1. Client requests an upload URL from:

```http
POST /api/galleries/{galleryId}/photos/upload-url
```

2. The API verifies gallery ownership and generates a pre-signed S3 URL.

3. The client uploads the image directly to S3 using HTTP `PUT`.

4. The client calls:

```http
POST /api/galleries/{galleryId}/photos
```

with the returned `photoId`, `objectKey`, and optional caption.

5. The API verifies that the object exists in S3 before saving its metadata to PostgreSQL.

This keeps large image uploads away from the application server while maintaining ownership and access control through the API.

---

## Testing

Run all integration tests:

```bash
dotnet test
```

Integration tests use:

- xUnit
- `WebApplicationFactory`
- SQLite in-memory database

Each test database is disposable and does not modify the local PostgreSQL development database.

---

## Related Repository

### LinkLab Web

The frontend is maintained separately:

[github.com/tanbeoi/linklab-web](https://github.com/tanbeoi/linklab-web)

Built with:

- Next.js
- TypeScript
- React
- Tailwind CSS

The frontend consumes this API and handles the user-facing collaboration, application, moodboard, and gallery workflows.

---

## License

This project is licensed under the [MIT License](LICENSE).
