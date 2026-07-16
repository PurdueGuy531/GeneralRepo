# Sub-Module Design in the Modular Monolith

This document describes how sub-modules fit into the .NET 10 modular monolith project structure, using the domain boundaries identified in [domain-boundaries.md](domain-boundaries.md) as the source of candidate sub-modules.

---

## Project Structure Recap

The solution uses one `.csproj` class library per top-level module, with a main API project that only handles wiring:

```
MyApp.sln
├── src/
│   ├── Api/
│   │   └── Api.csproj                ← Program.cs, endpoint registration only
│   └── Modules/
│       └── Claims/
│           ├── Claims.csproj         ← .NET class library
│           └── UseCases/
│               ├── GetClaimSummary/
│               ├── GetClaimDetail/
│               └── SubmitClaimInquiry/
```

Each module exposes extension methods (e.g., `AddClaimsModule()`, `MapClaimsEndpoints()`) that `Program.cs` calls. The question is where sub-modules — like COBRA within Enrollment, or COB within Benefits — fit in this structure.

---

## Two Tiers, Not One Answer

The right structure depends on whether the sub-module is a **genuine bounded sub-domain** or just **organizational grouping** within a larger feature.

| Sub-module characteristic | Use |
|---|---|
| Distinct rules, dedicated workflow, realistic extraction candidate | Nested `.csproj` (Option A) |
| Large feature area, no independent deployment scenario | Subfolder only (Option B) |
| Already treated as a distinct product boundary by the business | Peer `.csproj` at the same level (Option C) |

---

## Option A — Nested `.csproj`, Parent Aggregates (Recommended for True Sub-modules)

The sub-module is its own class library nested physically inside the parent module's folder. The parent's extension methods register and wire the child transparently — the API project never knows the sub-module exists.

### Layout

```
Modules/
└── Enrollment/
    ├── Enrollment.csproj               ← references Cobra/ and Enrollment.Contracts/
    ├── Enrollment.Contracts/
    │   └── Enrollment.Contracts.csproj ← shared IDs and domain events only (see below)
    ├── EnrollmentModule.cs
    ├── UseCases/
    │   ├── GetEnrollmentStatus/
    │   └── SubmitEnrollment/
    └── Cobra/
        ├── Cobra.csproj                ← references Enrollment.Contracts, NOT Enrollment
        ├── CobraModule.cs
        └── UseCases/
            ├── GetCobraEligibility/
            └── SendCobraNotice/
```

### Project References

```xml
<!-- Modules/Enrollment/Enrollment.csproj -->
<ItemGroup>
    <ProjectReference Include="Enrollment.Contracts/Enrollment.Contracts.csproj" />
    <ProjectReference Include="Cobra/Cobra.csproj" />
</ItemGroup>

<!-- Modules/Enrollment/Cobra/Cobra.csproj -->
<ItemGroup>
    <!-- References Contracts for shared types — NOT the parent Enrollment project -->
    <ProjectReference Include="../Enrollment.Contracts/Enrollment.Contracts.csproj" />
</ItemGroup>
```

### Endpoint Registration

The API project stays clean — it has no knowledge of COBRA's existence:

```csharp
// Program.cs
builder.Services.AddEnrollmentModule();
app.MapEnrollmentEndpoints();

// Enrollment/EnrollmentModule.cs — parent aggregates child transparently
public static class EnrollmentModule
{
    public static IServiceCollection AddEnrollmentModule(this IServiceCollection services)
    {
        services.AddScoped<IEnrollmentRepository, EnrollmentRepository>();
        services.AddCobraServices();  // extension method from Cobra.csproj
        return services;
    }

    public static IEndpointRouteBuilder MapEnrollmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/enrollment").WithTags("Enrollment");
        group.MapGet("/status/{memberId:guid}", GetEnrollmentStatus.Handle);
        group.MapPost("/submit", SubmitEnrollment.Handle);

        app.MapCobraEndpoints();  // extension method from Cobra.csproj
        return app;
    }
}

// Enrollment/Cobra/CobraModule.cs
public static class CobraModule
{
    public static IServiceCollection AddCobraServices(this IServiceCollection services)
    {
        services.AddScoped<ICobraRepository, CobraRepository>();
        return services;
    }

    public static IEndpointRouteBuilder MapCobraEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/enrollment/cobra").WithTags("COBRA");
        group.MapGet("/eligibility/{memberId:guid}", GetCobraEligibility.Handle);
        group.MapPost("/notice", SendCobraNotice.Handle);
        return app;
    }
}
```

### The Contracts Project

When sub-modules need to share domain types (IDs, domain events, interfaces) without creating circular references, a small sibling contracts project holds only those shared primitives:

```
Enrollment.Contracts/
├── Enrollment.Contracts.csproj
├── EnrollmentId.cs
└── Events/
    └── EnrollmentTerminated.cs   ← COBRA subscribes to this event
```

Both `Enrollment.csproj` and `Cobra.csproj` reference `Enrollment.Contracts` — neither references the other directly. This eliminates any risk of circular dependencies entirely.

### Extraction Path

When COBRA needs to become its own service:
1. Move `Cobra/` to a top-level `Modules/Enrollment.Cobra/` folder
2. Publish `Enrollment.Contracts` as an internal NuGet package (or shared project)
3. In the parent `Enrollment.csproj`, replace `AddCobraServices()` / `MapCobraEndpoints()` calls with an HTTP client or message bus consumer
4. `Program.cs` is untouched — the parent still aggregates

---

## Option B — Subfolder Only, Same `.csproj` (For Organizational Grouping)

When the sub-concern is genuinely internal to the domain with no realistic extraction scenario, a folder is enough. No extra project, no extra ceremony.

### Layout

```
Modules/
└── GroupManagement/
    ├── GroupManagement.csproj
    ├── GroupManagementModule.cs
    ├── UseCases/
    │   ├── GetGroupDetails/
    │   └── UpdateGroupSettings/
    └── GroupConfig/                  ← just a folder, same .csproj
        └── UseCases/
            ├── GetGroupConfigSettings/
            └── SetGroupServiceSettings/
```

### Endpoint Registration

The sub-folder exposes an internal static class that the parent delegates to:

```csharp
// GroupManagement/GroupConfig/GroupConfigEndpoints.cs
internal static class GroupConfigEndpoints
{
    internal static RouteGroupBuilder MapGroupConfigEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/config", GetGroupConfigSettings.Handle);
        group.MapPut("/config/services", SetGroupServiceSettings.Handle);
        return group;
    }
}

// GroupManagement/GroupManagementModule.cs
public static IEndpointRouteBuilder MapGroupManagementEndpoints(this IEndpointRouteBuilder app)
{
    var group = app.MapGroup("/groups").WithTags("Group Management");
    group.MapGet("/{groupId}", GetGroupDetails.Handle);
    group.MapGroupConfigEndpoints();   // delegates to subfolder
    return app;
}
```

**Trade-off:** The subfolder is documentation, not a compile-time boundary. Any class can call any other class within the same project. Use this only when that's acceptable.

---

## Option C — Peer `.csproj` at the Same Level

When the sub-module is already treated as a distinct product boundary — separate team, separate release cadence — make it a peer module with an explicit one-way dependency.

```
Modules/
├── Enrollment/
│   └── Enrollment.csproj
└── Enrollment.Cobra/              ← peer, not nested
    └── Enrollment.Cobra.csproj   ← references Enrollment.Contracts
```

`Program.cs` registers both independently:

```csharp
builder.Services.AddEnrollmentModule().AddCobraModule();
app.MapEnrollmentEndpoints().MapCobraEndpoints();
```

**Trade-off:** The sibling relationship between `Enrollment` and `Enrollment.Cobra` is only signalled by the naming convention — it is not visible in the folder hierarchy. Use this when the sub-module is already operating with significant independence.

---

## Decision Table for This Project's Sub-Modules

| Sub-module | Parent Domain | Recommended Structure | Reason |
|---|---|---|---|
| **COBRA** | Enrollment | Option A — nested csproj | Federal rules, notification timelines, dedicated analyst workflow; realistic extraction candidate |
| **COB** (Coordination of Benefits) | Benefits & Coverage | Option A — nested csproj | Adjudication logic is specialized; benefits from its own assembly and test suite |
| **Referrals** | Provider & Care Network | Option A — nested csproj | Authorization workflow is distinct from provider directory lookup |
| **PCP Elections** | Provider & Care Network | Option B — subfolder to start | Promote to Option A if it develops its own lifecycle |
| **Group Config** | Group / Employer Management | Option B — subfolder | Sub-concern of group management; no independent deployment scenario |
| **CDHP Accounts** | CDHP / Health Accounts | Option B — subfolder | All CDHP operations are tightly coupled; extraction would be the whole domain, not a sub-part |

---

## Related Documents

- [domain-boundaries.md](domain-boundaries.md) — The full domain boundary analysis this design is based on
