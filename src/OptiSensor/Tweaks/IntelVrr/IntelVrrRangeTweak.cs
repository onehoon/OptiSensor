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

    public IntelVrrRangeTweak(Func<IIntelArcSyncClient> clientFactory, Func<IReadOnlyList<PanelIdentity>> panelIdentitiesProvider)
    {
        _clientFactory = clientFactory;
        _panelIdentitiesProvider = panelIdentitiesProvider;
    }

    /// <summary>Runs the tweak once. <paramref name="isEnabled"/> is the persisted toggle value -
    /// callers must not call this at all if they can avoid the client init cost, but passing it
    /// through keeps the "disabled -> do nothing" branch covered by the same tested code path.</summary>
    public IntelVrrRunResult Run(bool isEnabled)
    {
        _log.Clear();

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
        if (!client.TryInitialize())
        {
            Log("IGCL initialization failed.");
            return Finish(IntelVrrRunResult.Create(IntelVrrRunStatus.Unavailable,
                "Intel Graphics Control Library is not available.", affectedPanel.PanelName));
        }

        var outputs = client.EnumerateDisplayOutputs();
        Log($"IGCL reported {outputs.Count} display output(s).");

        var candidateInfos = new List<(IntelDisplayOutputHandle Output, IntelArcSyncInfo Info)>();
        foreach (var output in outputs)
        {
            var info = client.TryGetArcSyncInfo(output);
            if (info is null || !info.IsArcSyncSupported)
                continue;

            candidateInfos.Add((output, info));
        }

        Log($"{candidateInfos.Count} output(s) support Arc Sync.");

        var resolved = ResolveSingleCandidate(candidateInfos);
        if (resolved is null)
        {
            if (candidateInfos.Count == 0)
            {
                Log("No Arc Sync capable output found.");
                return Finish(IntelVrrRunResult.Create(IntelVrrRunStatus.Unavailable,
                    "Arc Sync is not available on this display.", affectedPanel.PanelName));
            }

            Log("Multiple ambiguous Arc Sync capable outputs; cannot safely disambiguate.");
            return Finish(IntelVrrRunResult.Create(IntelVrrRunStatus.AmbiguousDisplay,
                "Multiple displays matched; skipped to avoid changing the wrong one.", affectedPanel.PanelName));
        }

        var (output2, info2) = resolved.Value;

        if (!IsNativeCapabilityRange(info2.CapabilityMinRefreshHz, info2.CapabilityMaxRefreshHz))
        {
            Log($"Capability range {info2.CapabilityMinRefreshHz}-{info2.CapabilityMaxRefreshHz} Hz is not the expected ~{NativeMinHz}-{NativeMaxHz} Hz class.");
            return Finish(IntelVrrRunResult.Create(IntelVrrRunStatus.UnsupportedPanel,
                "Display capability range does not match the expected panel class.", affectedPanel.PanelName));
        }

        var beforeRangeText = FormatRange(info2.CurrentMinRefreshHz, info2.CurrentMaxRefreshHz);
        Log($"Current profile={info2.CurrentProfile}, current range={beforeRangeText}");

        switch (info2.CurrentProfile)
        {
            case CtlIntelArcSyncProfile.Excellent when IsNativeCapabilityRange(info2.CurrentMinRefreshHz, info2.CurrentMaxRefreshHz):
                Log("Already EXCELLENT with the full native range. No change needed.");
                return Finish(IntelVrrRunResult.Create(IntelVrrRunStatus.AlreadyCorrect,
                    "Already using the native VRR range.", affectedPanel.PanelName, beforeRangeText, beforeRangeText));

            case CtlIntelArcSyncProfile.Custom:
            case CtlIntelArcSyncProfile.Off:
                Log($"Profile is {info2.CurrentProfile}; this is an explicit user choice, preserving it.");
                return Finish(IntelVrrRunResult.Create(IntelVrrRunStatus.SkippedUserProfile,
                    $"Preserved existing user profile ({info2.CurrentProfile}).", affectedPanel.PanelName, beforeRangeText, beforeRangeText));

            case CtlIntelArcSyncProfile.Default:
            case CtlIntelArcSyncProfile.Basic:
                break; // ordinary driver-managed profile - eligible for the fix.

            case CtlIntelArcSyncProfile.Excellent:
                // EXCELLENT but the active range doesn't (yet) show the full native span - still
                // eligible; re-asserting EXCELLENT is how the official fix is applied.
                break;

            default:
                Log($"Unrecognized profile value {info2.CurrentProfile}; failing open without mutation.");
                return Finish(IntelVrrRunResult.Create(IntelVrrRunStatus.UnsupportedPanel,
                    "Unrecognized Arc Sync profile state.", affectedPanel.PanelName, beforeRangeText));
        }

        Log("Applying EXCELLENT profile via ctlSetIntelArcSyncProfile.");
        var (setSuccess, setError) = client.TrySetArcSyncProfile(output2, CtlIntelArcSyncProfile.Excellent);
        if (!setSuccess)
        {
            Log($"SET call failed: {setError}");
            return Finish(IntelVrrRunResult.Create(IntelVrrRunStatus.ApplyFailed,
                $"Failed to apply profile: {setError}", affectedPanel.PanelName, beforeRangeText));
        }

        var verifyInfo = client.TryGetArcSyncInfo(output2);
        var verifiedOk = verifyInfo is not null
            && verifyInfo.CurrentProfile == CtlIntelArcSyncProfile.Excellent
            && IsNativeCapabilityRange(verifyInfo.CurrentMinRefreshHz, verifyInfo.CurrentMaxRefreshHz);

        var afterRangeText = verifyInfo is null
            ? "unknown"
            : FormatRange(verifyInfo.CurrentMinRefreshHz, verifyInfo.CurrentMaxRefreshHz);

        if (!verifiedOk)
        {
            Log($"Readback after SET did not confirm EXCELLENT/native range (readback profile={verifyInfo?.CurrentProfile}, range={afterRangeText}).");
            return Finish(IntelVrrRunResult.Create(IntelVrrRunStatus.VerificationFailed,
                "Applied the profile but could not verify it took effect.", affectedPanel.PanelName, beforeRangeText, afterRangeText));
        }

        Log($"Verified: profile is EXCELLENT with range {afterRangeText}.");
        return Finish(IntelVrrRunResult.Create(IntelVrrRunStatus.Applied,
            "Restored the native VRR range.", affectedPanel.PanelName, beforeRangeText, afterRangeText));
    }

    /// <summary>Picks the one output to act on. Multiple Arc-Sync-capable outputs are ambiguous
    /// unless exactly one of them reports the expected native capability range, in which case that
    /// single output is used as a conservative fallback (per policy).</summary>
    private static (IntelDisplayOutputHandle, IntelArcSyncInfo)? ResolveSingleCandidate(
        List<(IntelDisplayOutputHandle Output, IntelArcSyncInfo Info)> candidates)
    {
        if (candidates.Count == 1)
            return candidates[0];

        var nativeRangeCandidates = candidates
            .Where(c => IsNativeCapabilityRange(c.Info.CapabilityMinRefreshHz, c.Info.CapabilityMaxRefreshHz))
            .ToList();

        return nativeRangeCandidates.Count == 1 ? nativeRangeCandidates[0] : null;
    }

    private static bool IsNativeCapabilityRange(double minHz, double maxHz)
    {
        return Math.Abs(minHz - NativeMinHz) <= ToleranceHz && Math.Abs(maxHz - NativeMaxHz) <= ToleranceHz;
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
        IntelVrrRunLogger.WriteRun(_log);
        IntelVrrResultStore.Save(result);
        return result;
    }
}
