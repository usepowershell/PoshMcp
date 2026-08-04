using System;
using System.Collections.Generic;
using System.Linq;

namespace PoshMcp.Tests.Characterization.Phase4;

/// <summary>
/// Canonical contract for merging the per-mode Phase 4 artifacts' <c>sameJobPaired</c> flags.
///
/// <para>
/// The CI "Merge Phase 4 comparison artifacts" step previously hardcoded
/// <c>sameJobPaired: true</c> in its jq expression, so the merged artifact claimed same-job
/// pairing regardless of what the per-mode measurements actually reported. This validator
/// defines the correct rule the merge must follow (and which the jq step mirrors):
/// the merged flag is <c>true</c> only when <b>every</b> per-mode input is present and
/// <c>true</c>; a missing (null) or <c>false</c> input is a deterministic failure.
/// </para>
/// </summary>
internal static class Phase4MergeValidator
{
    /// <summary>
    /// Validates the per-mode <c>sameJobPaired</c> flags and returns the merged value.
    /// Throws <see cref="InvalidOperationException"/> when there are no inputs, when any input
    /// is missing (null), or when any input is <c>false</c> — the merge must never silently
    /// override a false/missing flag with true.
    /// </summary>
    /// <param name="perModeFlags">
    /// One entry per per-mode artifact. <c>null</c> represents a missing <c>sameJobPaired</c>
    /// field (e.g. a truncated or legacy artifact).
    /// </param>
    internal static bool MergeSameJobPaired(IReadOnlyList<bool?> perModeFlags)
    {
        if (perModeFlags is null || perModeFlags.Count == 0)
            throw new InvalidOperationException(
                "Cannot merge sameJobPaired: no per-mode artifacts were provided. " +
                "A partial or empty merge must fail deterministically.");

        var missing = perModeFlags.Count(f => f is null);
        if (missing > 0)
            throw new InvalidOperationException(
                $"Cannot merge sameJobPaired: {missing} of {perModeFlags.Count} per-mode artifact(s) " +
                "are missing the sameJobPaired field. Every mode must report it — the merge must not " +
                "assume true for a missing input.");

        var falseCount = perModeFlags.Count(f => f == false);
        if (falseCount > 0)
            throw new InvalidOperationException(
                $"Cannot merge sameJobPaired: {falseCount} of {perModeFlags.Count} per-mode artifact(s) " +
                "reported sameJobPaired=false. A cross-runner (non-paired) measurement is not authoritative; " +
                "the merged flag must not be overridden to true.");

        // All present and true.
        return true;
    }
}
