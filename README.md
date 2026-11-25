# Kota Permai Golf & Country Club — Facilities Booking System

ASP.NET Core MVC (C# / .NET 10) booking platform inspired by **Kota Permai Golf & Country Club** (Kota Kemuning, Shah Alam).

## Facilities seeded from the club

| Facility | Category |
|----------|----------|
| Permai Ballroom | Banquet |
| Danau Function Room | Meeting |
| VIP Room | Meeting |
| Indoor Badminton Court | Sports |
| Tennis Court | Sports |
| Squash Court | Sports |
| Pickleball Court | Sports |
| Table Tennis | Sports |
| Olympic Lap Pool | Sports |
| Gymnasium | Sports |
| Driving Range Bay | Golf |
| Multi-Purpose Room | Leisure |

Photos are stored under `MIniProject/wwwroot/images/`.

### Sample pricing (MYR)

| Membership | Annual fee |
|------------|------------|
| Individual | 7,000 |
| Family | 12,500 |
| Premium | 35,000 |

Facility hourly rates range from member-included (pool/gym) to sports courts (~RM12–45/hr), driving range (RM48/hr), function rooms (RM180–380/hr) and Permai Ballroom (RM1,200/hr).

## Modules

1. Booking Management  
2. Facility & Inventory Registry  
3. Membership & Subscription  
4. Visitor Access & Check-in  
5. Payment & Billing (card details / e-wallet QR)  
6. Reporting & Analytics  

## Demo accounts

| Role | Email | Password |
|------|-------|----------|
| Member | `member@kotapermai.local` | `Member@12345` |
| Staff | `staff@kotapermai.local` | `Staff@12345` |
| Administrator | `admin@kotapermai.local` | `Admin@12345` |

Legacy `@clubhouse.local` accounts still work.

## Run

```bash
cd MIniProject
# Optional: delete clubhouse.db to force a clean Kota Permai seed
dotnet run --launch-profile http
```

Data is stored in SQLite: `MIniProject/clubhouse.db`.

## Email (password reset & booking confirmation)

Real inbox delivery requires SMTP. With an empty `Email:Smtp:Host`, Development writes message files under `MIniProject/App_Data/emails/` only when `Email:AllowFileFallback` is `true` (default in `appsettings.Development.json`). Outside Development, missing SMTP returns a clear error to the user — there is no silent “success”.

**Do not commit real SMTP credentials.** Use user secrets or environment variables (ASP.NET Core double-underscore form).

### Required settings for real SMTP

| Setting | Env var | Purpose |
|---------|---------|---------|
| `Email:Enabled` | `Email__Enabled` | Must be `true` |
| `Email:FromAddress` | `Email__FromAddress` | From address (must be allowed by your SMTP provider) |
| `Email:FromName` | `Email__FromName` | Display name |
| `Email:Smtp:Host` | `Email__Smtp__Host` | SMTP host (required for real delivery) |
| `Email:Smtp:Port` | `Email__Smtp__Port` | Usually `587` (STARTTLS) or `465` |
| `Email:Smtp:EnableSsl` | `Email__Smtp__EnableSsl` | Usually `true` |
| `Email:Smtp:UserName` | `Email__Smtp__UserName` | SMTP auth username |
| `Email:Smtp:Password` | `Email__Smtp__Password` | SMTP auth password / app password |
| `Email:OtpExpiryMinutes` | `Email__OtpExpiryMinutes` | Forgot-password OTP lifetime (default 15) |
| `Email:ResetLinkExpiryHours` | `Email__ResetLinkExpiryHours` | Reset-link token lifetime (default 1) |
| `Email:AllowFileFallback` | `Email__AllowFileFallback` | Dev-only local file write when Host is empty |

### Configure with user secrets (recommended locally)

```bash
cd MIniProject
dotnet user-secrets set "Email:Smtp:Host" "smtp.your-provider.example"
dotnet user-secrets set "Email:Smtp:Port" "587"
dotnet user-secrets set "Email:Smtp:EnableSsl" "true"
dotnet user-secrets set "Email:Smtp:UserName" "your-smtp-username"
dotnet user-secrets set "Email:Smtp:Password" "your-smtp-password-or-app-password"
dotnet user-secrets set "Email:FromAddress" "noreply@your-domain.example"
dotnet user-secrets set "Email:FromName" "Kota Permai Clubhouse"
```

Or set the `Email__Smtp__*` environment variables above before `dotnet run`.

Forgot password validates that the email belongs to a **registered member**; unknown addresses show an error (no silent success). It sends a 6-digit OTP and a reset link when delivery succeeds. Booking confirmation emails are attempted whenever a booking reaches **Confirmed** (free instant confirm, paid after payment, or zero-amount after staff approval); SMTP failures are logged.

## Reference

Club information based on [kotapermai.com.my](https://www.kotapermai.com.my/).
