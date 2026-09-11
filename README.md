# LinkLab API

LinkLab API is the ASP.NET Core backend for LinkLab, a platform where creatives can publish collaboration posts, apply to work together, and share photo galleries. Gallery images are stored in a private Amazon S3 bucket and uploaded with pre-signed URLs.

## Features

- Email and password registration with JWT authentication
- Public collaboration posts with paginated listing
- Applications that post owners can accept or reject
- Private and published galleries
- Gallery access restricted to owners until publication
- Direct image uploads to Amazon S3 using pre-signed URLs
- Swagger/OpenAPI documentation
- Global error handling with Problem Details responses
- Integration tests using xUnit, `WebApplicationFactory`, and SQLite in-memory

## Tech Stack

- .NET 10 and ASP.NET Core Web API
- Entity Framework Core
- PostgreSQL with Npgsql
- Amazon S3
- JWT bearer authentication
- BCrypt password hashing
- Swagger with Swashbuckle
- xUnit integration tests

## Project Structure

```text
linklab-api/
|-- LinkLab.Api/          # API source, controllers, DTOs, domain models, and migrations
|-- LinkLab.Api.Tests/    # Integration tests and disposable test database setup
|-- linklab-api.sln       # .NET solution containing both projects
|-- LICENSE
`-- README.md
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- PostgreSQL
- An AWS account, private S3 bucket, and IAM credentials for photo upload features
- `dotnet-ef` for applying migrations

Install the EF Core CLI if it is not already available:

```bash
dotnet tool install --global dotnet-ef
```

## Local Setup

1. Clone the repository and enter its directory:

```bash
git clone <repository-url>
cd linklab-api
```

2. Store local configuration with .NET user secrets. Replace all placeholder values with your own:

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

Do not commit real database passwords, JWT keys, or AWS credentials.

3. Create the PostgreSQL database, then restore dependencies and apply migrations:

```bash
createdb linklab
dotnet restore
dotnet ef database update --project LinkLab.Api --startup-project LinkLab.Api
```

4. Start the API:

```bash
dotnet run --project LinkLab.Api
```

The development server is available at:

- API: `http://localhost:5125`
- Swagger UI: `http://localhost:5125/swagger`
- Health check: `http://localhost:5125/api/health`

The local frontend origin allowed by CORS is `http://localhost:3000`.

## Authentication

Register or log in to receive a JWT. Protected requests must send it in the HTTP header:

```http
Authorization: Bearer YOUR_JWT
```

In Swagger, select **Authorize** and enter the JWT to test protected endpoints.

## API Overview

| Method | Route | Access | Purpose |
| --- | --- | --- | --- |
| `GET` | `/api/health` | Public | Check API health |
| `POST` | `/api/auth/register` | Public | Register and receive a JWT |
| `POST` | `/api/auth/login` | Public | Log in and receive a JWT |
| `GET` | `/api/auth/me` | Authenticated | Get the current user |
| `GET` | `/api/posts` | Public | List collaboration posts with pagination |
| `GET` | `/api/posts/{id}` | Public | Get one collaboration post |
| `POST` | `/api/posts` | Authenticated | Create a collaboration post |
| `POST` | `/api/posts/{postId}/apply` | Authenticated | Apply to a collaboration post |
| `GET` | `/api/posts/{postId}/applications` | Post owner | List applications for a post |
| `POST` | `/api/applications/{id}/accept` | Post owner | Accept an application |
| `POST` | `/api/applications/{id}/reject` | Post owner | Reject an application |
| `GET` | `/api/galleries` | Public | List published galleries with pagination |
| `GET` | `/api/galleries/mine` | Authenticated | List the current user's galleries |
| `POST` | `/api/galleries` | Authenticated | Create a gallery |
| `GET` | `/api/galleries/{galleryId}/photos` | Conditional | View a published gallery, or a private gallery as its owner |
| `POST` | `/api/galleries/{galleryId}/photos/upload-url` | Gallery owner | Create a pre-signed S3 upload URL |
| `POST` | `/api/galleries/{galleryId}/photos` | Gallery owner | Save uploaded photo metadata |
| `POST` | `/api/galleries/{galleryId}/publish` | Gallery owner | Publish a gallery |
| `POST` | `/api/galleries/{galleryId}/unpublish` | Gallery owner | Make a gallery private |

Paginated list endpoints accept `page` and `pageSize` query parameters, for example:

```http
GET /api/posts?page=2&pageSize=10
```

## Photo Upload Flow

Photo upload is a three-request flow:

1. The client requests an upload URL from `POST /api/galleries/{galleryId}/photos/upload-url`, sending the file name and content type.
2. The client sends the image bytes directly to the returned pre-signed S3 URL with an HTTP `PUT`. The `Content-Type` must match the value used when requesting the URL.
3. After S3 accepts the upload, the client calls `POST /api/galleries/{galleryId}/photos` with the returned `photoId`, `objectKey`, and an optional caption.

The API checks gallery ownership and verifies that the expected object exists in S3 before saving its metadata to PostgreSQL.

## Tests

Run all integration tests from the repository root:

```bash
dotnet test
```

The test project replaces PostgreSQL with a fresh SQLite in-memory database. Test records are disposable and do not modify the local development database.

## License

This project is licensed under the [MIT License](LICENSE).
