namespace CapybaraHome.Application.DataTransfer;

/// <summary>Settings for the automatic backups, bound from the <c>Backups</c> configuration section.</summary>
public class BackupOptions
{
    /// <summary>Configuration section these settings are bound from.</summary>
    public const string SectionName = "Backups";

    /// <summary>Whether automatic backups run at all.</summary>
    public bool Enabled { get; set; }

    /// <summary>Directory the export files are written to. A volume mount in a container.</summary>
    public string Destination { get; set; } = "backups";

    /// <summary>How long to wait between backups.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>How many files to keep. The oldest beyond this are deleted. Zero keeps every file.</summary>
    public int Keep { get; set; } = 14;
}
