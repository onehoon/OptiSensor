namespace OptiSensor.Tweaks.IntelVrr;

/// <summary>
/// Orchestrates one run of the Intel VRR Range Fix: detect the affected panel, inspect its
/// current Arc Sync state via IGCL, apply the conservative policy below, verify by readback, and
/// persist a compact result. Depends only on <see cref="IIntelArcSyncClient"/> and a panel-identity
/// lookup delegate, so this class is fully unit testable with fakes and has zero UI dependency.
/// </summary>
internal sealed class IntelVrrRangeTweak
{
    /// <summary>Native panel capability class this tweak targets, in Hz.</summary>
    private const double NativeMinHz = 48.0;
    private const double NativeMaxHz = 120.0;
    private const double ToleranceHz = 0.1;

    private readonly Func<IIntelArcSyncClient> _clientFactory;
    private readonly Func<IReadOnlyList<PanelIdentity>> _panelIdentitiesProvider;
    private readonly List<string> _log = [];
    private int _loggedCallCount;

    public IntelVrrRangeTweak(Func<IIntelArcSyncClient> clientFactory, Func<IReadOnlyList<PanelIdentity>> panelIdentitiesProvider)
    {
        _clientFactory = clientFactory;
        _panelIdentitiesProvider = panelIdentitiesProvider;
    }

    /// <summary>Diagnostic lines produced by the most recent <see cref="Run"/> call. Callers that
    /// need to accumulate diagnostics across multiple attempts (e.g. <c>TweakStartupCoordinator</c>'s
    /// bounded retry loop) should read this after each call and append it to their own session log -
    /// this class only tracks a single attempt's worth of lines at a time.</summary>
    public IReadOnlyList<string> LastRunLog => _log;

    /// <summary>Runs the tweak once. <paramref name="isEnabled"/> is the persisted toggle value -
    /// callers must not call this at all if they can avoid the client init cost, but passing it
    /// through keeps the "disabled -> do nothing" branch covered by the same tested code path.</summary>
    public IntelVrrRunResult Run(bool isEnabled)
    {
        _log.Clear();
        _loggedCallCount = 0;

        if (!isEnabled)
        {
            Log("Toggle is disabled. No action taken.");
            return Finish(IntelVrrRunResult.Create(IntelVrrRunStatus.Disabled, "Disabled by user."));
        }

        var panelIdentities = SafeGetPanelIdentities();
        var affectedPanel = panelIdentities.FirstOrDefault(AffectedPanelDetector.IsAffectedPanel);
        if (affectedPanel is null)
        {
            Log("No display matched the affected panel identity (CSW / 0801 / PN8007QB1-2).");
            return Finish(IntelVrrRunResult.Create(IntelVrrRunStatus.UnsupportedPanel,
                "This panel is not the affected MSI Claw 8 display."));
        }

        Log($"Affected panel detected: manufacturer={affectedPanel.ManufacturerCode}, product=0x{affectedPanel.ProductCodeHex}, name={affectedPanel.PanelName}");

        using var client = _clientFactory();
        var initialized = client.TryInitialize();
        FlushCallLog(client);
        if (!initialized)
        {
            Log("IGCL initialization failed.");
            return Finish(IntelVrrRunResult.Create(IntelVrrRunStatus.Unavailable,
                "Intel Graphics Control Library is not available.", affectedPanel.PanelName));
        }

        var outputs = client.EnumerateDisplayOutputs();
        FlushCallLog(client);
        Log($"IGCL reported {outputs.Count} display output(s).");

        var candidateCapabilities = new List<(IntelDisplayOutputHandle Output, IntelArcSyncMonitorCapability Capability)>();
        foreach (var output in outputs)
        {
            var capability = client.TryGetMonitorCapability(output);
            FlushCallLog(client);
            if (capability is null || !capability.IsIntelArcSyncSupported)
                continue;

            candidateCapabilities.Add((output, capability));
        }

        Log($"{candidateCapabilities.Count} output(s) support Arc Sync.");

        var resolved = ResolveSingleCandidate(candidateCapabilities, affectedPanel);
        if (resolved is null)
        {
            if (candidateCapabilities.Count == 0)
            {
                Log("No Arc Sync capable output found.");
                return Finish(IntelVrrRunResult.Create(IntelVrrRunStatus.Unavailable,
                    "Arc Sync is not available on this display.", affectedPanel.PanelName));
            }

            Log("Multiple ambiguous Arc Sync capable outputs (or a correlating identity mismatch); cannot safely disambiguate.");
            return Finish(IntelVrrRunResult.Create(IntelVrrRunStatus.AmbiguousDisplay,
                "Multiple displays matched; skipped to avoid changing the wrong one.", affectedPanel.PanelName));
        }

        var (output2, capability2) = resolved.Value;

        if (!IsNativeCapabilityRange(capability2.MinimumRefreshRateInHz, capability2.MaximumRefreshRateInHz))
        {
            Log($"Capability range {capability2.MinimumRefreshRateInHz}-{capability2.MaximumRefreshRateInHz} Hz is not the expected ~{NativeMinHz}-{NativeMaxHz} Hz class.");
            return Finish(IntelVrrRunResult.Create(IntelVrrRunStatus.UnsupportedPanel,
                "Display capability range does not match the expected panel class.", affectedPanel.PanelName));
        }

        Log($"Monitor capability frame-time constraints: increase={capability2.MaxFrameTimeIncreaseInUs}us, decrease={capability2.MaxFrameTimeDecreaseInUs}us (recorded for diagnostics only, not a policy condition).");

        var profile = client.TryGetArcSyncProfile(output2);
        FlushCallLog(client);
        if (profile is null)
        {
            Log("ctlGetIntelArcSyncProfile failed.");
            return Finish(IntelVrrRunResult.Create(IntelVrrRunStatus.Unavailable,
                "Could not read the current Arc Sync profile.", affectedPanel.PanelName));
        }

        var beforeRangeText = FormatRange(profile.MinRefreshRateInHz, profile.MaxRefreshRateInHz);
        Log($"Current profile={profile.Profile}, current range={beforeRangeText}");

        switch (profile.Profile)
        {
            case CtlIntelArcSyncProfile.Excellent when IsNativeCapabilityRange(profile.MinRefreshRateInHz, profile.MaxRefreshRateInHz):
                Log("Already EXCELLENT with the full native range. No change needed.");
                return Finish(IntelVrrRunResult.Create(IntelVrrRunStatus.AlreadyCorrect,
                    "Already using the native VRR range.", affectedPanel.PanelName, beforeRangeText, beforeRangeText));

            case CtlIntelArcSyncProfile.Custom:
            case CtlIntelArcSyncProfile.Off:
                Log($"Profile is {profile.Profile}; this is an explicit user choice, preserving it.");
                return Finish(IntelVrrRunResult.Create(IntelVrrRunStatus.SkippedUserProfile,
                    $"Preserved existing user profile ({profile.Profile}).", affectedPanel.PanelName, beforeRangeText, beforeRangeText));

            case CtlIntelArcSyncProfile.Recommended:
            case CtlIntelArcSyncProfile.Good:
            case CtlIntelArcSyncProfile.Compatible:
            case CtlIntelArcSyncProfile.Vesa:
                // Ordinary driver-managed profile. Only eligible for the fix if its active range is
                // verifiably narrower than the monitor's native capability - if it already reports
                // the full native range (within tolerance), there is nothing to fix and SETting
                // EXCELLENT would be an unnecessary mutation.
                if (IsWithinTolerance(profile.MinRefreshRateInHz, capability2.MinimumRefreshRateInHz)
                    && IsWithinTolerance(profile.MaxRefreshRateInHz, capability2.MaximumRefreshRateInHz))
                {
                    Log($"Profile is {profile.Profile} but active range {beforeRangeText} already matches the monitor's native capability range; no mutation needed.");
                    return Finish(IntelVrrRunResult.Create(IntelVrrRunStatus.AlreadyCorrect,
                        "Current profile already reports the full native range; no mutation needed.",
                        affectedPanel.PanelName, beforeRangeText, beforeRangeText));
                }

                break; // narrower than capability - eligible for the fix.

            case CtlIntelArcSyncProfile.Excellent:
                // EXCELLENT but the active range doesn't (yet) show the full native span - still
                // eligible; re-asserting EXCELLENT is how the official fix is applied.
                break;

            default:
                // Invalid / Max / unrecognized - fail open, no mutation.
                Log($"Unrecognized or non-actionable profile value {profile.Profile}; failing open without mutation.");
                return Finish(IntelVrrRunResult.Create(IntelVrrRunStatus.UnsupportedPanel,
                    "Unrecognized Arc Sync profile state.", affectedPanel.PanelName, beforeRangeText));
        }

        Log("Applying EXCELLENT profile via ctlSetIntelArcSyncProfile.");
        var (setSuccess, setError) = client.TrySetArcSyncProfile(output2, CtlIntelArcSyncProfile.Excellent);
        FlushCallLog(client);
        if (!setSuccess)
        {
            Log($"SET call failed: {setError}");
            return Finish(IntelVrrRunResult.Create(IntelVrrRunStatus.ApplyFailed,
                $"Failed to apply profile: {setError}", affectedPanel.PanelName, beforeRangeText));
        }

        var verifyProfile = client.TryGetArcSyncProfile(output2);
        FlushCallLog(client);
        var verifiedOk = verifyProfile is not null
            && verifyProfile.Profile == CtlIntelArcSyncProfile.Excellent
            && IsNativeCapabilityRange(verifyProfile.MinRefreshRateInHz, verifyProfile.MaxRefreshRateInHz);

        var afterRangeText = verifyProfile is null
            ? "unknown"
            : FormatRange(verifyProfile.MinRefreshRateInHz, verifyProfile.MaxRefreshRateInHz);

        if (!verifiedOk)
        {
            Log($"Readback after SET did not confirm EXCELLENT/native range (readback profile={verifyProfile?.Profile}, range={afterRangeText}).");
            return Finish(IntelVrrRunResult.Create(IntelVrrRunStatus.VerificationFailed,
                "Applied the profile but could not verify it took effect.", affectedPanel.PanelName, beforeRangeText, afterRangeText));
        }

        Log($"Verified: profile is EXCELLENT with range {afterRangeText}.");
        return Finish(IntelVrrRunResult.Create(IntelVrrRunStatus.Applied,
            "Restored the native VRR range.", affectedPanel.PanelName, beforeRangeText, afterRangeText));
    }

    /// <summary>Picks the one output to act on. Multiple Arc-Sync-capable outputs are ambiguous
    /// unless exactly one of them reports the expected native capability range, in which case that
    /// single output is used as a conservative fallback (per policy).
    ///
    /// KNOWN LIMITATION: full robust correlation between the Windows-reported affected monitor and
    /// an IGCL display output (e.g. via display path/target IDs or EDID cross-referencing) is not
    /// implemented here - IGCL's output handle does not currently expose enough identity surface
    /// for that in this codebase. As a conservative mitigation for PR1, we require EXACTLY ONE
    /// output to be both Arc-Sync-capable and in the expected native capability class, and if IGCL
    /// happens to expose a friendly name for that output, it must not positively contradict the
    /// WMI-detected affected panel's name. Any additional plausible candidate makes the result
    /// ambiguous rather than silently picking the first match. Extending this with real path/EDID
    /// correlation is deferred to a follow-up once more of the IGCL API surface is available.</summary>
    private static (IntelDisplayOutputHandle, IntelArcSyncMonitorCapability)? ResolveSingleCandidate(
        List<(IntelDisplayOutputHandle Output, IntelArcSyncMonitorCapability Capability)> candidates,
        PanelIdentity affectedPanel)
    {
        if (candidates.Count == 1)
            return MatchesCorrelatingIdentity(candidates[0].Output, affectedPanel) ? candidates[0] : null;

        var nativeRangeCandidates = candidates
            .Where(c => IsNativeCapabilityRange(c.Capability.MinimumRefreshRateInHz, c.Capability.MaximumRefreshRateInHz))
            .ToList();

        if (nativeRangeCandidates.Count != 1)
            return null;

        var single = nativeRangeCandidates[0];
        return MatchesCorrelatingIdentity(single.Output, affectedPanel) ? single : null;
    }

    /// <summary>If IGCL exposes any correlating identity for the output (currently just a friendly
    /// name), it must not contradict the WMI-detected affected panel. Absence of correlating data
    /// is not treated as a mismatch - see the known-limitation note above.</summary>
    private static bool MatchesCorrelatingIdentity(IntelDisplayOutputHandle output, PanelIdentity affectedPanel)
    {
        if (output.FriendlyName is null || affectedPanel.PanelName is null)
            return true;

        return output.FriendlyName.Contains(affectedPanel.PanelName, StringComparison.OrdinalIgnoreCase)
            || affectedPanel.PanelName.Contains(output.FriendlyName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNativeCapabilityRange(double minHz, double maxHz)
    {
        return Math.Abs(minHz - NativeMinHz) <= ToleranceHz && Math.Abs(maxHz - NativeMaxHz) <= ToleranceHz;
    }

    private static bool IsWithinTolerance(double a, double b) => Math.Abs(a - b) <= ToleranceHz;

    /// <summary>Appends any native call diagnostics recorded by <paramref name="client"/> since the
    /// last flush to this run's log, so the detailed log shows every native call attempted (with its
    /// raw/symbolic IGCL result code), not just a generic pass/fail summary.</summary>
    private void FlushCallLog(IIntelArcSyncClient client)
    {
        for (; _loggedCallCount < client.CallLog.Count; _loggedCallCount++)
            Log(client.CallLog[_loggedCallCount].ToString());
    }

    private static string FormatRange(double minHz, double maxHz) => $"{minHz:0.#}-{maxHz:0.#} Hz";

    private IReadOnlyList<PanelIdentity> SafeGetPanelIdentities()
    {
        try
        {
            return _panelIdentitiesProvider();
        }
        catch (Exception ex)
        {
            Log($"Panel identity lookup failed: {ex.Message}");
            return [];
        }
    }

    private void Log(string line) => _log.Add(line);

    private IntelVrrRunResult Finish(IntelVrrRunResult result)
    {
        _log.Add($"Result: {result.Status} - {result.Message}");
        IntelVrrResultStore.Save(result);
        return result;
    }
}
