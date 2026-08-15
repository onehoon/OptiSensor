namespace OptiSensor.Tweaks.IntelVrr;

/// <summary>Outcome of a single Intel VRR Range Fix run, used for both persistence and UI display.</summary>
internal enum IntelVrrRunStatus
{
    /// <summary>The toggle is off; nothing was inspected or changed.</summary>
    Disabled,

    /// <summary>IGCL could not be initialized/loaded after the bounded startup retry window.</summary>
    Unavailable,

    /// <summary>The active panel's EDID identity did not match the known affected panel.</summary>
    UnsupportedPanel,

    /// <summary>More than one IGCL display output was a plausible candidate and none could be
    /// disambiguated with high confidence.</summary>
    AmbiguousDisplay,

    /// <summary>The panel already reports the EXCELLENT profile with the full native range.</summary>
    AlreadyCorrect,

    /// <summary>The panel reported a CUSTOM or explicit OFF Arc Sync profile; the user's choice
    /// was preserved and no SET call was made.</summary>
    SkippedUserProfile,

    /// <summary>The EXCELLENT profile was set and a readback confirmed the native range.</summary>
    Applied,

    /// <summary>The SET call itself failed (returned a non-success IGCL result).</summary>
    ApplyFailed,

    /// <summary>The SET call reported success but the subsequent readback did not confirm
    /// EXCELLENT / the native range.</summary>
    VerificationFailed
}

/// <summary>Compact, UI-facing snapshot of the most recent Intel VRR Range Fix run. Intentionally
/// small - detailed diagnostics belong in the single-run log written by <see cref="IntelVrrRunLogger"/>,
/// not here.</summary>
internal sealed record IntelVrrRunResult(
    IntelVrrRunStatus Status,
    string? PanelName,
    string? RangeBeforeText,
    string? RangeAfterText,
    string Message,
    DateTimeOffset TimestampUtc)
{
    public static IntelVrrRunResult Create(IntelVrrRunStatus status, string message, string? panelName = null,
        string? rangeBeforeText = null, string? rangeAfterText = null)
    {
        return new IntelVrrRunResult(status, panelName, rangeBeforeText, rangeAfterText, message, DateTimeOffset.UtcNow);
    }
}
