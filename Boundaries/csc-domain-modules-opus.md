# Domain Module Analysis

A candidate decomposition for a future modular monolith, derived from the WebDE method surface, request/response classes, and entities in CSC. The goal is to surface *natural seams* — boundaries the existing system already exposes, whether the original authors intended them or not.

This document is deliberately opinionated. It proposes a synthesis rather than a menu.

---

## Approach

The analysis treats CSC as an unintentional map of the underlying business domain. Every WebDE method is a business capability that someone, somewhere, cares enough about to have paid to expose. The version history is a proxy for change frequency. The request payloads reveal how the business identifies things. The handler wiring reveals which capabilities the current authors already treated as different.

Six lenses were applied. Each carries roughly equal weight; agreement across lenses raises confidence.

### 1. Request pivot identifier (aggregate root lens)

The *first required identifier* in a request tells you what the operation is really "about" — the aggregate root the operation reads from or writes to. A `GetMemberClaimSummary` that requires `MemberID` is a query *rooted in Member*, even though it returns claim rows. That distinction matters: writes to a claim need `ClaimNumber`; writes to a member need `MemberID`; reads that answer "what claims does this member have?" are member-rooted projections.

### 2. Actor / journey

Every capability is invoked by someone. The visible actors in CSC are:
- **Member** (or a portal acting on their behalf) — reads their own coverage, submits enrollment, updates email.
- **Employer / group admin** — reads group and invoice data.
- **Producer / agent / broker** — reads commission schedule, groups managed.
- **Examiner** (internal claims processor) — pulls next claim, reads examiner messages.
- **Analyst / staff / supervisor** — internal operations.
- **Provider** — indirectly present via PCP election, provider demographic lookups.

Actor groups predict *use-case cohesion* better than entity groups do. A member portal never calls `GetNextClaim`; an examiner never calls `GetMemberIDCard` for themselves.

### 3. Transactional consistency

Writes define aggregates. Reads across aggregates are cheap; writes across aggregates are expensive. Every `Submit*` and `Set*` operation should be examined: what single aggregate does it mutate?

- `SubmitEnrollment` — mutates an enrollment transaction with member snapshots inside it → **Enrollment** aggregate.
- `SubmitClaimCrossover`, `SubmitClaimRepricing`, `SetAutoCrossover` — mutate a claim → **Claims** aggregate.
- `SetMemberPCPElection`, `SetPCPManager`, `SetPCPReferral` — mutate PCP election records for a member → **PCP** aggregate (or Member sub-aggregate).
- `SetHIPAAPermissions` — mutates authorization records for a member → **Privacy** aggregate.
- `SetMemberEComPreference`, `UpdateMemberEmailAddress`, `UpdateMemberPhones` — mutate member demographics → **Member** aggregate.
- `SetFICCBankInfo`, `SetFICCCardAuth` — mutate a member's account banking info → **Account Funding** aggregate.
- `CreateExaminerMessage`, `SetExaminerMessageAttribute` — mutate internal messages → **Examiner Workflow** aggregate.
- `SendLetterOfCoverage`, `SendMemberIDCard` — trigger correspondence emission → **Correspondence** aggregate.

Everything else is a read. Reads are the flexible layer; place them where the query is most natural even if they touch multiple aggregates via projections.

### 4. Ubiquitous language shifts

Where the vocabulary changes, a boundary probably lives. Watch for:
- **Member** vs **Employee** vs **Enrollee** — the entity project has both `Member` and `Employee`. Member is the plan participant; Employee suggests an HR/employer context. Look at where each is used before merging them.
- **Group** vs **Employer** — largely synonymous in this codebase, but `GetEmployerInvoices` and `GetEmployerPlanInformation` use "Employer" while `GetGroupDetails`, `GetGroupProductSummary` use "Group". Same aggregate, two vocabularies — one is billing/administration, the other is plan structure.
- **Producer** vs **Agent** vs **Broker** — the codebase uses all three (`GetProducerAppointments`, `GetAgentDemographicSummary`, `GetBrokersByState`). These may be distinct roles in the domain (a producer holds appointments; an agent may be an independent contractor; a broker markets group plans) or the same person seen through different lenses. Ambiguity worth resolving with a subject-matter expert before naming a module.
- **Examiner** vs **Analyst** vs **Staff** vs **Supervisor** — internal roles. Likely all live in a single "workforce" or "operations" module.

### 5. Change velocity

More versions = the contract has been reshaped more often = the surrounding logic is likely still churning. High-velocity clusters need the *most* isolation because that's where the pain of poor decomposition compounds.

| Method | Versions registered |
|---|---|
| GetMemberDemographicDetails | 14 |
| GetGroupProductBacklog | 13 |
| GetMemberAccumulators | 10 |
| GetMemberPlanProducts | 9 |
| GetClaimDetails | 8 |
| SubmitEnrollment | 8 |
| GetEmployerPlanInformation | 8 |
| GetMemberClaimSummary | 9 |
| GetNextClaim | 12 |
| GetExaminerGroupProduct | 8 |
| GetClaimDocument | 8 |
| GetStaff | 6 |

The high-velocity list is not evenly distributed — it clusters on Member core data, Coverage plan/accumulator logic, Claims processing, Group product structure, and Examiner workflow. Those clusters echo the decomposition below.

### 6. Existing seams

The current codebase already exposes two structural seams worth honoring:

- **CL_MISC WebDE account** — used exclusively for `GetGroupProductBacklog` and one `GetProductTypeBacklog` variant. The mainframe already treats these as a separate subsystem. Whatever module owns these should probably be prepared to become the first extraction target.
- **Handler-type specialization** — `EnrollmentAccesshandler` and `DocumentAccessHandler` exist because those operations have integration complexity (Base64/delimiter decoding, FTP fallback) that the standard path doesn't need. This is evidence that Enrollment and Document Retrieval have specialized adapters worth encapsulating.

---

## Proposed Decomposition

Twelve modules. Each is a candidate for a top-level directory in the modular monolith, with vertical-slice endpoints inside.

```
├── member/
├── coverage-and-benefits/
├── claims/
├── enrollment/
├── group-and-employer/
├── provider-and-pcp/
├── funding-accounts/           (CDHP + FICC)
├── authorization/              (precert, predetermination, appeals, referrals)
├── examiner-workflow/          (queue-based claim processing)
├── distribution/               (broker/agent/producer)
├── correspondence/             (documents, letters, announcements)
└── privacy-and-consent/        (HIPAA permissions)
```

Two supporting concerns that are *not* domain modules:

```
├── reference-data/             (states, countries, static codes) — shared kernel
└── platform-admin/             (DG user info, system permissions) — infrastructure
```

Rationale for each below.

---

### 1. `member/` — the plan participant record

**Aggregate root:** `Member` (identified by `MemberID` + `DepNo`).

**Owned writes:** `UpdateMemberPhones`, `UpdateMemberEmailAddress`, `SetMemberEComPreference`.

**Owned reads:** `GetMemberDemographicDetails`, `GetMemberDemographicSummary`, `GetMemberGroupInfo`, `GetMemberAltId`, `GetMemberTribalInfo`, `GetMemberIDCard`, `GetMemberIDCardVendor`, `GetMemberIDCardDocumentIDs`, `SendMemberIDCard`, `GetMembershipRelationshipSummary`, `GetMembershipTypeSummary`, `GetMemberStatementInfo`, `GetMemberEmailAddressUpdates`, `GetMemberEComPreference`, `GetTerminationInfo`.

**Why this is the anchor domain:** the demographic details operation alone has 14 versions — more than anything else. Everything the plan does eventually references a member. This module is high-churn and mission-critical, which is why isolating it is worth the effort.

**Boundary discipline:** resist the temptation to fold claim, coverage, or accumulator data *into* the Member module just because those operations take a `MemberID`. Those are cross-aggregate projections — see §"Cross-domain queries" below.

---

### 2. `coverage-and-benefits/` — what the member is entitled to

**Aggregate root:** `Coverage` (identified by member + plan-effective-period).

**Owned reads:** `GetMemberCurrentPlan`, `GetMemberPlanProducts`, `GetMemberPlanTypePeriods`, `GetBenefitServicesSummary`, `GetMemberAccumulators`, `GetMemberCOBInformation`, `GetMemberSpecialCostShareInfo`.

**Why separate from Member:** the coverage vocabulary is entirely different — plans, products, effective periods, tiers, accumulators, order-of-payment, other-insurance coverage (Medicare, Medicaid, OtherINS). And the change velocity is high (`GetMemberAccumulators` has 10 versions, `GetMemberPlanProducts` has 9). Folding this into Member would create a giant module that changes constantly for two unrelated reasons.

**Ambiguities:**
- `GetMemberReferrals` looks like it belongs here (a referral is part of what you're entitled to), but a referral is also an *authorization*. See Authorization module. Recommend keeping it here unless the referral workflow becomes significant.
- `GetEnrollmentPlanList` is superficially about coverage but is really about *what you could enroll in* — belongs in Enrollment.

---

### 3. `claims/` — a specific claim's lifecycle

**Aggregate root:** `Claim` (identified by `ClaimNumber`).

**Owned writes:** `SubmitClaimRepricing`, `SubmitClaimCrossover`, `SetAutoCrossover`.

**Owned reads:** `GetClaimDetails`, `GetClaimTracking`, `GetClaimComments`, `GetClaimDocument`, `GetClaimImages`, `GetClaimTypesSummary`.

**Cross-aggregate reads owned here for now:** `GetMemberClaimSummary`, `GetRecentClaimsSummary`, `GetMemberClaimAddress` — these are rooted in `MemberID` but return claim projections. See discussion in §"Cross-domain queries".

**Do NOT include:**
- `GetNextClaim` — this looks like a claims read, but semantically it's a work-queue pull for an examiner. It belongs in `examiner-workflow/`. The operation is not "give me this claim"; it's "pick a claim for me based on my queue filters".

---

### 4. `enrollment/` — moving a member onto a plan

**Aggregate root:** `EnrollmentTransaction` (identified by the transaction ID in `SubmitEnrollment`, not by member).

**Owned writes:** `SubmitEnrollment` (8 versions, custom `chr(252)`/`chr(253)` response decoding via `EnrollmentAccesshandler`).

**Owned reads:** `GetEnrollmentPlanList`.

**Why this is its own module:** it has genuinely different integration mechanics (the custom handler exists for a reason), its own aggregate (the transaction, not the member), and enrollment is typically a bounded workflow with a distinct team/system in health-insurance orgs. The 8 versions of `SubmitEnrollment` also signal ongoing schema churn.

**Note:** `MemberEnrollment` (the entity nested inside `SubmitEnrollment`) is *not* the same as a `Member`. It's a proposed change to member state, still in a transaction. Do not conflate them.

---

### 5. `group-and-employer/` — the paying customer's world

**Aggregate roots:** `Group` (identified by `GroupID`) and `HoldingCompany` (identified by holding-company ID).

**Owned reads:** `GetGroupDetails`, `GetGroupProductSummary`, `GetHoldingCompanies`, `GetGroupsByHoldingCompany`, `GetDivisions`, `GetDepartments`, `GetEmployerPlanInformation`, `GetEmployerInvoices`, `GetInvoiceDocument`, `GetGroupsByBroker`.

**Owned "backlog" reads (using `CL_MISC` WebDE account):** `GetGroupProductBacklog`, `GetProductTypeBacklog`.

**Language duality:** the module handles *both* the "Group" (plan-structure) and "Employer" (billing) vocabularies. These map to the same underlying aggregate but different capabilities. Keep both vocabularies in the API; don't force one.

**First candidate for future extraction:** the backlog operations already run on a separate mainframe account. If any part of this system ever spins out into its own microservice first, it will be these.

---

### 6. `provider-and-pcp/` — network directory + primary care management

**Aggregate roots:** `Provider` (identified by NPI or provider ID) and `PCPElection` (identified by member + effective period).

**Owned writes:** `SetMemberPCPElection`, `SetPCPManager`, `SetPCPReferral`.

**Owned reads:** `GetProviderDemographic`, `GetProviderTierLevel`, `GetProvPayeeInfo`, `GetNetworkProviders`, `GetMemberPCPElections`, `GetPCPLanguages`, `GetPCPSpecialities`, `GetPCPElectionAuthorizers`, `GetPCPElectionAuthorizees`.

**Boundary note:** PCP election is a *member action* (choosing a doctor) but the referral, election-authorization, and management are provider-network concerns. Bundling them keeps the write model consistent. If PCP election ever grows into its own team/workflow, it can split later.

---

### 7. `funding-accounts/` — CDHP + FICC banking

**Aggregate roots:** `CDHPAccount` and `FICCBankAccount`, both member-scoped.

**Owned writes:** `SetFICCBankInfo`, `SetFICCCardAuth`.

**Owned reads:** `GetCDHPDetails`, `GetCDHPAccountTypePeriods`, `GetCDHPContribution`, `GetCDHPBridgeDetails`, `GetFICCBankInfo`.

**Why merge CDHP and FICC:** FICC (Financial Institution Card / Contribution) has only 3 operations and is almost certainly the banking layer for HSA/FSA/HRA accounts. Splitting them creates a 3-endpoint module. The write side of FICC (`SetFICCBankInfo`, `SetFICCCardAuth`) is only meaningful for members with CDHP accounts. Merge unless subject-matter experts push back.

**Trade-off:** if the org has a separate treasury/finance team that owns FICC, keep it separate — the module boundary should reflect who does the work.

---

### 8. `authorization/` — precert, predetermination, appeals

**Aggregate roots:** `PreCert`, `Predetermination`, `Appeal` (all member-scoped).

**Owned reads:** `GetMemberPreCertSummary`, `GetMemberPreCertDetail`, `GetFamilyPredeterminations`, `GetMemberAppeals`.

**Optionally included:** `GetMemberReferrals` if referral is treated as an authorization event (recommended if a UM/utilization-management team exists).

**Why separate from Coverage or Claims:** authorization is a distinct workflow upstream of the claim event. In many health-insurance orgs it's owned by a "utilization management" team with its own vendors, SLAs, and clinical staff. The entities `MedicalReview` and `MedicalReviewStatus` in the codebase — which have no dedicated request/response pair — appear to be embedded in PreCert responses, reinforcing that this is a single clinical-review domain.

**Naming choice:** "Authorization" is the umbrella term. "Utilization Management" is the industry standard but too jargon-heavy for a module name. Pick based on the audience for the codebase.

---

### 9. `examiner-workflow/` — internal claims processing

**Aggregate roots:** `ExaminerAssignment`, `ExaminerMessage`.

**Owned writes:** `CreateExaminerMessage`, `SetExaminerMessageAttribute`.

**Owned reads:** `GetNextClaim` (12 versions), `GetExaminerMessages`, `GetExaminerGroupProduct`, `GetStaff`, `GetAnalystInfo`, `GetCallsReferrals`.

**Why this is its own module and not part of Claims:** these operations are all invoked by internal users (examiners, analysts). The `GetNextClaim` request has *no claim identifier* — it takes queue filter criteria (`OnlyHighDollar`, `OnlyPromptPay`, `OnlyRush`, `GroupProducts`, benefit codes) and returns whatever claim the queue algorithm surfaces. This is a work-queue pull, not a claim lookup.

The 12 versions of `GetNextClaim` also signal that the work-assignment logic evolves independently of the claim schema itself.

**Alternative naming:** `claims-operations/`, `back-office/`, `workforce/`. Pick based on how the internal team refers to itself.

---

### 10. `distribution/` — broker, agent, producer channel

**Aggregate roots:** `Broker`, `Agent`, `Producer` (may collapse into one).

**Owned reads:** `GetBrokerStates`, `GetBrokersByState`, `GetGroupsByBroker`, `GetAgentDemographicSummary`, `GetProducerAppointments`, `GetCommissionSchedule`.

**Language warning:** three overlapping terms (broker/agent/producer) may be distinct roles or synonyms — clarify with SMEs before finalizing. Whatever the answer, this module handles the sales distribution channel: who sells the plans, where they're licensed, how they're compensated.

**Why separate:** the actor is a business partner, not an internal employee or a member. Different auth, different UI, different regulatory constraints (state licensing, commissions). Small module, but a clean boundary.

---

### 11. `correspondence/` — everything the plan sends outward

**Aggregate roots:** `Correspondence`, `Document`, `Announcement`.

**Owned writes:** `SendLetterOfCoverage`, `SendMemberIDCard`.

**Owned reads:** `GetMemberDocument`, `GetCorrespondenceSummary`, `GetCorrespondenceTrackingInfo`, `GetAnnouncements`, `GetImage`, `DGFileTransferNotification`.

**Domain-specific document reads owned by their parent domain:** `GetClaimDocument` (Claims), `GetInvoiceDocument` (Group/Employer), `GetProvPayeeInfo` (Provider). This is the recommended split — see discussion below under "Documents: unified vs. distributed".

**Why this exists as its own module:** correspondence has emitter concerns (delivery method, tracking, FTP infrastructure, template management, delivery status) that are fundamentally different from *retrieving* an already-stored document. Even if all documents are retrieved from OneImage, the *sending* of documents is a workflow domain.

---

### 12. `privacy-and-consent/` — HIPAA authorization records

**Aggregate root:** `HIPAAAuthorization` (member-scoped).

**Owned writes:** `SetHIPAAPermissions`.

**Owned reads:** `GetHIPAAPermissionsAuthorized`, `GetHIPAAPermissionsAuthorizees`.

**Why separate from Member:** HIPAA has independent regulatory drivers, audit requirements, and typically a compliance owner. Even if it's only 3 operations today, the isolation pays off when regulations change — you don't want to redeploy the Member module every time a HIPAA form changes.

**Recommendation:** keep small but distinct. Do *not* fold `GetUserUpdatePermission` in here — that's about internal system users, not member privacy. Put it in `platform-admin/`.

---

### Supporting concerns (not domain modules)

**`reference-data/`** — `GetValidStates`, `GetCountryInformation`, `GetStaticReportCodes`, `GetPCPLanguages`, `GetPCPSpecialities`. These are lookup tables consumed by many modules. Make it a shared kernel module with no business logic — just data.

**`platform-admin/`** — `GetDGUserInfo`, `GetUserUpdatePermission`. Internal system user management, not a business capability.

---

## Cross-domain queries

The trickiest operations are the ones that read across aggregate boundaries. `GetMemberClaimSummary` is rooted in `MemberID` but returns claim rows. `GetGroupsByBroker` is rooted in broker ID but returns groups. `GetMemberDocument` is rooted in `MemberID` but returns documents.

Three valid strategies:

**A. Put the query in the module that owns the *root*.**
`GetGroupsByBroker` → `distribution/` (broker is the pivot). `GetMemberClaimSummary` → `member/` (member is the pivot). The module composes: it calls into the other module's read model, or maintains a projection.

**B. Put the query in the module that owns the *returned data*.**
`GetGroupsByBroker` → `group-and-employer/`. `GetMemberClaimSummary` → `claims/`. The module receives the filter identifier as a parameter and queries its own aggregate.

**C. Introduce a dedicated read/projection module.**
For dashboards, summaries, and reports that legitimately span multiple aggregates. Common in mature systems.

**Recommendation:** default to **B** (data-owning module) for these three operations. Rationale: the query result *is* claim data; it will change as the claim schema evolves; the module that owns claim schema changes should own the query. The filter identifier is just a lookup key. This matches the versioning signal — `GetMemberClaimSummary` has 9 versions, all driven by claim data changes, not member changes.

Save option **C** for genuine cross-cutting reports later.

---

## Documents: unified vs. distributed

This is the decomposition's most consequential structural question, called out separately because it deserves its own decision.

The current codebase has *one* concept of documents but retrieves them through several paths:
- `GetClaimDocument`, `GetInvoiceDocument` — FTP fallback via `DocumentAccessHandler`.
- `GetMemberDocument` — DG return, no FTP.
- `GetClaimImages`, `GetImage` — `OneImageBuilder` / `OneImageLoader` (1mage system, secured or not).
- `GetProvPayeeInfo` — `DocumentAccessHandler` (FTP fallback).

**Option A — one `documents/` module.** Everything document-related routes through one place. One FTP integration, one OneImage integration, consistent security. Downside: every other domain module depends on it.

**Option B — each domain owns its document endpoints.** `claims/` owns `GetClaimDocument`. `group-and-employer/` owns `GetInvoiceDocument`. Etc. Domain isolation is stronger. Downside: FTP and OneImage integration are duplicated or must live in a shared library.

**Recommendation:** **Option B, with a shared `infrastructure/documents/` library.** The domain module knows what it's fetching (claim vs. invoice vs. correspondence). The library knows *how* to fetch it (FTP, OneImage, direct). The `correspondence/` module owns only the *emitting* side — sending letters, tracking delivery, retrieving general-purpose member documents.

This preserves boundary independence for the domain modules while sharing the plumbing. It also matches the reality that `GetClaimDocument` schemas evolve independently of `GetInvoiceDocument` schemas (each has its own version history).

---

## Cross-cutting shared kernel

The following should live in a small shared kernel used by every module. They are *not* domains:

- **Identifiers & value objects:** `Address`, `Phone`, `Email`, `Country`, `State` — appear in almost every request/response.
- **Enums:** `GenderType`, `RelationshipType`, `MaritalStatusType`, `MemberStatusType`, `ClaimType`, `ClaimStatuses`, `TransferMethodType`, etc.
- **Error model:** consistent `Error`, `ErrorInformation`, `ErrorLevelType`.
- **Logging / envelope types:** the equivalent of today's `LoggingInformation`, `RecordSetInformation`, `TransactionID`/`SessionID` if you keep that model.

Keep the shared kernel *small* and *stable*. If it starts to grow features, extract them into a domain module.

---

## Seams from the source worth preserving

Three signals from the existing code that map directly to module boundaries in the new system:

1. **The `CL_MISC` account** — `GetGroupProductBacklog` and `GetProductTypeBacklog` already run on a separate mainframe account. If the new system ever needs to scale group-backlog operations independently, this is the cheapest extraction seam. Keep them in one module (`group-and-employer/`) but have their handlers/adapters isolated from the rest of that module — a sub-namespace if nothing else.

2. **The `EnrollmentAccesshandler` handler-type specialization** — enrollment has integration mechanics that no other operation has. Keep this specialization: the new `enrollment/` module should own its own submission-response decoding, not push that logic into a generic layer.

3. **The `DocumentAccessHandler` + FTP pattern** — retrieval of large binary content is genuinely different from retrieval of structured records. Isolate this in a shared `infrastructure/documents/` library as recommended above.

---

## Common pitfalls when transposing

Things to specifically *not* do when carrying the CSC design forward:

- **Do not treat versioned request classes as domain evidence.** They're WebDE contract snapshots, not aggregate designs. The right aggregate for a new system might not match the shape of `GetMemberDemographicDetails_v1_12_0`.
- **Do not carry the single-command-bus dispatch pattern forward.** It was necessary for the SOAP-facade design, but it obscures use-case-level intent. Vertical-slice endpoints per module are clearer.
- **Do not merge Member and Coverage because they share `MemberID`.** They have entirely different vocabularies and change on different clocks. Sharing a key is not the same as sharing an aggregate.
- **Do not put `GetNextClaim` in Claims because it returns a claim.** It's a queue operation. The return type is the wrong signal; the request pivot is the right one.
- **Do not build a giant Documents module.** Documents are cross-cutting infrastructure, not a domain. Own retrieval where the domain is; share the plumbing.
- **Do not defer the broker/agent/producer language decision.** These three terms appear in the codebase without clear boundaries — resolve before naming the module. It's the kind of ambiguity that gets baked into APIs and is expensive to unwind later.

---

## Summary table

| Module | Aggregate root(s) | Writes | Key evidence for the boundary |
|---|---|---|---|
| `member/` | Member | UpdateEmail, UpdatePhones, SetEComPreference | Highest-versioned operation in the system; distinct vocabulary |
| `coverage-and-benefits/` | Coverage | — (read-only) | High-churn accumulator/plan-product logic; different vocabulary from Member |
| `claims/` | Claim | SubmitClaimRepricing, SubmitClaimCrossover, SetAutoCrossover | Distinct pivot (`ClaimNumber`); large entity graph (diagnosis, procedure, service line, etc.) |
| `enrollment/` | EnrollmentTransaction | SubmitEnrollment | Custom handler type; distinct aggregate (transaction, not member) |
| `group-and-employer/` | Group, HoldingCompany | — (read-only) | Separate WebDE account for backlog ops; dual vocabulary handled here |
| `provider-and-pcp/` | Provider, PCPElection | SetMemberPCPElection, SetPCPManager, SetPCPReferral | Actor is provider-network; PCP writes are cohesive |
| `funding-accounts/` | CDHPAccount, FICCBankAccount | SetFICCBankInfo, SetFICCCardAuth | Product type distinct from medical; typically finance-owned |
| `authorization/` | PreCert, Predetermination, Appeal | — (read-only in CSC) | Clinical workflow upstream of claims; distinct team in most orgs |
| `examiner-workflow/` | ExaminerAssignment, ExaminerMessage | CreateExaminerMessage, SetExaminerMessageAttribute | Actor is internal; queue-pull semantics for GetNextClaim |
| `distribution/` | Broker, Agent, Producer | — (read-only) | Sales channel actor; regulatory constraints differ |
| `correspondence/` | Correspondence, Announcement | SendLetterOfCoverage, SendMemberIDCard | Emitter workflow; distinct from document retrieval |
| `privacy-and-consent/` | HIPAAAuthorization | SetHIPAAPermissions | Independent regulatory driver |

Two supporting concerns: `reference-data/` (shared kernel), `platform-admin/` (infra).

---

## Next steps

1. **Validate actor list with SMEs.** Confirm who calls each capability today. That resolves the broker/agent/producer question and the member-facing vs. examiner-facing split.
2. **Resolve the referral ownership question.** Is `GetMemberReferrals` a coverage read or an authorization read? Ask utilization management.
3. **Confirm FICC/CDHP relationship.** Are FICC banking accounts always tied to CDHP, or do they exist standalone? Determines whether to merge or split module 7.
4. **Sketch the shared kernel.** Enumerate the ~15 value objects and enums that must be shared. Anything larger belongs in a domain module.
5. **Pick a first module.** Start with a small, clear one — `privacy-and-consent/` or `distribution/` — to prove the vertical-slice pattern before tackling `claims/` or `member/`.
