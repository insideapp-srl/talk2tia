using Talk2Tia.Core.Openness;
using Xunit;

namespace Talk2Tia.Tests;

/// <summary>Installs the Openness assembly resolver once, before any test touches Siemens types.</summary>
public sealed class ResolverFixture
{
    private static readonly object Gate = new();
    private static bool _installed;

    public ResolverFixture()
    {
        lock (Gate)
        {
            if (!_installed)
            {
                OpennessResolver.Install();
                _installed = true;
            }
        }
    }
}

[CollectionDefinition(Name)]
public sealed class OpennessCollection : ICollectionFixture<ResolverFixture>
{
    public const string Name = "Openness";
}
