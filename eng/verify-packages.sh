#!/usr/bin/env bash
#
# Verifies the produced packages from a consumer's point of view.
#
# Packing successfully proves very little. The failure that matters is a package that installs
# cleanly and silently generates nothing, so this script checks the analyzer assets are present
# and then builds and runs a throwaway project against the packages.

set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
artifacts="${1:-$root/artifacts}"
version="${JEVGEN_VERSION:-1.0.0-preview.1}"
package="$artifacts/JevGen.$version.nupkg"

if [[ ! -f "$package" ]]; then
  echo "error: $package does not exist. Run 'dotnet pack -c Release -o $artifacts' first." >&2
  exit 1
fi

echo "==> Checking $package carries the compiler extensions"

for asset in JevGen.Generator.dll JevGen.Analyzers.dll JevGen.CodeFixes.dll; do
  if unzip -l "$package" | grep -q "analyzers/dotnet/cs/$asset"; then
    echo "    ok   analyzers/dotnet/cs/$asset"
  else
    echo "    FAIL analyzers/dotnet/cs/$asset is missing; consumers would get no generator." >&2
    exit 1
  fi
done

echo "==> Building a consumer against the packages"

workspace="$(mktemp -d)"
trap 'rm -rf "$workspace"' EXIT

cat > "$workspace/nuget.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$artifacts" />
    <add key="nuget" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
XML

cat > "$workspace/Consumer.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RestorePackagesPath>packages</RestorePackagesPath>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="JevGen" Version="$version" />
    <PackageReference Include="JevGen.DependencyInjection" Version="$version" />
    <PackageReference Include="JevGen.Testing" Version="$version" />
  </ItemGroup>
</Project>
XML

cat > "$workspace/Program.cs" <<'CSHARP'
using System.Text.Json.Serialization;
using JevGen;
using JevGen.Testing;

[assembly: JevJsonContext(typeof(AppJsonContext))]

var fake = JevFake.Create<ITicketAI>();
fake.Returns("route", Department.Billing, confidence: 0.94);

var result = await fake.Client.RouteAsync(new Ticket { Subject = "Charged twice" });

Console.WriteLine($"{result.Value} at {result.Confidence:P0}");

if (result.Value != Department.Billing || Math.Abs(result.Confidence - 0.94) > 1e-9)
{
    Console.Error.WriteLine("The generated client did not map the answer correctly.");
    return 1;
}

return 0;

public enum Department
{
    [JevOption("billing", "Invoices, payments and refunds")]
    Billing,

    [JevOption("technical", "Defects, outages and technical support")]
    Technical,
}

public sealed record Ticket
{
    public required string Subject { get; init; }
}

[JevClient]
public interface ITicketAI
{
    [JevChoice("Which department should handle this ticket?")]
    Task<ChoiceResult<Department>> RouteAsync(Ticket ticket, CancellationToken cancellationToken = default);
}

[JsonSerializable(typeof(Ticket))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
CSHARP

(cd "$workspace" && dotnet run --configuration Release)

echo "==> Packages verified"
