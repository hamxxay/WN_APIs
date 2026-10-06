using System.Data;
using ClosedXML.Excel;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.Reports;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Infrastructure.ExternalServices.Reports
{
    public class SecurityDepositReportService : ISecurityDepositReportService
    {
        private readonly string _connectionString;

        public SecurityDepositReportService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        }

        public async Task<ApiResponse> GetCustomerSummaryAsync(SecurityDepositReportFilterDto filter)
        {
            if (filter.FromDate.HasValue && filter.ToDate.HasValue && filter.FromDate.Value > filter.ToDate.Value)
            {
                return ApiResponse.Fail("FromDate cannot be greater than ToDate.");
            }

            var list = new List<SecurityDepositSummaryDto>();

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var cmd = new SqlCommand("dbo.WN_SecurityDeposit_CustomerReport", connection)
            {
                CommandType = CommandType.StoredProcedure
            };

            cmd.Parameters.Add(new SqlParameter("@CustomerId", SqlDbType.Int) { Value = (object?)filter.CustomerId ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@FromDate", SqlDbType.DateTime2) { Value = (object?)filter.FromDate ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@ToDate", SqlDbType.DateTime2) { Value = (object?)filter.ToDate ?? DBNull.Value });

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new SecurityDepositSummaryDto
                {
                    CustomerId = reader.GetInt32(reader.GetOrdinal("CustomerId")),
                    CustomerCode = reader.GetString(reader.GetOrdinal("CustomerCode")),
                    CustomerName = reader.GetString(reader.GetOrdinal("CustomerName")),
                    Company = reader.IsDBNull(reader.GetOrdinal("Company")) ? null : reader.GetString(reader.GetOrdinal("Company")),
                    DepositCount = reader.GetInt32(reader.GetOrdinal("DepositCount")),
                    TotalReceived = reader.GetDecimal(reader.GetOrdinal("TotalReceived")),
                    TotalReleased = reader.GetDecimal(reader.GetOrdinal("TotalReleased")),
                    TotalForfeited = reader.GetDecimal(reader.GetOrdinal("TotalForfeited")),
                    BalanceHeld = reader.GetDecimal(reader.GetOrdinal("BalanceHeld")),
                    LastHeldOn = reader.IsDBNull(reader.GetOrdinal("LastHeldOn")) ? null : reader.GetDateTime(reader.GetOrdinal("LastHeldOn"))
                });
            }

            return ApiResponse.Ok(list);
        }

        public async Task<ApiResponse> GetCustomerDetailAsync(int customerId, DateTime? fromDate, DateTime? toDate)
        {
            if (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value)
            {
                return ApiResponse.Fail("FromDate cannot be greater than ToDate.");
            }

            var list = new List<SecurityDepositDetailDto>();

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var cmd = new SqlCommand("dbo.WN_SecurityDeposit_CustomerDetail", connection)
            {
                CommandType = CommandType.StoredProcedure
            };

            cmd.Parameters.Add(new SqlParameter("@CustomerId", SqlDbType.Int) { Value = customerId });
            cmd.Parameters.Add(new SqlParameter("@FromDate", SqlDbType.DateTime2) { Value = (object?)fromDate ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@ToDate", SqlDbType.DateTime2) { Value = (object?)toDate ?? DBNull.Value });

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new SecurityDepositDetailDto
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    BookingId = reader.GetInt32(reader.GetOrdinal("BookingId")),
                    RefNo = reader.IsDBNull(reader.GetOrdinal("RefNo")) ? null : reader.GetString(reader.GetOrdinal("RefNo")),
                    Amount = reader.GetDecimal(reader.GetOrdinal("Amount")),
                    State = reader.GetString(reader.GetOrdinal("State")),
                    HeldOn = reader.GetDateTime(reader.GetOrdinal("HeldOn")),
                    ReleasedOn = reader.IsDBNull(reader.GetOrdinal("ReleasedOn")) ? null : reader.GetDateTime(reader.GetOrdinal("ReleasedOn")),
                    ForfeitedOn = reader.IsDBNull(reader.GetOrdinal("ForfeitedOn")) ? null : reader.GetDateTime(reader.GetOrdinal("ForfeitedOn")),
                    ForfeitReason = reader.IsDBNull(reader.GetOrdinal("ForfeitReason")) ? null : reader.GetString(reader.GetOrdinal("ForfeitReason")),
                    Notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader.GetString(reader.GetOrdinal("Notes"))
                });
            }

            return ApiResponse.Ok(list);
        }

        public async Task<ApiResponse> GetCustomerLookupAsync()
        {
            var list = new List<CustomerReportDropdownDto>();

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                SELECT Id, Code, FirstName, LastName, Company 
                FROM dbo.WN_Customers WITH (NOLOCK)
                WHERE IsActive = 1
                ORDER BY FirstName, LastName;";

            await using var cmd = new SqlCommand(sql, connection);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var id = reader.GetInt32(0);
                var code = reader.GetString(1);
                var firstName = reader.GetString(2);
                var lastName = reader.IsDBNull(3) ? "" : reader.GetString(3);
                var company = reader.IsDBNull(4) ? "" : reader.GetString(4);

                var name = $"{firstName} {lastName}".Trim();
                var display = string.IsNullOrWhiteSpace(company)
                    ? $"{code} - {name}"
                    : $"{code} - {name} ({company})";

                list.Add(new CustomerReportDropdownDto
                {
                    Id = id,
                    Code = code,
                    DisplayText = display
                });
            }

            return ApiResponse.Ok(list);
        }

        public async Task<byte[]> ExportExcelAsync(SecurityDepositReportFilterDto filter)
        {
            var response = await GetCustomerSummaryAsync(filter);
            var records = response.Data as List<SecurityDepositSummaryDto> ?? new List<SecurityDepositSummaryDto>();

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Security Deposits");

            // Title
            worksheet.Cell("A1").Value = "WorkNest - Security Deposit Customer Report";
            worksheet.Cell("A1").Style.Font.Bold = true;
            worksheet.Cell("A1").Style.Font.FontSize = 16;
            worksheet.Cell("A1").Style.Font.FontColor = XLColor.FromHtml("#1E293B");
            worksheet.Range("A1:I1").Merge();

            // Metadata
            var filterText = $"Generated On: {WorkNest.Application.Services.BusinessClock.Default.Now:yyyy-MM-dd HH:mm} | Filter: " +
                $"{(filter.FromDate.HasValue ? filter.FromDate.Value.ToString("yyyy-MM-dd") : "All")} to " +
                $"{(filter.ToDate.HasValue ? filter.ToDate.Value.ToString("yyyy-MM-dd") : "All")}";
            worksheet.Cell("A2").Value = filterText;
            worksheet.Cell("A2").Style.Font.Italic = true;
            worksheet.Cell("A2").Style.Font.FontColor = XLColor.FromHtml("#64748B");
            worksheet.Range("A2:I2").Merge();

            // Headers
            var headers = new[]
            {
                "Customer Code", "Customer Name", "Company", "Deposit Count",
                "Total Received", "Total Released", "Total Forfeited", "Balance Held", "Last Held Date"
            };

            int startRow = 4;
            for (int col = 0; col < headers.Length; col++)
            {
                var cell = worksheet.Cell(startRow, col + 1);
                cell.Value = headers[col];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0F172A");
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            // Data Rows
            int currentRow = startRow + 1;
            foreach (var item in records)
            {
                worksheet.Cell(currentRow, 1).Value = item.CustomerCode;
                worksheet.Cell(currentRow, 2).Value = item.CustomerName;
                worksheet.Cell(currentRow, 3).Value = item.Company ?? "-";
                worksheet.Cell(currentRow, 4).Value = item.DepositCount;
                worksheet.Cell(currentRow, 5).Value = item.TotalReceived;
                worksheet.Cell(currentRow, 6).Value = item.TotalReleased;
                worksheet.Cell(currentRow, 7).Value = item.TotalForfeited;
                worksheet.Cell(currentRow, 8).Value = item.BalanceHeld;
                worksheet.Cell(currentRow, 9).Value = item.LastHeldOn.HasValue ? item.LastHeldOn.Value.ToString("yyyy-MM-dd") : "-";

                worksheet.Cell(currentRow, 4).Style.NumberFormat.Format = "#,##0";
                worksheet.Cell(currentRow, 5).Style.NumberFormat.Format = "#,##0.00";
                worksheet.Cell(currentRow, 6).Style.NumberFormat.Format = "#,##0.00";
                worksheet.Cell(currentRow, 7).Style.NumberFormat.Format = "#,##0.00";
                worksheet.Cell(currentRow, 8).Style.NumberFormat.Format = "#,##0.00";

                if ((currentRow - startRow) % 2 == 0)
                {
                    worksheet.Range(currentRow, 1, currentRow, 9).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
                }

                currentRow++;
            }

            // Totals row
            if (records.Any())
            {
                worksheet.Cell(currentRow, 1).Value = "TOTAL";
                worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
                worksheet.Range(currentRow, 1, currentRow, 3).Merge();

                worksheet.Cell(currentRow, 4).FormulaA1 = $"=SUM(D{startRow + 1}:D{currentRow - 1})";
                worksheet.Cell(currentRow, 5).FormulaA1 = $"=SUM(E{startRow + 1}:E{currentRow - 1})";
                worksheet.Cell(currentRow, 6).FormulaA1 = $"=SUM(F{startRow + 1}:F{currentRow - 1})";
                worksheet.Cell(currentRow, 7).FormulaA1 = $"=SUM(G{startRow + 1}:G{currentRow - 1})";
                worksheet.Cell(currentRow, 8).FormulaA1 = $"=SUM(H{startRow + 1}:H{currentRow - 1})";

                var summaryRange = worksheet.Range(currentRow, 1, currentRow, 9);
                summaryRange.Style.Font.Bold = true;
                summaryRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#E2E8F0");
                summaryRange.Style.Border.TopBorder = XLBorderStyleValues.Thin;
                summaryRange.Style.Border.BottomBorder = XLBorderStyleValues.Double;

                worksheet.Cell(currentRow, 4).Style.NumberFormat.Format = "#,##0";
                worksheet.Cell(currentRow, 5).Style.NumberFormat.Format = "#,##0.00";
                worksheet.Cell(currentRow, 6).Style.NumberFormat.Format = "#,##0.00";
                worksheet.Cell(currentRow, 7).Style.NumberFormat.Format = "#,##0.00";
                worksheet.Cell(currentRow, 8).Style.NumberFormat.Format = "#,##0.00";
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        public async Task<byte[]> ExportCustomerDetailExcelAsync(int customerId, DateTime? fromDate, DateTime? toDate)
        {
            var response = await GetCustomerDetailAsync(customerId, fromDate, toDate);
            var records = response.Data as List<SecurityDepositDetailDto> ?? new List<SecurityDepositDetailDto>();

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Customer Deposit History");

            // Title
            worksheet.Cell("A1").Value = $"WorkNest - Customer Deposit History";
            worksheet.Cell("A1").Style.Font.Bold = true;
            worksheet.Cell("A1").Style.Font.FontSize = 16;
            worksheet.Cell("A1").Style.Font.FontColor = XLColor.FromHtml("#1E293B");
            worksheet.Range("A1:J1").Merge();

            // Metadata
            var filterText = $"Customer ID: #{customerId} | Generated On: {WorkNest.Application.Services.BusinessClock.Default.Now:yyyy-MM-dd HH:mm} | Filter Period: " +
                $"{(fromDate.HasValue ? fromDate.Value.ToString("yyyy-MM-dd") : "All")} to " +
                $"{(toDate.HasValue ? toDate.Value.ToString("yyyy-MM-dd") : "All")}";
            worksheet.Cell("A2").Value = filterText;
            worksheet.Cell("A2").Style.Font.Italic = true;
            worksheet.Cell("A2").Style.Font.FontColor = XLColor.FromHtml("#64748B");
            worksheet.Range("A2:J2").Merge();

            // Headers
            var headers = new[]
            {
                "Deposit ID", "Booking ID", "Invoice Ref No", "Amount", "State",
                "Held Date", "Released Date", "Forfeited Date", "Forfeit Reason", "Notes"
            };

            int startRow = 4;
            for (int col = 0; col < headers.Length; col++)
            {
                var cell = worksheet.Cell(startRow, col + 1);
                cell.Value = headers[col];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0F172A");
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            int currentRow = startRow + 1;
            foreach (var item in records)
            {
                worksheet.Cell(currentRow, 1).Value = item.Id;
                worksheet.Cell(currentRow, 2).Value = item.BookingId;
                worksheet.Cell(currentRow, 3).Value = item.RefNo ?? "-";
                worksheet.Cell(currentRow, 4).Value = item.Amount;
                worksheet.Cell(currentRow, 5).Value = item.State;
                worksheet.Cell(currentRow, 6).Value = item.HeldOn.ToString("yyyy-MM-dd");
                worksheet.Cell(currentRow, 7).Value = item.ReleasedOn.HasValue ? item.ReleasedOn.Value.ToString("yyyy-MM-dd") : "-";
                worksheet.Cell(currentRow, 8).Value = item.ForfeitedOn.HasValue ? item.ForfeitedOn.Value.ToString("yyyy-MM-dd") : "-";
                worksheet.Cell(currentRow, 9).Value = item.ForfeitReason ?? "-";
                worksheet.Cell(currentRow, 10).Value = item.Notes ?? "-";

                worksheet.Cell(currentRow, 4).Style.NumberFormat.Format = "#,##0.00";

                if ((currentRow - startRow) % 2 == 0)
                {
                    worksheet.Range(currentRow, 1, currentRow, 10).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
                }

                currentRow++;
            }

            if (records.Any())
            {
                worksheet.Cell(currentRow, 1).Value = "TOTAL";
                worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
                worksheet.Range(currentRow, 1, currentRow, 3).Merge();

                worksheet.Cell(currentRow, 4).FormulaA1 = $"=SUM(D{startRow + 1}:D{currentRow - 1})";
                worksheet.Cell(currentRow, 4).Style.NumberFormat.Format = "#,##0.00";

                var summaryRange = worksheet.Range(currentRow, 1, currentRow, 10);
                summaryRange.Style.Font.Bold = true;
                summaryRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#E2E8F0");
                summaryRange.Style.Border.TopBorder = XLBorderStyleValues.Thin;
                summaryRange.Style.Border.BottomBorder = XLBorderStyleValues.Double;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }
    }
}
