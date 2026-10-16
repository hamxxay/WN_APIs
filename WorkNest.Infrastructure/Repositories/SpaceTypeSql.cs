using Microsoft.Data.SqlClient;
using WorkNest.Application.DTOs.SpaceType;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Space types per location (SpaceTypes_Location P1_02..P1_04): dbo.WN_SpaceTypes_GetList / _Insert / _Update take
    /// @LocationId. Until those scripts have run the procedures don't know it (SQL error 8144 too many arguments / 8145
    /// not a parameter): the call is repeated without it, so lists, adds and edits keep working as before (rows then
    /// have no LocationId). The procedures' rules (THROW 50000: a name that already exists at the location, a type with
    /// spaces at another location, an unknown location) become a <see cref="SpaceTypeRuleException"/> with their text.
    /// </summary>
    public static class SpaceTypeSql
    {
        /// <summary>Runs <paramref name="run"/> with the location; without it (null) when the procedure doesn't know it.</summary>
        public static async Task<T> WithLocationAsync<T>(int? locationId, Func<int?, Task<T>> run)
        {
            try
            {
                return await run(locationId);
            }
            catch (SqlException ex) when (locationId is not null && ex.Number is 8144 or 8145)
            {
                return await run(null);   // P1 not run yet: no @LocationId, as before
            }
        }

        /// <summary>A procedure rule (error 50000) as a <see cref="SpaceTypeRuleException"/>; anything else as it is.</summary>
        public static async Task<T> RulesAsync<T>(Func<Task<T>> run)
        {
            try
            {
                return await run();
            }
            catch (SqlException ex) when (ex.Number == 50000)
            {
                throw new SpaceTypeRuleException(ex.Message, ex);
            }
        }
    }
}
