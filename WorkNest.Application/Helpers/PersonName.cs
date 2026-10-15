using System;
using System.Linq;

namespace WorkNest.Application.Helpers
{
    /// <summary>A person's full name from a first and a last name, without repeating the last name.</summary>
    public static class PersonName
    {
        /// <summary>
        /// "Haroon" + "Laghari" -> "Haroon Laghari". When the first name already holds the full name ("Haroon Laghari" +
        /// "Laghari") the last name is not added again (that is how "Haroon Laghari Laghari" reached WN_Users.Name).
        /// Extra spaces are collapsed; empty parts are skipped.
        /// </summary>
        public static string Join(string? first, string? last)
        {
            var f = Clean(first);
            var l = Clean(last);
            if (l.Length == 0) return f;
            if (f.Length == 0) return l;
            if (string.Equals(f, l, StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(" " + l, StringComparison.OrdinalIgnoreCase))
                return f;
            return f + " " + l;
        }

        private static string Clean(string? s) =>
            string.Join(" ", (s ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(p => p.Length > 0));
    }
}
