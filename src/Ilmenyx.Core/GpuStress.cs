// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
namespace Ilmenyx.Core;

/// <summary>GPU load for the stress page. Implemented by the Vulkan module; faked in tests so no GPU work ever runs there.</summary>
public interface IGpuStressEngine
{
    /// <summary>Null when a usable adapter exists; otherwise the reason (no loader, no Vulkan 1.3, software renderer only…).</summary>
    string? UnavailableReason { get; }
    string AdapterName { get; }
    /// <summary>True for a GPU that shares the CPU package/die (its temperature is then the CPU temperature).</summary>
    bool SharesCpuThermals { get; }
    bool Running { get; }
    double Fps { get; }
    /// <summary>Set when the run ended on its own with a failure (device lost, no response); null otherwise.</summary>
    string? Error { get; }
    void Start();
    void Stop();
}
