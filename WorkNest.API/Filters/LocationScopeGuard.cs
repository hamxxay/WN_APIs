using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WorkNest.API.Extensions;
using WorkNest.Common.Responses;

namespace WorkNest.API.Filters
{
    /// <summary>
    /// Action Filter to enforce Location Scope on request payloads and query parameters.
    /// SuperAdmin users bypass location restrictions.
    /// Admin and SalesExecutive users are strictly validated against their LocationId claim:
    /// - If request payload/query specifies a different LocationId, returns 403 Forbidden.
    /// - If request payload LocationId is null/empty, pre-fills with claim LocationId.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class ValidateLocationScopeAttribute : Attribute, IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var user = context.HttpContext.User;
            if (user == null || !user.Identity?.IsAuthenticated == true)
            {
                await next();
                return;
            }

            if (user.IsSuperAdmin())
            {
                await next();
                return;
            }

            if (user.IsLocationBoundRole())
            {
                var claimLocationId = user.GetLocationId();
                if (!claimLocationId.HasValue || claimLocationId.Value <= 0)
                {
                    context.Result = new ObjectResult(ApiResponse.Fail("Access denied. User account is not bound to any location."))
                    {
                        StatusCode = 403
                    };
                    return;
                }

                context.HttpContext.Items["ClaimLocationId"] = claimLocationId.Value;
                // Staff may be assigned to several locations (location_ids claim); any of them is allowed.
                var allowedLocationIds = user.GetLocationIds();

                // Validate or overwrite LocationId across action arguments
                foreach (var argKey in context.ActionArguments.Keys)
                {
                    var argValue = context.ActionArguments[argKey];
                    if (argValue == null) continue;

                    // Case 1: Argument itself is LocationId (e.g., int locationId parameter)
                    if (string.Equals(argKey, "locationId", StringComparison.OrdinalIgnoreCase))
                    {
                        if (argValue is int intVal && intVal > 0 && !allowedLocationIds.Contains(intVal))
                        {
                            context.Result = new ObjectResult(ApiResponse.Fail($"Location mismatch. You aren't assigned to Location {intVal}."))
                            {
                                StatusCode = 403
                            };
                            return;
                        }
                    }

                    // Case 2: Argument is DTO object containing LocationId property
                    var prop = argValue.GetType().GetProperty("LocationId", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (prop != null && prop.CanRead)
                    {
                        var propVal = prop.GetValue(argValue);
                        if (propVal is int intPropVal && intPropVal > 0)
                        {
                            if (!allowedLocationIds.Contains(intPropVal))
                            {
                                context.Result = new ObjectResult(ApiResponse.Fail($"Location mismatch. You aren't assigned to Location {intPropVal}."))
                                {
                                    StatusCode = 403
                                };
                                return;
                            }
                        }
                        else if (prop.CanWrite)
                        {
                            // If property is nullable or 0, pre-fill with claim location ID
                            if (prop.PropertyType == typeof(int?) || prop.PropertyType == typeof(int))
                            {
                                try
                                {
                                    prop.SetValue(argValue, claimLocationId.Value);
                                }
                                catch
                                {
                                    // Ignore if type conversion fails
                                }
                            }
                        }
                    }
                }
            }

            await next();
        }
    }
}
