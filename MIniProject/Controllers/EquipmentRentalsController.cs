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

        if (User.IsInRole(AppRoles.Member))
        {
            ViewBag.CanCreateRental = await HasLinkableBookingAsync(user.Id);
        }

        var rentals = await query.OrderByDescending(r => r.CreatedAt).Take(100).ToListAsync();
        await SyncRentalStatusesAsync(rentals);
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

        await SyncRentalStatusesAsync([rental]);
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

        if (!await HasLinkableBookingAsync(user.Id))
        {
            TempData["Error"] = NoEligibleBookingMessage;
            return RedirectToAction(nameof(Index));
        }

        return View(await BuildFormAsync(
            user.Id,
            new EquipmentRentalFormViewModel
            {
                Lines = [new EquipmentRentalLineViewModel()]
            },
            requireBookingLink: true));
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

        // Create uses Lines; ignore single-item edit fields that are not posted.
        ModelState.Remove(nameof(model.InventoryItemId));
        ModelState.Remove(nameof(model.Quantity));

        // Members may only create rentals linked to an eligible future/current booking.
        var linkableBookings = await LoadLinkableBookingsAsync(user.Id);
        if (linkableBookings.Count == 0)
        {
            TempData["Error"] = NoEligibleBookingMessage;
            return RedirectToAction(nameof(Index));
        }

        // When the member has exactly one linkable booking, always bind it (chooser is hidden).
        if (linkableBookings.Count == 1)
        {
            model.BookingId = linkableBookings[0].Id;
        }

        model.Lines ??= [];
        // Drop blank template rows the client may leave after remove.
        model.Lines = model.Lines
            .Where(l => l.InventoryItemId > 0 || l.Quantity > 0)
            .ToList();

        if (model.Lines.Count == 0)
        {
            ModelState.AddModelError(nameof(model.Lines), "Add at least one equipment line.");
            model.Lines = [new EquipmentRentalLineViewModel()];
        }

        if (model.BookingId is not int bookingId)
        {
            ModelState.AddModelError(nameof(model.BookingId),
                "Select a facility booking to link. Equipment rentals cannot be created without a booking.");
            return View(await BuildFormAsync(user.Id, model, requireBookingLink: true));
        }

        var linkedBooking = linkableBookings.FirstOrDefault(b => b.Id == bookingId);
        if (linkedBooking is null)
        {
            ModelState.AddModelError(nameof(model.BookingId),
                "Linked booking was not found or is no longer eligible. Choose a current facility booking.");
            return View(await BuildFormAsync(user.Id, model, requireBookingLink: true));
        }

        // Rental window is locked to the linked booking schedule.
        ApplyBookingSchedule(model, linkedBooking);

        if (!TryParseWindow(model, out var startUtc, out var dueUtc, out var windowError))
        {
            ModelState.AddModelError(string.Empty, windowError!);
        }

        if (!ModelState.IsValid)
        {
            return View(await BuildFormAsync(user.Id, model, requireBookingLink: true));
        }

        var requestedIds = model.Lines.Select(l => l.InventoryItemId).Where(id => id > 0).Distinct().ToList();
        var items = await _db.InventoryItems
            .Include(i => i.Facility)
            .Where(i => requestedIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id);

        // Track remaining stock so duplicate lines for the same item share one pool.
        var remainingStock = items.ToDictionary(kv => kv.Key, kv => kv.Value.Quantity);

        for (var i = 0; i < model.Lines.Count; i++)
        {
            var line = model.Lines[i];
            var prefix = $"{nameof(model.Lines)}[{i}]";

            if (line.InventoryItemId <= 0)
            {
                ModelState.AddModelError($"{prefix}.{nameof(line.InventoryItemId)}", "Select equipment.");
                continue;
            }

            if (line.Quantity < 1 || line.Quantity > 50)
            {
                ModelState.AddModelError($"{prefix}.{nameof(line.Quantity)}", "Quantity must be between 1 and 50.");
                continue;
            }

            if (!items.TryGetValue(line.InventoryItemId, out var item) ||
                item.Condition == InventoryCondition.Retired)
            {
                ModelState.AddModelError($"{prefix}.{nameof(line.InventoryItemId)}",
                    "Selected equipment is not available.");
                continue;
            }

            if (item.FacilityId != linkedBooking.FacilityId)
            {
                ModelState.AddModelError($"{prefix}.{nameof(line.InventoryItemId)}",
                    $"Equipment must belong to {linkedBooking.Facility?.Name ?? "the linked booking's facility"}.");
                continue;
            }

            var available = remainingStock[item.Id];
            if (line.Quantity > available)
            {
                ModelState.AddModelError($"{prefix}.{nameof(line.Quantity)}",
                    $"Only {available} unit(s) available for {item.Name}.");
                continue;
            }

            remainingStock[item.Id] = available - line.Quantity;
        }

        if (!ModelState.IsValid)
        {
            return View(await BuildFormAsync(user.Id, model, requireBookingLink: true));
        }

        var createdNames = new List<string>();
        decimal totalFee = 0m;
        var guidance = BuildEquipmentGuidance(
            linkedBooking.Facility?.Name ?? "facility",
            (linkedBooking.Facility?.InventoryItems ?? [])
                .Where(i => i.Condition != InventoryCondition.Retired)
                .Select(i => i.Name));

        foreach (var line in model.Lines)
        {
            var item = items[line.InventoryItemId];
            var fee = EquipmentPricing.CalculateFee(line.Quantity, startUtc, dueUtc);
            totalFee += fee;
            item.Quantity -= line.Quantity;

            _db.EquipmentRentals.Add(new EquipmentRental
            {
                MemberId = user.Id,
                InventoryItemId = item.Id,
                BookingId = linkedBooking.Id,
                Quantity = line.Quantity,
                StartUtc = startUtc,
                DueUtc = dueUtc,
                Status = EquipmentRentalStatus.Active,
                Fee = fee,
                Notes = guidance
            });
            createdNames.Add($"{item.Name} × {line.Quantity}");
        }

        // Equipment is always full price; roll into unpaid booking/invoice totals.
        await ApplyEquipmentFeeToBookingAsync(linkedBooking.Id, totalFee);

        await _db.SaveChangesAsync();

        TempData["Success"] = createdNames.Count == 1
            ? $"Rental created for {createdNames[0]} (MYR {totalFee:N2})."
            : $"Created {createdNames.Count} rentals: {string.Join(", ", createdNames)} (MYR {totalFee:N2}).";
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

        await SyncRentalStatusesAsync([rental]);

        if (rental.Status is EquipmentRentalStatus.Returned
            or EquipmentRentalStatus.Cancelled
            or EquipmentRentalStatus.Inactive)
        {
            TempData["Error"] = "Returned or inactive rentals cannot be edited.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (await IsLinkedBookingCancelledAsync(rental))
        {
            await ForceCancelRentalAsync(rental);
            TempData["Error"] = "This rental is linked to a cancelled booking and is now inactive.";
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

        await SyncRentalStatusesAsync([rental]);

        if (rental.Status is EquipmentRentalStatus.Returned
            or EquipmentRentalStatus.Cancelled
            or EquipmentRentalStatus.Inactive)
        {
            TempData["Error"] = "Returned or inactive rentals cannot be edited.";
            return RedirectToAction(nameof(Details), new { id = rental.Id });
        }

        if (await IsLinkedBookingCancelledAsync(rental))
        {
            await ForceCancelRentalAsync(rental);
            TempData["Error"] = "This rental is linked to a cancelled booking and is now inactive.";
            return RedirectToAction(nameof(Details), new { id = rental.Id });
        }

        if (model.InventoryItemId <= 0)
        {
            ModelState.AddModelError(nameof(model.InventoryItemId), "Select equipment.");
        }

        if (!TryParseWindow(model, out var startUtc, out var dueUtc, out var windowError))
        {
            ModelState.AddModelError(string.Empty, windowError!);
        }

        if (!ModelState.IsValid)
        {
            return View(await BuildFormAsync(rental.MemberId, model));
        }

        // Restore previous stock, then apply new quantity against current item
        // (stock is reserved while Active / held while Rented or NotReturned).
        // Invalid/Inactive/Cancelled already restored stock when they transitioned.
        if (rental.InventoryItem is not null &&
            rental.Status is EquipmentRentalStatus.Active
                or EquipmentRentalStatus.Rented
                or EquipmentRentalStatus.NotReturned)
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

        // Cannot reactivate or keep Active when linking to a cancelled booking.
        if (model.BookingId is int linkBookingId)
        {
            var linked = await _db.Bookings.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == linkBookingId && b.MemberId == rental.MemberId);
            if (linked is null)
            {
                ModelState.AddModelError(nameof(model.BookingId), "Linked booking was not found.");
                return View(await BuildFormAsync(rental.MemberId, model));
            }

            if (linked.Status == BookingStatus.Cancelled)
            {
                ModelState.AddModelError(nameof(model.BookingId), "Cannot link a rental to a cancelled booking.");
                return View(await BuildFormAsync(rental.MemberId, model));
            }
        }

        item.Quantity -= model.Quantity;
        var previousFee = rental.Fee;
        var previousBookingId = rental.BookingId;
        rental.InventoryItemId = item.Id;
        rental.Quantity = model.Quantity;
        rental.StartUtc = startUtc;
        rental.DueUtc = dueUtc;
        rental.BookingId = model.BookingId;
        rental.Notes = model.Notes;
        rental.Fee = EquipmentPricing.CalculateFee(model.Quantity, startUtc, dueUtc);

        if (previousBookingId is int oldBookingId && oldBookingId != model.BookingId)
        {
            await ApplyEquipmentFeeToBookingAsync(oldBookingId, -previousFee);
        }
        else if (previousBookingId is int sameBookingId)
        {
            await ApplyEquipmentFeeToBookingAsync(sameBookingId, rental.Fee - previousFee);
        }

        if (model.BookingId is int newBookingId && newBookingId != previousBookingId)
        {
            await ApplyEquipmentFeeToBookingAsync(newBookingId, rental.Fee);
        }
        if (User.IsInRole(AppRoles.Staff) || User.IsInRole(AppRoles.Administrator))
        {
            // Staff cannot mark Cancelled-booking rentals Active/Rented via status override.
            if ((model.Status is EquipmentRentalStatus.Active or EquipmentRentalStatus.Rented) &&
                await IsLinkedBookingCancelledAsync(rental))
            {
                ModelState.AddModelError(nameof(model.Status), "Rentals linked to cancelled bookings cannot be active or rented.");
                return View(await BuildFormAsync(rental.MemberId, model));
            }

            if (model.Status == EquipmentRentalStatus.Rented && rental.PickedUpAt is null)
            {
                rental.PickedUpAt = DateTime.UtcNow;
            }

            if (model.Status == EquipmentRentalStatus.Returned && rental.ReturnedAt is null)
            {
                rental.ReturnedAt = DateTime.UtcNow;
            }

            rental.Status = model.Status;
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = "Rental updated.";
        return RedirectToAction(nameof(Details), new { id = rental.Id });
    }

    [Authorize(Roles = AppRoles.Staff + "," + AppRoles.Administrator)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkPickedUp(int id)
    {
        var rental = await _db.EquipmentRentals
            .Include(r => r.InventoryItem)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (rental is null)
        {
            return NotFound();
        }

        await SyncRentalStatusesAsync([rental]);

        if (await IsLinkedBookingCancelledAsync(rental))
        {
            await ForceCancelRentalAsync(rental);
            TempData["Error"] = "This rental is linked to a cancelled booking and is now inactive.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (!EquipmentRentalLifecycle.CanMarkPickedUp(rental.Status))
        {
            TempData["Error"] = "Only awaiting-pickup rentals can be marked as taken.";
            return RedirectToAction(nameof(Details), new { id });
        }

        EquipmentRentalLifecycle.MarkPickedUp(rental, DateTime.UtcNow);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Equipment marked as taken by the member (Rented).";
        return RedirectToAction(nameof(Details), new { id });
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

        await SyncRentalStatusesAsync([rental]);

        if (await IsLinkedBookingCancelledAsync(rental))
        {
            await ForceCancelRentalAsync(rental);
            TempData["Error"] = "This rental is linked to a cancelled booking and is now inactive.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (!EquipmentRentalLifecycle.CanMarkReturned(rental.Status))
        {
            TempData["Error"] = "Only picked-up equipment (Rented) can be marked as returned. Staff must confirm pickup first.";
            return RedirectToAction(nameof(Details), new { id });
        }

        EquipmentRentalLifecycle.MarkReturned(rental, DateTime.UtcNow);
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

        await SyncRentalStatusesAsync([rental]);

        // Members may delete only invalid/cancelled/inactive records; staff may also delete returned.
        var deletable = rental.Status is EquipmentRentalStatus.Invalid
                            or EquipmentRentalStatus.Cancelled
                            or EquipmentRentalStatus.Inactive
                        || (isStaff && rental.Status == EquipmentRentalStatus.Returned);
        if (!deletable)
        {
            // Mark not-returned overdue rentals as Invalid then allow cleanup, or block.
            if (rental.Status == EquipmentRentalStatus.NotReturned && isStaff)
            {
                EquipmentRentalLifecycle.RestoreStock(rental);
                rental.Status = EquipmentRentalStatus.Invalid;
                await _db.SaveChangesAsync();
            }

            if (rental.Status is not (EquipmentRentalStatus.Invalid
                    or EquipmentRentalStatus.Cancelled
                    or EquipmentRentalStatus.Inactive)
                && !(isStaff && rental.Status == EquipmentRentalStatus.Returned))
            {
                TempData["Error"] = "Only invalid or inactive rental records can be deleted.";
                return RedirectToAction(nameof(Details), new { id });
            }
        }

        if ((rental.Status is EquipmentRentalStatus.Active
                or EquipmentRentalStatus.Rented
                or EquipmentRentalStatus.NotReturned)
            && rental.InventoryItem is not null)
        {
            EquipmentRentalLifecycle.RestoreStock(rental);
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

        await SyncRentalStatusesAsync([rental]);

        if (rental.Status is EquipmentRentalStatus.Active
            or EquipmentRentalStatus.Rented
            or EquipmentRentalStatus.NotReturned)
        {
            EquipmentRentalLifecycle.RestoreStock(rental);
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

    private async Task<bool> IsLinkedBookingCancelledAsync(EquipmentRental rental)
    {
        if (rental.BookingId is null)
        {
            return false;
        }

        if (rental.Booking is not null)
        {
            return rental.Booking.Status == BookingStatus.Cancelled;
        }

        return await _db.Bookings.AnyAsync(b =>
            b.Id == rental.BookingId && b.Status == BookingStatus.Cancelled);
    }

    private async Task ForceCancelRentalAsync(EquipmentRental rental)
    {
        // Only release reserved / not-yet-taken stock. Out-on-loan items stay tracked.
        if (rental.Status != EquipmentRentalStatus.Active)
        {
            return;
        }

        if (rental.InventoryItem is null)
        {
            await _db.Entry(rental).Reference(r => r.InventoryItem).LoadAsync();
        }

        rental.Status = EquipmentRentalStatus.Cancelled;
        EquipmentRentalLifecycle.RestoreStock(rental);
        await _db.SaveChangesAsync();
    }

    private async Task SyncRentalStatusesAsync(IEnumerable<EquipmentRental> rentals)
    {
        var now = DateTime.UtcNow;
        var changed = false;
        foreach (var rental in rentals)
        {
            if (EquipmentRentalLifecycle.ApplyTimeBasedTransitions(rental, now))
            {
                changed = true;
            }
        }

        if (changed)
        {
            await _db.SaveChangesAsync();
        }
    }

    private const string NoEligibleBookingMessage =
        "You are not allowed to have any equipment rental.";

    /// <summary>
    /// Eligible linked bookings are non-cancelled / non-completed / non-no-show
    /// slots whose end time has not yet passed (future or currently in progress).
    /// </summary>
    private async Task<bool> HasLinkableBookingAsync(string memberId) =>
        await _db.Bookings.AnyAsync(b =>
            b.MemberId == memberId &&
            b.Status != BookingStatus.Cancelled &&
            b.Status != BookingStatus.Completed &&
            b.Status != BookingStatus.NoShow &&
            b.EndUtc >= DateTime.UtcNow);

    private async Task<List<Booking>> LoadLinkableBookingsAsync(string memberId) =>
        await _db.Bookings
            .Include(b => b.Facility)!.ThenInclude(f => f!.InventoryItems)
            .Where(b => b.MemberId == memberId &&
                        b.Status != BookingStatus.Cancelled &&
                        b.Status != BookingStatus.Completed &&
                        b.Status != BookingStatus.NoShow &&
                        b.EndUtc >= DateTime.UtcNow)
            .OrderBy(b => b.StartUtc)
            .ToListAsync();

    private async Task<EquipmentRentalFormViewModel> BuildFormAsync(
        string memberId,
        EquipmentRentalFormViewModel model,
        bool requireBookingLink = false)
    {
        var inventory = await _db.InventoryItems
            .Include(i => i.Facility)
            .Where(i => i.Condition != InventoryCondition.Retired && i.Facility!.IsActive)
            .OrderBy(i => i.Facility!.Name).ThenBy(i => i.Name)
            .ToListAsync();

        if (requireBookingLink && (model.Lines is null || model.Lines.Count == 0))
        {
            model.Lines = [new EquipmentRentalLineViewModel()];
        }

        model.EquipmentCatalog = inventory.Select(i => new EquipmentCatalogItem
        {
            Id = i.Id,
            FacilityId = i.FacilityId,
            FacilityName = i.Facility!.Name,
            Name = i.Name,
            Quantity = i.Quantity
        }).ToList();

        var bookings = await LoadLinkableBookingsAsync(memberId);

        model.LinkedBookingSchedules = bookings.Select(b =>
        {
            var startLocal = b.StartUtc.ToLocalTime();
            var endLocal = b.EndUtc.ToLocalTime();
            return new LinkedBookingSchedule
            {
                Id = b.Id,
                FacilityId = b.FacilityId,
                FacilityName = b.Facility?.Name ?? string.Empty,
                StartDate = startLocal.ToString("yyyy-MM-dd"),
                StartTime = startLocal.ToString("HH:mm"),
                DueDate = endLocal.ToString("yyyy-MM-dd"),
                DueTime = endLocal.ToString("HH:mm"),
                EquipmentNames = b.Facility?.InventoryItems
                    .Where(i => i.Condition != InventoryCondition.Retired)
                    .OrderBy(i => i.Name)
                    .Select(i => i.Name)
                    .ToList() ?? []
            };
        }).ToList();

        model.LinkableBookingCount = bookings.Count;
        model.RequiresLinkedBooking = requireBookingLink;

        // Sole linkable booking: auto-select and skip the chooser in the view.
        if (bookings.Count == 1)
        {
            var sole = bookings[0];
            model.BookingId = sole.Id;
            model.SoleLinkedBookingLabel =
                $"#{sole.Id} · {sole.Facility!.Name} · {sole.StartUtc.ToLocalTime():dd MMM HH:mm}";
        }
        else
        {
            model.SoleLinkedBookingLabel = null;
        }

        model.BookingOptions = bookings.Select(b => new SelectListItem
        {
            Value = b.Id.ToString(),
            Text = $"#{b.Id} · {b.Facility!.Name} · {b.StartUtc.ToLocalTime():dd MMM HH:mm}",
            Selected = b.Id == model.BookingId
        }).ToList();

        if (!requireBookingLink)
        {
            model.BookingOptions.Insert(0, new SelectListItem
            {
                Value = "",
                Text = "(No linked booking)",
                Selected = model.BookingId is null
            });
        }
        else if (model.BookingId is null && bookings.Count > 1)
        {
            model.BookingOptions.Insert(0, new SelectListItem
            {
                Value = "",
                Text = "Select a booking…",
                Selected = true
            });
        }

        int? facilityFilter = null;
        if (model.BookingId is int selectedBookingId)
        {
            var schedule = model.LinkedBookingSchedules.FirstOrDefault(s => s.Id == selectedBookingId);
            if (schedule is not null)
            {
                facilityFilter = schedule.FacilityId;
                model.ScheduleLocked = true;
                model.StartDate = DateTime.Parse(schedule.StartDate);
                model.StartTime = schedule.StartTime;
                model.DueDate = DateTime.Parse(schedule.DueDate);
                model.DueTime = schedule.DueTime;
                model.EquipmentGuidance = BuildEquipmentGuidance(schedule.FacilityName, schedule.EquipmentNames);
            }
        }

        if (!model.ScheduleLocked)
        {
            model.EquipmentGuidance = requireBookingLink
                ? "Select a linked booking to lock the rental window and show equipment for that facility."
                : "Select a linked booking to see which equipment you can rent for that facility, or leave unlinked to browse all club inventory.";
        }

        var filtered = facilityFilter is int fid
            ? inventory.Where(i => i.FacilityId == fid)
            : inventory;

        model.EquipmentOptions = filtered.Select(i => new SelectListItem
        {
            Value = i.Id.ToString(),
            Text = facilityFilter is null
                ? $"{i.Facility!.Name} · {i.Name} (qty {i.Quantity})"
                : $"{i.Name} (qty {i.Quantity})",
            Selected = i.Id == model.InventoryItemId
        }).ToList();

        if (facilityFilter is not null && model.EquipmentOptions.Count == 0)
        {
            model.EquipmentOptions.Insert(0, new SelectListItem
            {
                Value = "",
                Text = "(No rentable equipment for this facility)"
            });
        }

        return model;
    }

    /// <summary>
    /// Adds (or subtracts) equipment fees on an unpaid booking/invoice.
    /// Member facility discount is never applied to equipment.
    /// </summary>
    private async Task ApplyEquipmentFeeToBookingAsync(int bookingId, decimal feeDelta)
    {
        if (feeDelta == 0)
        {
            return;
        }

        var booking = await _db.Bookings
            .Include(b => b.Invoice)
            .FirstOrDefaultAsync(b => b.Id == bookingId);
        if (booking is null)
        {
            return;
        }

        if (booking.Status is BookingStatus.Cancelled or BookingStatus.Completed or BookingStatus.NoShow)
        {
            return;
        }

        booking.Amount = Math.Max(0, Math.Round(booking.Amount + feeDelta, 2));

        if (booking.Invoice is not null &&
            booking.Invoice.Status is InvoiceStatus.Unpaid or InvoiceStatus.Overdue)
        {
            booking.Invoice.Amount = booking.Amount;
        }
    }

    private static void ApplyBookingSchedule(EquipmentRentalFormViewModel model, Booking booking)
    {
        var startLocal = booking.StartUtc.ToLocalTime();
        var endLocal = booking.EndUtc.ToLocalTime();
        model.StartDate = startLocal.Date;
        model.StartTime = startLocal.ToString("HH:mm");
        model.DueDate = endLocal.Date;
        model.DueTime = endLocal.ToString("HH:mm");
        model.ScheduleLocked = true;
    }

    private static string BuildEquipmentGuidance(string facilityName, IEnumerable<string> equipmentNames)
    {
        var names = equipmentNames.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
        if (names.Count == 0)
        {
            return $"No equipment inventory is listed for {facilityName}. Ask staff if other gear is available for this booking.";
        }

        return $"For {facilityName}, you can rent: {string.Join(", ", names)}. Only inventory tied to this facility is shown below.";
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
