# Email verification and password reset setup

This branch adds email verification on registration and password reset by email.

## What changed

- New backend endpoints:
  - `POST /api/auth/register` creates an unverified account and sends a verification email.
  - `POST /api/auth/verify-email` verifies the email token.
  - `POST /api/auth/resend-verification` sends a new verification link when needed.
  - `POST /api/auth/forgot-password` sends a reset link without revealing whether the account exists.
  - `POST /api/auth/reset-password` validates the reset token and changes the password.
- New database migration:
  - `20260610183000_AddAuthEmailVerificationAndPasswordReset`
- New frontend pages:
  - `/verify-email-sent`
  - `/verify-email`
  - `/forgot-password`
  - `/reset-password`

## SMTP configuration

You do **not** need to install an SMTP server on the backend machine. The backend connects to an external SMTP/transactional email provider.

In production, set these as environment variables or server secrets:

```bash
Email__Enabled=true
Email__Host=smtp.example.com
Email__Port=587
Email__UseSsl=true
Email__UserName=your-smtp-username
Email__Password=your-smtp-password
Email__FromEmail=no-reply@your-domain.com
Email__FromName=GeoTrack
Email__FrontendBaseUrl=https://softeng.pmf.kg.ac.rs:10131
```

For local development, `Email:Enabled` defaults to `false`. In that mode the backend logs the generated links instead of sending real email.

## Deployment notes

Run the normal EF migration step after deploying the backend:

```bash
dotnet ef database update --project Geotrack.Infrastructure --startup-project Geotrack.Server --context AppDbContext
```

Existing users are marked as email-confirmed by the migration so admin/demo accounts do not get locked out.
