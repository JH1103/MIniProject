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

Configure the `Email` section in `appsettings.json` / user secrets / environment variables. Leave `Email:Smtp:Host` empty for local/dev: messages are written as text files under `MIniProject/App_Data/emails/` (see logs for the path). Do not commit real SMTP credentials.

| Setting | Purpose |
|---------|---------|
| `Email:Smtp:Host` / `Port` / `EnableSsl` | SMTP server |
| `Email:Smtp:UserName` / `Password` | Optional SMTP auth (use secrets, not source control) |
| `Email:FromAddress` / `FromName` | From header |
| `Email:OtpExpiryMinutes` | Forgot-password OTP lifetime (default 15) |
| `Email:ResetLinkExpiryHours` | Reset-link token lifetime (default 1) |
| `Email:FallbackDirectory` | Local file fallback when SMTP is unset |

Forgot password sends both a 6-digit OTP and a reset link. Booking confirmation emails go out when a booking reaches **Confirmed** (free instant confirm, paid after payment, or zero-amount after staff approval).

## Reference

Club information based on [kotapermai.com.my](https://www.kotapermai.com.my/).
