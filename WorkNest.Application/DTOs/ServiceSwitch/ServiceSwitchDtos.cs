namespace WorkNest.Application.DTOs.ServiceSwitch
{
    /// <summary>
    /// One background service as GET api/admin/services lists it (the CMS Background Services page reads exactly these
    /// camelCase fields): name, displayName, description, isEnabled, configKey, configEnabled, running, updatedBy, updatedAt.
    /// </summary>
    public sealed class ServiceSwitchState
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        /// <summary>The database switch (dbo.WN_ServiceSwitches.IsEnabled); true when the service has no row.</summary>
        public bool IsEnabled { get; set; } = true;
        /// <summary>The appsettings flag that can also turn the service off on this server (null = none).</summary>
        public string? ConfigKey { get; set; }
        /// <summary>That flag on this server: true when the service has no flag.</summary>
        public bool ConfigEnabled { get; set; } = true;
        /// <summary>The service does its work: switched on and not off in appsettings.</summary>
        public bool Running => IsEnabled && ConfigEnabled;
        /// <summary>Who changed the switch last (e-mail); null = never changed.</summary>
        public string? UpdatedBy { get; set; }
        /// <summary>When it was changed last, UTC (JSON "...Z"); null when the service has no row.</summary>
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>A row of dbo.WN_ServiceSwitches (dbo.WN_ServiceSwitches_GetAll / _Set).</summary>
    public sealed class ServiceSwitchRow
    {
        public string ServiceName { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public string? UpdatedBy { get; set; }
        /// <summary>UTC (DateTimeKind.Utc).</summary>
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>Body of POST api/admin/services/{name}: { "isEnabled": true | false }.</summary>
    public sealed class ServiceSwitchRequest
    {
        public bool? IsEnabled { get; set; }
    }
}
