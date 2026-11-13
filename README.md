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

## Reference

Club information based on [kotapermai.com.my](https://www.kotapermai.com.my/).
