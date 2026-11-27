using System;
using System.Collections.Generic;
using System.Linq;

namespace ViModManager.Core;

/// <summary>
/// Resolves a selected target to a format-neutral adapter. Only local-folder deployment is
/// supported; no game detection, executable-name matching, or proprietary-format handling exists.
/// </summary>
public sealed class TargetAdapterRegistry
{
    private readonly IReadOnlyList<ITargetAdapter> _adapters;

    public TargetAdapterRegistry(params ITargetAdapter[] adapters)
    {
        _adapters = adapters.Length == 0
            ? new ITargetAdapter[] { new TargetFolder() }
            : adapters.ToList();

        Default = _adapters[0];
    }

    public ITargetAdapter Default { get; }

    public IReadOnlyList<ITargetAdapter> Adapters => _adapters;

    public ITargetAdapter Resolve(string? folder) =>
        _adapters.FirstOrDefault(adapter => adapter.Supports(folder)) ?? Default;
}
