using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

// These tests share a process-wide cache and clear it between cases.
[CollectionDefinition("HandleCache", DisableParallelization = true)]
public sealed class HandleCacheCollection { }
