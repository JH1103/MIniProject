using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MIniProject.Data;
using MIniProject.Models;
using MIniProject.Services;
using MIniProject.ViewModels;

namespace MIniProject.Controllers;

[Authorize]
public class EquipmentRentalsController : Controller
{
    private const decimal FeePerUnitPerDay = 15m;

    private readonly ApplicationDbContext _db;
    private readonly BookingService _bookingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public EquipmentRentalsController(
        ApplicationDbContext db,
        BookingService bookingService,
        UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _bookingService = bookingService;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var isStaff = User.IsInRole(AppRoles.Staff) || User.IsInRole(AppRoles.Administrator);
        var query = _db.EquipmentRentals
            .Include(r => r.InventoryItem)!.ThenInclude(i => i!.Facility)
            .Include(r => r.Member)
            .Include(r => r.Booking)
            .AsQueryable();

        if (!isStaff)
        {
            query = query.Where(r => r.MemberId == user.Id);
        }

        var rentals = await query.OrderByDescending(r => r.CreatedAt).Take(100).ToListAsync();
        return View(rentals);
    }

    public async Task<IActionResult> Details(int id)
    {
        var rental = await LoadRentalAsync(id);
        if (rental is null)
        {
            return NotFound();
        }

        if (!await CanAccessAsync(rental))
        {
            return Forbid();
        }

        return View(rental);
    }

    [Authorize(Roles = AppRoles.Member)]
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var sub = await _bookingService.GetActiveSubscriptionAsync(user.Id);
        if (sub is null)
        {
            TempData["Error"] = "An active membership is required to rent equipment.";
            return RedirectToAction("Plans", "Membership");
        }

        return View(await BuildFormAsync(user.Id, new EquipmentRentalFormViewModel()));
    }

    [Authorize(Roles = AppRoles.Member)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(EquipmentRentalFormViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var sub = await _bookingService.GetActiveSubscriptionAsync(user.Id);
        if (sub is null)
        {
            TempData["Error"] = "An active membership is required to rent equipment.";
            return RedirectToAction("Plans", "Membership");
        }

        if (!TryParseWindow(model, out var startUtc, out var dueUtc, out var windowError))
        {
            ModelState.AddModelError(string.Empty, windowError!);
        }

        if (!ModelState.IsValid)
        {
            return View(await BuildFormAsync(user.Id, model));
        }

        var item = await _db.InventoryItems
            .Include(i => i.Facility)
            .FirstOrDefaultAsync(i => i.Id == model.InventoryItemId);
        if (item is null || item.Condition == InventoryCondition.Retired)
        {
            ModelState.AddModelError(nameof(model.InventoryItemId), "Selected equipment is not available.");
            return View(await BuildFormAsync(user.Id, model));
        }

        if (model.Quantity > item.Quantity)
        {
            ModelState.AddModelError(nameof(model.Quantity), $"Only {item.Quantity} unit(s) available.");
            return View(await BuildFormAsync(user.Id, model));
        }

        if (model.BookingId is int bookingId)
        {
            var bookingOk = await _db.Bookings.AnyAsync(b =>
                b.Id == bookingId &&
                b.MemberId == user.Id &&
                b.Status != BookingStatus.Cancelled);
            if (!bookingOk)
            {
                ModelState.AddModelError(nameof(model.BookingId), "Linked booking was not found.");
                return View(await BuildFormAsync(user.Id, model));
            }
        }

        var days = Math.Max(1, (int)Math.Ceiling((dueUtc - startUtc).TotalDays));
        var fee = Math.Round(FeePerUnitPerDay * model.Quantity * days, 2);

        item.Quantity -= model.Quantity;
        _db.EquipmentRentals.Add(new EquipmentRental
        {
            MemberId = user.Id,
            InventoryItemId = item.Id,
            BookingId = model.BookingId,
            Quantity = model.Quantity,
            StartUtc = startUtc,
            DueUtc = dueUtc,
            Status = EquipmentRentalStatus.Active,
            Fee = fee,
            Notes = model.Notes
        });
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Rental created for {item.Name} (MYR {fee:N2}).";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var rental = await LoadRentalAsync(id);
        if (rental is null)
        {
            return NotFound();
        }

        if (!await CanAccessAsync(rental))
        {
            return Forbid();
        }

        if (rental.Status is EquipmentRentalStatus.Returned or EquipmentRentalStatus.Cancelled)
        {
            TempData["Error"] = "Returned or cancelled rentals cannot be edited.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var model = new EquipmentRentalFormViewModel
        {
            Id = rental.Id,
            InventoryItemId = rental.InventoryItemId,
            Quantity = rental.Quantity,
            StartDate = rental.StartUtc.ToLocalTime().Date,
            StartTime = rental.StartUtc.ToLocalTime().ToString("HH:mm"),
            DueDate = rental.DueUtc.ToLocalTime().Date,
            DueTime = rental.DueUtc.ToLocalTime().ToString("HH:mm"),
            BookingId = rental.BookingId,
            Notes = rental.Notes,
            Status = rental.Status
        };

        return View(await BuildFormAsync(rental.MemberId, model));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EquipmentRentalFormViewModel model)
    {
        var rental = await _db.EquipmentRentals
            .Include(r => r.InventoryItem)
            .FirstOrDefaultAsync(r => r.Id == model.Id);
        if (rental is null)
        {
            return NotFound();
        }

        if (!await CanAccessAsync(rental))
        {
            return Forbid();
        }

        if (rental.Status is EquipmentRentalStatus.Returned or EquipmentRentalStatus.Cancelled)
        {
            TempData["Error"] = "Returned or cancelled rentals cannot be edited.";
            return RedirectToAction(nameof(Details), new { id = rental.Id });
        }

        if (!TryParseWindow(model, out var startUtc, out var dueUtc, out var windowError))
        {
            ModelState.AddModelError(string.Empty, windowError!);
        }

        if (!ModelState.IsValid)
        {
            return View(await BuildFormAsync(rental.MemberId, model));
        }

        // Restore previous stock, then apply new quantity against current item.
        if (rental.InventoryItem is not null)
        {
            rental.InventoryItem.Quantity += rental.Quantity;
        }

        var item = await _db.InventoryItems.FirstOrDefaultAsync(i => i.Id == model.InventoryItemId);
        if (item is null || item.Condition == InventoryCondition.Retired)
        {
            ModelState.AddModelError(nameof(model.InventoryItemId), "Selected equipment is not available.");
            return View(await BuildFormAsync(rental.MemberId, model));
        }

        if (model.Quantity > item.Quantity)
        {
            ModelState.AddModelError(nameof(model.Quantity), $"Only {item.Quantity} unit(s) available.");
            return View(await BuildFormAsync(rental.MemberId, model));
        }

        item.Quantity -= model.Quantity;
        var days = Math.Max(1, (int)Math.Ceiling((dueUtc - startUtc).TotalDays));
        rental.InventoryItemId = item.Id;
        rental.Quantity = model.Quantity;
        rental.StartUtc = startUtc;
        rental.DueUtc = dueUtc;
        rental.BookingId = model.BookingId;
        rental.Notes = model.Notes;
        rental.Fee = Math.Round(FeePerUnitPerDay * model.Quantity * days, 2);
        if (User.IsInRole(AppRoles.Staff) || User.IsInRole(AppRoles.Administrator))
        {
            rental.Status = model.Status;
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = "Rental updated.";
        return RedirectToAction(nameof(Details), new { id = rental.Id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Return(int id)
    {
        var rental = await _db.EquipmentRentals
            .Include(r => r.InventoryItem)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (rental is null)
        {
            return NotFound();
        }

        if (!await CanAccessAsync(rental))
        {
            return Forbid();
        }

        if (rental.Status != EquipmentRentalStatus.Active)
        {
            TempData["Error"] = "Only active rentals can be returned.";
            return RedirectToAction(nameof(Details), new { id });
        }

        rental.Status = EquipmentRentalStatus.Returned;
        rental.ReturnedAt = DateTime.UtcNow;
        if (rental.InventoryItem is not null)
        {
            rental.InventoryItem.Quantity += rental.Quantity;
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = "Equipment marked as returned.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var rental = await _db.EquipmentRentals
            .Include(r => r.InventoryItem)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (rental is null)
        {
            return NotFound();
        }

        var isStaff = User.IsInRole(AppRoles.Staff) || User.IsInRole(AppRoles.Administrator);
        if (!isStaff && rental.MemberId != (await _userManager.GetUserAsync(User))?.Id)
        {
            return Forbid();
        }

        // Members may delete only invalid/cancelled records; staff may delete invalid/cancelled/returned.
        var deletable = rental.Status is EquipmentRentalStatus.Invalid or EquipmentRentalStatus.Cancelled
                        || (isStaff && rental.Status == EquipmentRentalStatus.Returned);
        if (!deletable)
        {
            // Mark active overdue rentals as Invalid then allow cleanup, or block.
            if (rental.Status == EquipmentRentalStatus.Active && rental.DueUtc < DateTime.UtcNow && isStaff)
            {
                if (rental.InventoryItem is not null)
                {
                    rental.InventoryItem.Quantity += rental.Quantity;
                }

                rental.Status = EquipmentRentalStatus.Invalid;
                await _db.SaveChangesAsync();
            }

            if (rental.Status is not (EquipmentRentalStatus.Invalid or EquipmentRentalStatus.Cancelled)
                && !(isStaff && rental.Status == EquipmentRentalStatus.Returned))
            {
                TempData["Error"] = "Only invalid or cancelled rental records can be deleted.";
                return RedirectToAction(nameof(Details), new { id });
            }
        }

        if (rental.Status == EquipmentRentalStatus.Active && rental.InventoryItem is not null)
        {
            rental.InventoryItem.Quantity += rental.Quantity;
        }

        _db.EquipmentRentals.Remove(rental);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Invalid rental record deleted.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = AppRoles.Staff + "," + AppRoles.Administrator)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkInvalid(int id)
    {
        var rental = await _db.EquipmentRentals
            .Include(r => r.InventoryItem)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (rental is null)
        {
            return NotFound();
        }

        if (rental.Status == EquipmentRentalStatus.Active && rental.InventoryItem is not null)
        {
            rental.InventoryItem.Quantity += rental.Quantity;
        }

        rental.Status = EquipmentRentalStatus.Invalid;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Rental marked as invalid.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<EquipmentRental?> LoadRentalAsync(int id) =>
        await _db.EquipmentRentals
            .Include(r => r.InventoryItem)!.ThenInclude(i => i!.Facility)
            .Include(r => r.Member)
            .Include(r => r.Booking)
            .FirstOrDefaultAsync(r => r.Id == id);

    private async Task<bool> CanAccessAsync(EquipmentRental rental)
    {
        if (User.IsInRole(AppRoles.Staff) || User.IsInRole(AppRoles.Administrator))
        {
            return true;
        }

        var user = await _userManager.GetUserAsync(User);
        return user is not null && rental.MemberId == user.Id;
    }

    private async Task<EquipmentRentalFormViewModel> BuildFormAsync(string memberId, EquipmentRentalFormViewModel model)
    {
        model.EquipmentOptions = await _db.InventoryItems
            .Include(i => i.Facility)
            .Where(i => i.Condition != InventoryCondition.Retired && i.Facility!.IsActive)
            .OrderBy(i => i.Facility!.Name).ThenBy(i => i.Name)
            .Select(i => new SelectListItem
            {
                Value = i.Id.ToString(),
                Text = $"{i.Facility!.Name} · {i.Name} (qty {i.Quantity})",
                Selected = i.Id == model.InventoryItemId
            })
            .ToListAsync();

        model.BookingOptions = await _db.Bookings
            .Include(b => b.Facility)
            .Where(b => b.MemberId == memberId &&
                        b.Status != BookingStatus.Cancelled &&
                        b.StartUtc >= DateTime.UtcNow.Date.AddDays(-1))
            .OrderBy(b => b.StartUtc)
            .Select(b => new SelectListItem
            {
                Value = b.Id.ToString(),
                Text = $"#{b.Id} · {b.Facility!.Name} · {b.StartUtc.ToLocalTime():dd MMM HH:mm}",
                Selected = b.Id == model.BookingId
            })
            .ToListAsync();

        model.BookingOptions.Insert(0, new SelectListItem { Value = "", Text = "(No linked booking)" });
        return model;
    }

    private static bool TryParseWindow(
        EquipmentRentalFormViewModel model,
        out DateTime startUtc,
        out DateTime dueUtc,
        out string? error)
    {
        startUtc = default;
        dueUtc = default;
        error = null;

        if (!TimeSpan.TryParse(model.StartTime, out var startTs) || !TimeSpan.TryParse(model.DueTime, out var dueTs))
        {
            error = "Enter valid start and due times.";
            return false;
        }

        var startLocal = model.StartDate.Date.Add(startTs);
        var dueLocal = model.DueDate.Date.Add(dueTs);
        startUtc = DateTime.SpecifyKind(startLocal, DateTimeKind.Local).ToUniversalTime();
        dueUtc = DateTime.SpecifyKind(dueLocal, DateTimeKind.Local).ToUniversalTime();

        if (dueUtc <= startUtc)
        {
            error = "Due time must be after start time.";
            return false;
        }

        return true;
    }
}
