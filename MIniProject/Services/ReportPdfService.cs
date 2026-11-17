using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MIniProject.Services;

public class ReportPdfService
{
    static ReportPdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] BuildMonthlyReport(DashboardStats stats, string clubName)
    {
        var generatedAt = DateTime.Now.ToString("dd MMM yyyy HH:mm");
        var monthLabel = DateTime.UtcNow.ToString("MMMM yyyy");
        var maxPeak = Math.Max(1, stats.PeakHours.DefaultIfEmpty().Max(x => x?.Count ?? 0));

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken3));

                page.Header().Column(col =>
                {
                    col.Item().Text(clubName).SemiBold().FontSize(16).FontColor(Colors.Green.Darken3);
                    col.Item().Text("Operational dashboard & financial analytics").FontSize(12).FontColor(Colors.Grey.Darken2);
                    col.Item().Text($"Report period: {monthLabel} · Generated {generatedAt}").FontSize(9).FontColor(Colors.Grey.Medium);
                    col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Green.Darken2);
                });

                page.Content().PaddingVertical(16).Column(col =>
                {
                    col.Spacing(14);

                    col.Item().Text("Key metrics").SemiBold().FontSize(12).FontColor(Colors.Green.Darken3);
                    col.Item().Row(row =>
                    {
                        MetricCard(row, "Revenue this month", $"MYR {stats.RevenueThisMonth:N2}");
                        MetricCard(row, "Bookings today", stats.BookingsToday.ToString("N0"));
                        MetricCard(row, "Active members", stats.ActiveMembers.ToString("N0"));
                        MetricCard(row, "Pending approvals", stats.PendingApprovals.ToString("N0"));
                    });

                    col.Item().Row(row =>
                    {
                        MetricCard(row, "Confirmed bookings", stats.TotalBookings.ToString("N0"));
                        MetricCard(row, "Cancellation ratio", $"{stats.CancellationRatio:N1}%");
                        MetricCard(row, "Facilities listed", stats.TotalFacilities.ToString("N0"));
                        MetricCard(row, "Registered members", stats.RegisteredMembers.ToString("N0"));
                    });

                    col.Item().Text("Facility demand & revenue").SemiBold().FontSize(12).FontColor(Colors.Green.Darken3);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1.4f);
                        });
                        table.Header(h =>
                        {
                            h.Cell().Element(HeaderCell).Text("Facility");
                            h.Cell().Element(HeaderCell).AlignRight().Text("Bookings");
                            h.Cell().Element(HeaderCell).AlignRight().Text("Revenue");
                        });
                        foreach (var item in stats.FacilityUsage)
                        {
                            table.Cell().Element(BodyCell).Text(item.FacilityName);
                            table.Cell().Element(BodyCell).AlignRight().Text(item.BookingCount.ToString());
                            table.Cell().Element(BodyCell).AlignRight().Text($"MYR {item.Revenue:N2}");
                        }
                    });

                    col.Item().Text("Peak booking hours (UTC)").SemiBold().FontSize(12).FontColor(Colors.Green.Darken3);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                            c.RelativeColumn(3);
                        });
                        table.Header(h =>
                        {
                            h.Cell().Element(HeaderCell).Text("Hour");
                            h.Cell().Element(HeaderCell).AlignRight().Text("Count");
                            h.Cell().Element(HeaderCell).Text("Relative demand");
                        });
                        foreach (var item in stats.PeakHours.OrderByDescending(x => x.Count).Take(8))
                        {
                            var width = (int)Math.Round(100.0 * item.Count / maxPeak);
                            table.Cell().Element(BodyCell).Text($"{item.Hour:00}:00");
                            table.Cell().Element(BodyCell).AlignRight().Text(item.Count.ToString());
                            table.Cell().Element(BodyCell).Text($"{width}% of peak");
                        }
                    });

                    if (stats.MembershipTiers.Count > 0)
                    {
                        col.Item().Text("Membership tier breakdown").SemiBold().FontSize(12).FontColor(Colors.Green.Darken3);
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(2.4f);
                                c.RelativeColumn(1);
                                c.RelativeColumn(1.4f);
                                c.RelativeColumn(1.2f);
                            });
                            table.Header(h =>
                            {
                                h.Cell().Element(HeaderCell).Text("Plan");
                                h.Cell().Element(HeaderCell).AlignRight().Text("Members");
                                h.Cell().Element(HeaderCell).AlignRight().Text("Advance days");
                                h.Cell().Element(HeaderCell).AlignRight().Text("Discount");
                            });
                            foreach (var tier in stats.MembershipTiers)
                            {
                                table.Cell().Element(BodyCell).Text(tier.PlanName);
                                table.Cell().Element(BodyCell).AlignRight().Text(tier.MemberCount.ToString());
                                table.Cell().Element(BodyCell).AlignRight().Text(tier.AdvanceBookingDays.ToString());
                                table.Cell().Element(BodyCell).AlignRight().Text($"{tier.DiscountPercent:N0}%");
                            }
                        });
                    }
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Kota Permai Golf & Country Club · confidential management report · page ");
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        }).GeneratePdf();

        static void MetricCard(RowDescriptor row, string label, string value)
        {
            row.RelativeItem().Padding(4).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
            {
                c.Item().Text(label).FontSize(8).FontColor(Colors.Grey.Darken1);
                c.Item().Text(value).SemiBold().FontSize(11).FontColor(Colors.Green.Darken3);
            });
        }

        static IContainer HeaderCell(IContainer container) =>
            container.DefaultTextStyle(x => x.SemiBold().FontSize(9).FontColor(Colors.Green.Darken3))
                .PaddingVertical(4)
                .BorderBottom(1)
                .BorderColor(Colors.Grey.Lighten1);

        static IContainer BodyCell(IContainer container) =>
            container.PaddingVertical(3).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3);
    }
}
