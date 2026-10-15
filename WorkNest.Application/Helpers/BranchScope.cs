namespace WorkNest.Application.Helpers
{
    /// <summary>
    /// Branch routing (BR_01_branch_routing): which tour inquiries and complaints a staff member sees, and which branch
    /// (dbo.WN_Locations) they may give one. Location-bound staff (admin, sales executive, sales manager, access officer)
    /// see the items of their own locations plus every item without a branch; everyone else (super admin, CEO,
    /// receptionist) sees everything. Items without a branch stay visible to everyone until staff set one.
    /// </summary>
    public sealed class BranchScope
    {
        private BranchScope(IReadOnlyList<int>? locations) => Locations = locations;

        /// <summary>The staff member's locations; null = every location (not location-bound).</summary>
        public IReadOnlyList<int>? Locations { get; }

        public bool IsBound => Locations != null;

        /// <summary>Not location-bound: every inquiry / complaint, any active branch.</summary>
        public static readonly BranchScope Everything = new(null);

        /// <summary>Location-bound staff with these locations (none = only the items without a branch).</summary>
        public static BranchScope Bound(IEnumerable<int> locationIds) =>
            new(locationIds.Where(i => i > 0).Distinct().ToList());

        /// <summary>True when an item with this branch (null = no branch) is visible.</summary>
        public bool Sees(int? locationId) => Locations == null || locationId == null || Locations.Contains(locationId.Value);

        /// <summary>True when the staff member may give an item this branch.</summary>
        public bool MayChoose(int locationId) => Locations == null || Locations.Contains(locationId);

        /// <summary>
        /// The rows a list shows for its optional ?locationId= filter: none = the whole scope (location-bound: their
        /// locations + the rows without a branch), 0 = only the rows without a branch, an id = only that branch.
        /// Null = forbidden (a location outside the staff member's own).
        /// </summary>
        public BranchFilter? ForList(int? requested)
        {
            if (requested is null or < 0)
                return Locations == null ? BranchFilter.None : new BranchFilter(Locations, true, false);
            if (requested == 0) return new BranchFilter(Array.Empty<int>(), true, true);
            if (!MayChoose(requested.Value)) return null;
            return new BranchFilter(new[] { requested.Value }, false, true);
        }

        /// <summary>
        /// The branch of a new inquiry / complaint added by this staff member (or the public website / app when the scope
        /// is <see cref="Everything"/>). <paramref name="requested"/> null / 0 = not chosen. Rules: a location-bound staff
        /// member with ONE location always gets it (even when none was chosen); a chosen branch must be one of theirs
        /// (403) and an active location (400). Not chosen otherwise = no branch.
        /// </summary>
        public BranchChoice ForNew(int? requested, IReadOnlyCollection<BranchOption> activeLocations)
        {
            if (requested is <= 0) requested = null;
            if (Locations != null)
            {
                if (requested == null)
                {
                    if (Locations.Count != 1) return BranchChoice.NoBranch;
                    var own = Locations[0];
                    return BranchChoice.Of(activeLocations.FirstOrDefault(l => l.Id == own) ?? new BranchOption(own, null));
                }
                if (!Locations.Contains(requested.Value)) return BranchChoice.Forbidden(BranchMessages.NotYourBranch);
            }
            if (requested == null) return BranchChoice.NoBranch;
            var match = activeLocations.FirstOrDefault(l => l.Id == requested.Value);
            return match == null ? BranchChoice.Invalid(BranchMessages.InvalidBranch) : BranchChoice.Of(match);
        }

        /// <summary>
        /// The new branch of an existing inquiry / complaint (set / change). Location-bound: one of their own locations
        /// (403), never empty. Everyone else: an active location, or null / 0 = no branch.
        /// </summary>
        public BranchChoice ForChange(int? requested, IReadOnlyCollection<BranchOption> activeLocations)
        {
            if (requested is <= 0) requested = null;
            if (requested == null)
                return Locations == null ? BranchChoice.NoBranch : BranchChoice.Invalid(BranchMessages.ChooseBranch);
            if (!MayChoose(requested.Value)) return BranchChoice.Forbidden(BranchMessages.NotYourBranch);
            var match = activeLocations.FirstOrDefault(l => l.Id == requested.Value);
            return match == null ? BranchChoice.Invalid(BranchMessages.InvalidBranch) : BranchChoice.Of(match);
        }

        /// <summary>Active locations (dbo.WN_Locations_GetAll rows) as id + name.</summary>
        public static List<BranchOption> Options(IEnumerable<IDictionary<string, object?>> locationRows) =>
            locationRows
                .Select(r => new BranchOption(
                    r.TryGetValue("Id", out var id) && id is not null && id is not DBNull ? Convert.ToInt32(id) : 0,
                    r.TryGetValue("Name", out var n) && n is not null && n is not DBNull ? n.ToString()?.Trim() : null))
                .Where(o => o.Id > 0)
                .ToList();

        /// <summary>The location whose name is this branch text (spaces and case ignored); null when none matches.</summary>
        public static BranchOption? ByName(string? branch, IEnumerable<BranchOption> locations)
        {
            var key = (branch ?? "").Trim();
            if (key.Length == 0) return null;
            return locations.FirstOrDefault(l => string.Equals((l.Name ?? "").Trim(), key, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>An active location: Id and Name (dbo.WN_Locations).</summary>
    public sealed record BranchOption(int Id, string? Name);

    /// <summary>
    /// A list's branch filter: rows of <see cref="LocationIds"/> (null = every row) and, when
    /// <see cref="IncludeUnassigned"/>, the rows without a branch. <see cref="Requested"/> = asked for with ?locationId=
    /// (otherwise the staff member's own scope).
    /// </summary>
    public sealed record BranchFilter(IReadOnlyList<int>? LocationIds, bool IncludeUnassigned, bool Requested)
    {
        public static readonly BranchFilter None = new(null, true, false);

        /// <summary>No filter: every row.</summary>
        public bool IsNone => LocationIds == null;

        /// <summary>"1,2" for a @LocationIds parameter; null = no filter; "-1" (matches nothing) for only the rows without a branch.</summary>
        public string? Csv => LocationIds == null ? null : (LocationIds.Count == 0 ? "-1" : string.Join(",", LocationIds));

        public bool Matches(int? locationId) =>
            LocationIds == null || (locationId == null ? IncludeUnassigned : LocationIds.Contains(locationId.Value));
    }

    /// <summary>Outcome of <see cref="BranchScope.ForNew"/> / <see cref="BranchScope.ForChange"/>: the branch (null = none) or an error.</summary>
    public sealed record BranchChoice(BranchOption? Location, int ErrorStatus, string? Error)
    {
        public static readonly BranchChoice NoBranch = new(null, 0, null);
        public static BranchChoice Of(BranchOption location) => new(location, 0, null);
        public static BranchChoice Forbidden(string message) => new(null, 403, message);
        public static BranchChoice Invalid(string message) => new(null, 400, message);
        public bool IsError => ErrorStatus != 0;
        public int? LocationId => Location?.Id;
    }

    /// <summary>Plain-language messages for the branch rules (shown as-is by the CMS, website and app).</summary>
    public static class BranchMessages
    {
        public const string NotYourBranch = "You can only choose one of your own branches.";
        public const string InvalidBranch = "Choose a valid branch.";
        public const string ChooseBranch = "Choose the branch.";
        public const string ListForbidden = "You can only see the branches you are assigned to.";
        public const string ContactsUpdateMissing =
            "Branches for tour inquiries are not set up on this database yet (database update BR_01_branch_routing).";
        public const string ComplaintsUpdateMissing =
            "Changing a complaint's branch is not set up on this database yet (database update BR_01_branch_routing).";
    }
}
