# Domain Boundary Analysis (Alternative Take)

This is a second-pass analysis of the same source material, organized around **aggregate roots** and **lifecycle phases** rather than mirroring the current WCF service layout. The goal is to identify boundaries that would survive as the system grows, not boundaries that reflect how the Silverlight UI happened to be sliced.

The analysis leans hardest on two signals:
- **What data changes together in a transaction** (consistency boundaries)
- **What changes for the same reason** (change-cadence boundaries)

Secondary signals: DG className clusters, cross-service dependencies, and shared DataContract types.

---

## Framing: The Current Services Are UI-Shaped, Not Domain-Shaped

Before proposing boundaries, it helps to name what the current 21 WCF services actually are. They are not domain services. They are **screen-backed service facades** — each one exists to feed a Silverlight view. That's why:

- `CommunicationService` is a grab bag of "things to show above the fold" (alerts, restrictions, litigation) that share a UI region but no domain logic.
- `ConstituentSearchService` mixes member search, provider search, employer search, and contact search — because they're all "search boxes."
- `DocumentService` has 28 operations because "documents" is a UI tab, not a bounded context.
- `EligibilityService` reaches into `CSI:MedicalReview` for precertification because precert data appears on the eligibility screen.

Domain modeling should ignore this. The modules below are proposed as they would be if we were designing greenfield with only the backend `className` clusters and the operational semantics as evidence.

---

## Aggregate Roots (Draft)

Aggregate roots are the entities the system fundamentally exists to manage. Every operation is either a command against one of these or a query returning one or a projection of one.

| Aggregate | Identifier | Owns | Notes |
|---|---|---|---|
| **Member** | Alt ID / SSN / internal ID | Demographics, contact info, PCP election, direct deposit | Root of most read paths |
| **Coverage** | Member ID + effective date range | Plan enrollment, COBRA state, PREX, other insurance | Time-scoped facts about a member |
| **Group** | Group Broker ID | Employer, plan design, contacts (agent/broker/vendor) | Independent lifecycle from members |
| **Claim** | Claim number | Claim lines, tracking status, checks, adjustments | Financial transaction record |
| **Review Case** | Review case ID | UM decision workflow, letters, status history | Precert, dental review, preex verification |
| **Interaction** | Log ID | Call log, referral, admin log | CSR touchpoint record |
| **Notice** | Notice ID + member scope | Alerts, restrictions, litigation, authorizations | Read-mostly attention markers |
| **Document** | DCN / doc ID | Generated letter or retrieved image | Artifact, not workflow |
| **Provider** | Provider TIN / NPI | Network affiliation, tier, specialty | Directory reference |
| **Reference Value** | Type + code | Lookup tables, code lists, feature flags | Shared kernel |

Nine of these become module boundaries. Reference Value becomes shared kernel.

---

## Proposed Modules

### Module A — Member Identity

**Aggregate:** Member.

**Owns write path for:** Contact information, email, phone, direct deposit, member-to-alt-ID resolution.

**Owns read path for:** Member demographics, member search (by ID and by attribute), family relationships, dependent enumeration.

**Evidence:**
- `CSI:MemberIDSearchQuery`, `CSI:NoMemberIDSearchQuery` are dedicated identity-search backend modules
- `CSI:MemberInformation` operations on this aggregate: `ContactInfoUpdate`, `DirectDepositInformation`
- CSCService is the source-of-truth for the identity subset: `GetMemberDemographicDetails`, `UpdateMemberEmailAddress`, `UpdateMemberPhones`, `GetMemberAltId`
- SSN encryption applied on search response — PII isolation is a design signal

**What lives here that doesn't today:** Nothing new. Extraction from current `ConstituentService` + `ConstituentSearchService` (member search subset) + `AuthenticationService`.

**What leaves today's boundary:** Provider search leaves for the Provider module; employer/contact search leaves for the Group module.

---

### Module B — Coverage

**Aggregate:** Coverage.

**Owns write path for:** PCP election, other-insurance coordination.

**Owns read path for:** Member/family/dependent/group coverage, COBRA summary, PREX, misrepresentation tracking, benefit-plan accumulators (deductible/OOP).

**Evidence:**
- `CSI:MemberInformation` coverage subset: `CoverageTypeInformation`, `FamilyCoverageOutput`, `MedicalCoverageOutput`, `OtherDentalMedicalCoverage`, `OtherDentalMedicalInsurance`, `CobraSummary`, `PREXSummary`, `ContactsDetail`, `BenefitsPlanDollarAccumulator`, `ElectPCPCSI`
- `CSI:MisRepTracking` — misrepresentation is a coverage-integrity concept
- `CSI:GetPersistentData.CoverageOutput` — coverage-specific reference data
- Accumulator is a materialized view of coverage financial state; it doesn't warrant its own module

**Why Coverage is separate from Member Identity:** Coverage is time-scoped and event-driven (enrollment/termination/change events); Member Identity is comparatively stable. They have different change cadences and different consistency requirements.

**Rejects the current "Accumulator = own service" split.** Accumulators are a coverage read; they don't own state independently.

---

### Module C — Group & Plan Sponsor

**Aggregate:** Group.

**Owns write path for:** (Read-only in current codebase.)

**Owns read path for:** Employer verification, group contacts, agent/broker relationships, vendor/consultant relationships, group plan documents, employer invoices, group information used to contextualize eligibility.

**Evidence:**
- `CSI:EmployerInformation` is a coherent backend module with clear boundaries
- `CSI:GetPersistentData.GroupInformationOutput` — the current `BusinessGroupService` is one thin call and belongs here
- Employer contact / agent-broker / vendor-consultant searches all share this className
- Group plan documents and employer invoices are group-scoped and should not live in a document module (see boundary discussion below)

**Why separate from Member:** Groups have a completely different lifecycle, different user personas (account managers vs. CSRs), and different data source patterns. Merging them would create the same anti-pattern that led to `CSI:MemberInformation` becoming a 6-service backend god-class.

---

### Module D — Claims

**Aggregate:** Claim.

**Owns write path for:** Adjustment requests (new + update).

**Owns read path for:** Claim search (all variants), claim detail, tracking, checks paid, orthodontic monthly summaries, benefit code limits, EDI indicators, subrogation cases.

**Evidence:**
- `CSI:ClaimsData` and `CSI:ClaimsDataQuery` are dedicated claim backend modules
- Subrogation (`CSI:SubrogationSummaryQuery` + `SubrogationDetails`/`SubrogationNotes`/`SubrogationSearch` on `CSI:ClaimsData`) has no independent aggregate — a subrogation case only exists because a claim exists
- Orthodontic operations (`GetMonthlyOrthoSummary`, `GetMonthlyOrthoDetail`) are claim variants
- Benefit code limit and EDI operations are claim-search refinements, not their own concern

**Deliberate exclusions from Claims:**
- **Medical referrals** — currently in `ClaimService` but call `CSI:MemberInformation.GetReferralsForMemberCSI`. Not claim-owned. Move to Utilization Management.
- **Overpayment & reinsurance** — currently in `MedicalReviewService` but call `CSI:ClaimsData`. These are post-adjudication financial workflows. Reasonable argument to keep in Claims or split as sub-module.

**Recommendation on overpayment/reinsurance:** Keep in Claims as internal sub-features. They're claim-scoped financial adjustments; grouping them with clinical UM decisions confuses two different workflows.

---

### Module E — Utilization Management

**Aggregate:** Review Case.

**Owns write path for:** Preexisting condition verification creation, PCP election (contested — could argue Coverage).

**Owns read path for:** Medical review, dental review, preex verification, precertification, usual & customary, procedure code modifiers, medical referrals, mail information tied to a review case.

**Evidence:**
- `CSI:MedicalReview` is a dedicated backend module
- Precertification and U&C are currently in `EligibilityService` but call `CSI:MedicalReview` — a clear misplacement
- Medical referrals (`GetReferralsForMemberCSI`) belong here for the same reason
- `CSI:MailMenu.GetMailInformation` is UM correspondence tracking

**Split decision — Overpayment/Reinsurance:** The v1 analysis put these here. I disagree. They use `CSI:ClaimsData` and represent claim-financial workflows, not clinical review. Placing them here to preserve the current `MedicalReviewService` grouping would encode a UI convenience as a domain fact. Move them to Claims.

---

### Module F — Interactions (CSR Case Notes)

**Aggregate:** Interaction.

**Owns write path for:** Phone log, admin log, call referral, CRM member call history.

**Owns read path for:** Call log history, admin log summary/detail, referral summary, call type reference lists.

**Evidence:**
- `CSI:PhoneLog` is a dedicated backend module
- `CSI:CallReferralQuery` is scoped to referral search
- The SQL `CRM_Logging.MemberCallHistory` is the same aggregate stored elsewhere — it exists because the UniData model didn't support the specific query pattern needed. This is a data-source split, not a domain split.

**Consolidation note:** In the current design, `CallLogService` and `MemberCallHistoryService` are separate WCF services because they use different backing stores. In a new design they should be one module; the storage split can be an internal implementation detail (or the SQL table can be retired).

---

### Module G — Notices

**Aggregate:** Notice.

**Owns write path for:** (Read-only in current codebase.)

**Owns read path for:** Member alerts, authorizations, confidential communications, restrictions, legal announcements, litigation announcements, family flags, eligibility comments.

**Evidence:**
- `CSI:Announce` is a dedicated backend module with tightly cohesive operations
- `CSI:MemberInformation.FamilyFlags` and `EligibilityComments` are notice-shaped (attention markers displayed to a CSR)
- Every operation in this module is a read; there's no CSR-driven write path here

**Naming note:** The current `CommunicationService` name is misleading. This module has nothing to do with sending communications — that's Correspondence (Module H). Renaming to Notices or Alerts is a clarity win.

**Merge candidate:** If Notices stays read-only and small, folding it into Member Identity is defensible. Keep separate if the roadmap includes CSR-driven suppression/preference/write operations, or if event-sourcing alerts is on the horizon.

---

### Module H — Correspondence Generation

**Aggregate:** Document (generated).

**Owns write path for:** Sending letters (display letters, claim letters, coverage letters, predetermination, COCC, address labels), sending ID cards, sending EOB/claim detail reports.

**Owns read path for:** Display letter search, claim letter search, correspondence summary and tracking.

**Evidence:**
- `CSI:Letters`, `CSI:LettersQuery` are dedicated backend modules
- ID card delivery (`IdCardService` via RTI REST) is a document generation and delivery operation, not a distinct domain
- Send operations dominate: this is a **command-side** module

**Split decision — this vs. Document Retrieval:** I split these into two modules where the v1 analysis kept them together. Generation and retrieval have different failure modes, different scaling profiles, different infrastructure (SMTP/print vs. object storage), and different consistency requirements. See Module I.

---

### Module I — Document Retrieval

**Aggregate:** Document (stored).

**Owns write path for:** None.

**Owns read path for:** EOB PDFs, claim image EDI, ECHO documents, invoice documents, plan documents, plan summary XLS, imaging (OneMage), employer invoice retrieval, correspondence tracking retrieval, HealthSparq SSO handoff.

**Evidence:**
- The retrieval operations are almost entirely CSCService/RTI proxies with no domain logic — they're a fetch layer
- Different infrastructure: RTI REST + OneMage + CSCService, not UniData
- The RTI OAuth2 token cache lives here and is specific to retrieval

**Why split from Correspondence Generation:**
- Retrieval is idempotent and cacheable; generation is not
- Retrieval failure modes are 404 / timeout / auth-expiry; generation failure modes are print-queue / SMTP / template errors
- Retrieval is a candidate for a thin CDN-style caching layer in a future microservice split; generation is not

**Merge alternative:** If the team is small and unlikely to grow either side independently, keep Modules H and I as a single Correspondence & Documents module with internal read/write sub-packages. The split earns its keep only if the two sides evolve separately.

---

### Module J — Provider Directory

**Aggregate:** Provider.

**Owns read path for:** PPO network search, PPO network provider search, PPO codes, PPO lookup, provider profile search, provider tier level.

**Evidence:**
- `CSI:PPOData`, `CSI:PPOOTHQuery` are dedicated backend modules
- `GIS:LookupQueries.PPOLookup` uses a different backend system prefix entirely — signaling provider network data may sit in a distinct upstream data source
- Provider search currently in `ConstituentSearchService.ProviderSearch` uses `CSI:PPOData.ProviderProfileSearch` — belongs here

**Boundary decision:** Provider tier level (CSCService) belongs here even though it's fetched member-in-context, because the fact "provider X is tier 2 for group Y" is a provider directory fact, not a member fact.

---

### Shared Kernel — Reference Data

**Not a module.** A shared library or platform-level service.

**Rationale:** `CSI:GetPersistentData.ReferenceDataRetrieval` is called by 8+ current services with a `ReferenceType` discriminator returning: call categories, complaint codes, department lists, place-of-service codes, revenue codes, claim status options, adjustment reason codes, examiner codes, check cycles, fax cover options. Plus `ClientConfigurationService`, `PackageService`, `UtilityService.GetConfig`.

If reference data becomes its own module, every other module depends on it, and it becomes a hidden hub. Better outcomes:
- **Static reference data** (place-of-service, revenue codes): compile-time or embedded resource per consuming module
- **Semi-static reference data** (department lists, feature flags): a lightweight shared library with a cache-ahead-of-time load
- **Truly dynamic reference data** (rare): a small internal service consumed via a client library

`OrgChartService`, `ClientConfigurationService`, `PackageService`, and the reference-data operations currently embedded in `ClaimService`/`CallLogService`/`EligibilityService` all resolve into the shared kernel.

---

### Platform / Infrastructure

**Not a domain module.** These operations are cross-cutting concerns:
- `UtilityService.ReportIssue` (email alerting)
- `UtilityService.CreateError`, `CreateWarning` (client-error logging)
- `UtilityService.GetConfig` (config exposure — likely remove entirely)
- `SecurityService.GetMachineName`, `GetServerDns`, `GetHostInformation`, `GetAssemblyInformation` (deployment diagnostics)
- `SecurityService.GetUserInformation`, `GetMemberRoles` (AuthN/AuthZ)

The security operations belong in an authentication/authorization concern (or in Module A if identity extends to authenticated users). The rest belong in platform/observability tooling, not in any domain module.

---

## Consolidated Module Map

| # | Module | Aggregate | Type | Current WCF Services Consumed |
|---|---|---|---|---|
| A | Member Identity | Member | Read + Write | ConstituentService, ConstituentSearchService (member subset), AuthenticationService |
| B | Coverage | Coverage | Read + Write | EligibilityService, AccumulatorService, BusinessGroupService |
| C | Group & Plan Sponsor | Group | Read (mostly) | ConstituentSearchService (employer/contact subset), DocumentService (group docs/invoices subset) |
| D | Claims | Claim | Read + Write | ClaimService, SubrogationService, MedicalReviewService (overpayment/reinsurance subset) |
| E | Utilization Management | Review Case | Read + Write | MedicalReviewService (UM subset), EligibilityService (precert/U&C/referrals subset) |
| F | Interactions | Interaction | Read + Write | CallLogService, MemberCallHistoryService |
| G | Notices | Notice | Read | CommunicationService |
| H | Correspondence Generation | Document (generated) | Write-heavy | DocumentService (send subset), IdCardService |
| I | Document Retrieval | Document (stored) | Read | DocumentService (fetch subset), ImageService |
| J | Provider Directory | Provider | Read | PPOService, ConstituentSearchService (provider subset) |
| — | Reference Data | — | Shared kernel | OrgChartService, ClientConfigurationService, PackageService, UtilityService, embedded lookups |
| — | Platform | — | Cross-cutting | UtilityService (issue/error), SecurityService |

---

## Boundary Tensions & Recommendations

### Overpayment and reinsurance: Claims, not UM
Currently in `MedicalReviewService`. Use `CSI:ClaimsData`. They are post-payment financial workflows. **Placement:** Module D (Claims), as a sub-feature.

### Precertification, U&C, and medical referrals: UM, not Claims/Eligibility
Currently scattered across `EligibilityService` (precert, U&C) and `ClaimService` (referrals). All use `CSI:MedicalReview` or `CSI:MemberInformation.GetReferralsForMemberCSI`. **Placement:** Module E (UM).

### Accumulator: Coverage, not its own module
The current `AccumulatorService` exists as a single-operation service because there's a screen for it. Accumulators are a computed view of coverage state. **Placement:** Module B (Coverage), as a read model.

### Business Group: Group, not its own module
The current `BusinessGroupService` wraps one call. **Placement:** Module C (Group), absorbed.

### Provider search: Provider Directory, not Constituent Search
Currently in `ConstituentSearchService` because it's a search-shaped UI. **Placement:** Module J (Provider Directory).

### Employer/contact search: Group, not Constituent Search
Same reasoning. **Placement:** Module C (Group).

### Document module: split by read vs. write, not merged
The current `DocumentService` is 28 operations and heterogeneous. Split into Modules H (generation) and I (retrieval). If team size doesn't warrant it, keep as one module with two internal packages. Don't ship as one flat surface.

### Communication → Notices rename
The current `CommunicationService` doesn't send anything. **Rename** to Notices or Alerts on extraction.

### MemberCallHistory: fold into Interactions
Storage split (SQL vs. UniData) is not a domain split. **Placement:** Module F (Interactions).

### Reference data: shared kernel, not module
Making it a module creates a hidden hub every module depends on. **Placement:** shared library with per-module caches.

---

## What This Analysis Does *Not* Recommend

- **A "Search" module.** Search is a query pattern across many aggregates, not a domain.
- **A "CSC integration" module.** CSCService is an infrastructure dependency, not a bounded context. Each domain module calls CSC directly for the subset it needs, encapsulated behind that module's repository/gateway.
- **A "Notifications" module distinct from Notices and Correspondence.** These are separate concerns: Notices = read state markers, Correspondence Generation = outbound documents. There is no third thing.
- **Splitting Claims by product line (medical/dental/pharmacy).** The current codebase uses the same `CSI:ClaimsData` backend for all products. Product line is a claim attribute, not a domain boundary.

---

## Sequencing for Extraction

If this analysis is used to guide an incremental extraction from the WCF service into a modular monolith, an order that minimizes cross-module churn:

1. **Reference Data** first — extract as a shared library; unblocks everything else.
2. **Platform/Infrastructure** (auth, logging, config, error reporting) — foundational.
3. **Member Identity** and **Coverage** — the aggregate everything else references.
4. **Group** — has few inbound dependencies from other modules.
5. **Provider Directory** — mostly independent read model.
6. **Notices** — read-only, isolated.
7. **Interactions** — self-contained; introduces the first write path.
8. **Claims** — depends on Member, Coverage, Provider.
9. **Utilization Management** — depends on Member, Coverage, Claims.
10. **Correspondence Generation** and **Document Retrieval** — depend on almost everything; do last.
