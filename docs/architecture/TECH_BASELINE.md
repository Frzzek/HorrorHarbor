# Technical baseline references

- .NET support policy: https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core
- WPF overview: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/
- WPF .NET 10 changes: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100
- Microsoft.Data.Sqlite.Core package: https://www.nuget.org/packages/Microsoft.Data.Sqlite.Core/10.0.10
- SQLite3MC.PCLRaw.bundle package: https://www.nuget.org/packages/SQLite3MC.PCLRaw.bundle/2.3.6

The solution centrally pins its package versions. The WPF shell targets
`net10.0-windows` and the non-UI projects target `net10.0`.
