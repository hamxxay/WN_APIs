using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Extensions
{
    /// <summary>
    /// Shared answers of the admin list filters (LST_01_list_filters) on GET api/booking, api/quotation, api/invoice,
    /// api/payment and api/customer, and of their GET .../statuses choices: a wrong filter value = 400, the database
    /// update not run yet = 503 "Database update needed: run LST_01_list_filters_TEST.sql ...".
    /// </summary>
    public static class ListFilterResults
    {
        public static async Task<IActionResult> RunAsync(Func<Task<IActionResult>> action)
        {
            try
            {
                return await action();
            }
            catch (ListFilterException ex)
            {
                return new BadRequestObjectResult(ApiResponse.Fail(ex.Message));
            }
            catch (ListFiltersDatabaseUpdateMissingException ex)
            {
                return new ObjectResult(ApiResponse.Fail(ex.Message)) { StatusCode = StatusCodes.Status503ServiceUnavailable };
            }
        }

        /// <summary>GET api/{list}/statuses: the Status filter choices [{ value, label }] read from the database.</summary>
        public static Task<IActionResult> StatusOptionsAsync(IListFilterService lists, string list) =>
            RunAsync(async () => new OkObjectResult(ApiResponse.Ok(await lists.GetStatusOptionsAsync(list))));

        /// <summary>
        /// Locations a filtered list covers, for the procedures that take a list of locations (payments, customers):
        /// location-bound staff = the requested one if it is theirs, otherwise all of theirs (none = matches nothing);
        /// everyone else = the requested one, or null (every location).
        /// </summary>
        public static List<int>? ScopedLocationIds(this ClaimsPrincipal user, int? requested)
        {
            var scoped = user.ScopedLocations(requested is > 0 ? requested : null);
            if (scoped.Count == 1 && scoped[0] == null) return null;
            return scoped.Where(x => x.HasValue).Select(x => x!.Value).ToList();
        }
    }
}
