# Greenwood Clubhouse — Facilities Booking System

ASP.NET Core MVC (C# / .NET 10) implementation of a web-based clubhouse facilities booking platform.

## Modules

1. **Booking Management** — browse availability, reserve slots, waiting list, approval workflow, temporary payment holds  
2. **Facility & Inventory Registry** — facilities, operating hours, prices, inventory, maintenance block-outs  
3. **Membership & Subscription Management** — registration, plans, validity, tier-based privileges  
4. **Visitor Access & Check-in** — guest registration, QR digital passes, staff check-in/out  
5. **Payment & Billing Management** — invoices, simulated FPX/card/e-wallet payments, refund policy  
6. **Reporting & Analytics Management** — dashboards, usage charts, CSV export  

## Roles

| Role | Demo login |
|------|------------|
| Member | `member@clubhouse.local` / `Member@12345` |
| Staff | `staff@clubhouse.local` / `Staff@12345` |
| Administrator | `admin@clubhouse.local` / `Admin@12345` |

## Stack

- ASP.NET Core MVC + Identity  
- Entity Framework Core with SQLite (`clubhouse.db`)  
- Bootstrap + custom clubhouse UI  
- QRCoder for visitor passes  
- Simulated payment gateway (MYR)

## Run locally

```bash
cd MIniProject
dotnet restore
dotnet run
```

Open the printed URL (typically `http://localhost:5xxx`).

## Notes

- Single clubhouse scope; English UI; MYR currency  
- PDPA consent captured at registration  
- Slot holds expire after the facility’s configured minutes (default 15)  
- Refund defaults: 48h full / 24–48h half / &lt;24h none (admin-configurable)
