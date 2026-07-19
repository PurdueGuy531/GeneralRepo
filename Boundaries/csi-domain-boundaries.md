# Domain-Boundary Analysis

Analysis of CSIWeb's backend logic and page structure to identify candidate **domain modules** for a future modular monolith. Each proposed module is a bounded context that would own its data, its endpoints (vertical slices), and could later be extracted as an independent service.

This is a starting point for discussion — not a prescription. Where reasonable people would split differently, that's called out explicitly.

## What the codebase tells us before we decide anything

The current app is organized by **UI area** in `Pages/App/`, but that's a workflow grouping — not a domain grouping. The stronger signal comes from three places:

| Signal | Weight | Why |
|---|---|---|
| **Backend service interfaces** (`~25`) | Strong | Each is a business capability, named in the org's ubiquitous language (`Constituent`, `Accumulator`, `Subrogation`, `Election`, etc.) |
| **DbContexts / databases** (4) | Strong | Data-store boundaries reflect real ownership: CRM_Logging (this app's data), IVR (telephony), Portal_MC / Portal_ServProv (member-portal config, an entirely separate consumer) |
| **Upstream SOAP services** (16) | Medium | Grouping reflects the DG platform's own boundaries, not necessarily the right seams for a new service |
| **Ubiquitous language** in Models/Entities | Strong | `Group`, `Constituent`, `Claim`, `Coverage`, `Accumulator`, `Precert`, `Subrogation`, `Referral`, `Election` are terms the business already uses |
| **`Pages/App/` folders** | Weak | Two of the seven (`Request/`, `Review/`) are pure workflow buckets that touch 4–5 different domains each — do not use these as module boundaries |

### Two folders that are traps if you follow them

- **`Pages/App/Request/`** contains: *AccountLetter, BillingPaymentsRates, ClaimDetailReport, CoverageLetter, EmployerLetters, EOB, ManageAdjustment, ManageReferral, MemberRegistrationEmail, PredeterminationLetter, RequestAddressPage, RequestIDCard, ShowIDCard*. Every one of these is a **command operation on a domain that lives elsewhere** (correspondence, claims, medical mgmt, portal registration, ID card). Requesting an ID card is not its own domain; it's a command on the Member/IdCard domain.

- **`Pages/App/Review/`** contains: *AdjustmentSummaryDetails, CallReferralSummaryDetails, DentalReviewSummaryDetails, ManagedCare, MedicalReferrals, Misrep, MRVSummaryDetails, Overpayments, PreCertificationSummaryDetails, PreExisting, ReinsuranceSummaryDetails, ReviewProviderNetwork, Subrogation, UsualAndCustomary*. These are the **query views** for the same domains — mostly claim events and medical-management decisions.

Reading `Request` and `Review` together tells you what the actual domains are. They just got sliced by verb (do/see) rather than by noun in the current UI.

---

## Recommended default: 8 domain modules + 2 platform modules

This is a coarse starting point. Each row lists the current source elements that would land in that module.

### 1. Member & Person

**Answers**: "Who is this person?"

- Backend: `IConstituent`, `IConstituentSearch`, `IMemberHistory`, `IIdCard`, `IOrgChart`
- SOAP: `ConstituentServices`, `ConstituentSearchServices`, `IdCardServices`, `OrgChartServices`
- Models: Constituent, MemberHistory, IdCard, OrgChart
- Also owns: address changes, phone/email updates, direct-deposit updates, ID-card issuance (both `RequestIDCard` and `ShowIDCard`)

**Why together**: Everything else references a member. This is the aggregate root of the customer domain. IdCard is a member-identity artifact so it belongs here rather than in Correspondence. OrgChart is member-position within their employer, so it's an attribute of the person.

### 2. Coverage & Eligibility

**Answers**: "Is this person covered, when, and by what plan?"

- Backend: `IEligibility`, `IBusinessGroup`
- SOAP: `EligibilityServices`, `BusinessGroupServices`
- UI: entire `Pages/App/Eligibility/*` (Cobra, CoverageHistory, GroupCoverage, OtherDental, OtherMedical, PCP), plus `Pages/App/Request/CoverageLetter.cshtml`
- Models: Eligibility, BusinessGroup

**Why together**: Coverage is scoped to a plan (BusinessGroup). Coverage history, COBRA continuation, PCP elections, and "does this member have other dental/medical coverage" are all facets of the same coverage-record aggregate.

### 3. Benefits

**Answers**: "What does the plan cover, and how much has been used?"

- Backend: `IAccumulator`
- SOAP: `AccumulatorServices`
- UI: `Pages/App/Benefits/*` (Accumulators, CostEstimatorTool, Details), plus `Pages/App/Request/IVRWebBenefitSummary.cshtml`
- Models: Accumulator

**Why separate from Coverage**: Coverage answers whether coverage exists; Benefits answers what the plan pays and how much of the deductible/OOP is consumed. Different concepts, different consumers (Coverage is asked by intake/eligibility staff; Benefits by CSRs quoting a member's remaining deductible).

### 4. Claims & Financial Adjudication

**Answers**: "What happened with this claim / bill?"

- Backend: `IClaim`
- SOAP: `ClaimServices`
- UI: `Pages/App/Claims/*` (SearchAndDetail, Letters, MailMenu, MonthlyOrthoSchedule); from `Request/`: EOB, ClaimDetailReport, ManageAdjustment, PredeterminationLetter, BillingPaymentsRates; from `Review/`: AdjustmentSummaryDetails, Overpayments, ReinsuranceSummaryDetails
- Models: Claim

**Why together**: Adjustments, EOBs, predeterminations, reinsurance, overpayments, and orthodontia payment schedules are all claim-lifecycle financial events. They share the same claim aggregate and are all reads/writes against that ledger.

### 5. Medical Management & Adjudication Review

**Answers**: "What clinical/policy decisions apply to this member/claim?"

- Backend: `IMedicalReview`, `ISubrogation`
- SOAP: `MedicalReviewServices`, `SubrogationServices`
- UI: from `Review/`: MedicalReferrals, PreCertificationSummaryDetails, PreExisting, ManagedCare, Misrep, MRVSummaryDetails, DentalReviewSummaryDetails, CallReferralSummaryDetails, UsualAndCustomary, Subrogation; from `Request/`: ManageReferral
- Models: MedicalReview, Subrogation

**Why together**: All are non-financial adjudication decisions about a member/claim — pre-certifications, referrals, managed-care rulings, pre-existing exclusions, misrepresentation flags, usual-and-customary pricing rulings, subrogation cases. If you'd rather split, see [Debatable seams](#debatable-seams) below.

### 6. Provider & Network

**Answers**: "Which doctors/facilities are in-network for this plan?"

- Backend: `IPPO`
- SOAP: `PPOServices`
- UI: `Pages/App/Network/NetworkSearchDetails`, `Pages/App/Review/ReviewProviderNetwork`
- Models: PPO

**Why separate**: Providers are an independent directory. Claims and Medical Management _reference_ providers but do not own them. This module is small today but rarely stays small — you'll be adding fee schedules, credentialing state, etc.

### 7. Correspondence & Communications

**Answers**: "What did we say to (or about) this member, and how do they want to hear from us?"

- Backend: `ICommunication`, `IMemberAccountLetter`, `IElection`, `IDocument`, `IImage`
- SOAP: `CommunicationServices`, `DocumentServices`, `ImageServices`
- REST: MemberAccountLetter (mTLS), Election (mTLS)
- UI: `Pages/App/Communications/*` (Announcements, Elections, EligibilityComments, Flags, LitigationAnnouncements, MemberAlerts), plus `Pages/App/Request/*` letter-generating pages (AccountLetter, CoverageLetter, EmployerLetters, MemberRegistrationEmail)
- Models: Communication, Election, MemberAccountLetter, Document, Image

**Why together**: This is heterogeneous but all outbound-to-member. Letters, EOBs, ID-card PDFs, elections (paperless vs mail), announcements, flags, comments, alerts. Document/Image are content assets these letters embed. See [Debatable seams](#debatable-seams) — this is the module most likely to want splitting.

### 8. Portal Registration & Audit

**Answers**: "Is this member enrolled in the self-service portal, and who's touched their registration?"

- Backend: `IMemberRegistrationCheck`, `IMemRegSSOGroupCheck`, `IMemberRegistrationAudit`, `IMemRegSettings`, `ICETCheck`
- Databases: **PortalMCDBContext**, **PortalSerProvDBContext** (portal DBs — not CRM_Logging)
- UI: `Pages/App/Request/MemberRegistrationEmail*.cshtml`
- Models: MemberRegistrationAudit, MemberRegistrationSettings

**Why separate from Member**: This is a different lifecycle (portal enrollment) with a different consumer (the member self-service portal), different data stores (Portal DBs), and different audit requirements. The Member module owns _who_ the member is; this module owns _their portal identity_.

---

### Platform modules (not domain modules)

### 9. Call Center Operations (supporting subdomain)

- Backend: `ICallSession`, `ICallLog`, `IIvrCallInfo`, `IIvrCallInfoRest`
- Databases: EFDbContext (CallSession, MemberCallHistory), **IVRDbContext** (Call, Event, AgentTracking)
- Models: CallSession, CallLog

Every user interaction happens **inside** a CallSession. This is the operational envelope the other 8 modules are consumed from — not a peer domain. Depending on what you're building, this might not exist at all in the new service, or it might become a first-class module if you're rebuilding the CSR tool.

### 10. Identity & Access (infrastructure)

- Backend: `IAuthentication`, `ICertificateManager`
- Concerns: cookie auth, roles/policies, outbound mTLS certs, DataProtection keys

Cross-cutting. Not a domain module.

---

## Module summary table

| # | Module | Owns (representative) | Data stores | Split risk |
|---|---|---|---|---|
| 1 | Member & Person | Constituent, MemberHistory, IdCard, OrgChart | member master (new) | Low |
| 2 | Coverage & Eligibility | Eligibility, COBRA, PCP, BusinessGroup | coverage/plan (new) | Medium — could absorb Benefits |
| 3 | Benefits | Accumulator, plan design, cost estimator | benefits (new) | Medium — could merge into Coverage |
| 4 | Claims & Financial Adjudication | Claim, EOB, Adjustment, Predetermination, Reinsurance, Overpayments | claims ledger (new) | Low |
| 5 | Medical Management | MedicalReview, PreCert, Referrals, Misrep, PreExisting, ManagedCare, U&C, Subrogation | med-mgmt (new) | High — see seams |
| 6 | Provider & Network | PPO / network directory | provider directory (new) | Low |
| 7 | Correspondence & Communications | Communication, MemberAccountLetter, Election, Document, Image, Announcements, Flags, Alerts | correspondence + content (new) | High — see seams |
| 8 | Portal Registration & Audit | MemberRegistrationCheck/SSO/Audit/Settings, CET | Portal_MC, Portal_ServProv | Low |
| 9 | Call Center Operations | CallSession, CallLog, IvrCallInfo | CRM_Logging, IVR | N/A — supporting |
| 10 | Identity & Access | Auth, cookie policy, certificates | — | N/A — infra |

---

## Debatable seams

Places where the boundary was a judgment call. Worth deciding intentionally.

### A. Benefits + Coverage as one "Plan & Coverage" module

- **Argument for merging**: BusinessGroup (plan design), Eligibility (coverage instance), and Accumulator (usage) are tightly coupled — you almost always need all three to answer a member's benefits question.
- **Argument for splitting** (the default here): Different write-throughput profiles. Coverage changes rarely (open enrollment, life events); Accumulators change every claim. Different query patterns.

### B. Medical Management: one module vs several

Split candidates within module 5:

- **Adjudication Review** — precert, referrals, medical review, pre-existing, misrep, managed care
- **Subrogation** — its own module; different lifecycle (long-running case management, external correspondence with attorneys/insurers)
- **Pricing** — usual-and-customary; arguably belongs with Claims

**Recommendation**: start unified; split Subrogation off first if it grows independent workflows.

### C. Claims and Medical Management as one big "Adjudication" module

- **Argument for merging**: In reality claims and medical decisions are intertwined — a precert decision drives claim payment; a misrep finding triggers overpayment recovery.
- **Argument for splitting** (the default here): Claims is a financial ledger; Medical Management is decisions/policy. Different data shapes, different audit requirements, different regulators (state vs medical policy).

### D. Correspondence subdivision

Module 7 is the widest and most likely to grow apart. Reasonable splits:

- **Correspondence** — letters, EOBs, PDFs, mail/email generation, Document/Image assets
- **Preferences** — Elections (paperless, email vs mail)
- **Member Annotations** — Flags, Alerts, Comments, EligibilityComments (per-member CSR-facing notes)
- **Broadcast** — Announcements, LitigationAnnouncements (broadcast to groups, not per-member)

Each has a different domain owner and different write pattern. **This module is the strongest candidate for eventual microservice split.**

### E. Provider / Network as its own module vs shared

If the new service will rarely touch provider data directly and only references it inside Claims/Network-lookups, Provider could live as a small shared kernel. If you'll build any provider-facing tooling, it earns its own module.

---

## Cross-cutting concerns to keep out of domain modules

These will bleed across every module. Treat them as platform / shared kernel, not domain:

- **Document/Image asset storage** — letters, ID card PDFs, EOB PDFs, member photos. Currently sitting in Correspondence, but the storage/retrieval mechanism is one concern; the *authoring* of a letter is a Correspondence concern. Consider a shared "Content" service.
- **Audit logging** — MemberRegistrationAudit is one visible instance; every domain will need something similar. Bake into platform.
- **Group/Employer metadata** — every domain scopes by group number. Consider whether BusinessGroup is really "part of Coverage" or a shared reference dimension used by many modules.
- **CallSession context** — if you keep it, it's a request-envelope, not a domain object. Threaded via correlation ID.

---

## Recommended starting shortlist

If you want a smaller starting point to react to, six modules covers the space and lets pain reveal the rest:

1. **Member** — identity, demographics, IdCard, OrgChart
2. **Coverage** — fold BusinessGroup + Benefits/Accumulator in here initially
3. **Claims** — fold Medical Management in for now
4. **Provider** — small but distinct
5. **Correspondence** — all outbound + preferences + annotations
6. **Portal Registration** — clearly separate (different DB, different consumer)

Split Benefits, Medical Management, and Correspondence-subdomains off later once you feel the seam. Six is small enough to keep the module boundaries interesting instead of ceremonial, and each already has a clear real-world owner in the business.

---

## How to use this document

- Treat it as a **candidate list**, not a decision.
- Before finalizing, walk through 3–5 real use cases end-to-end (e.g. "member calls asking why an EOB was denied") and see which modules each hits. If a use case hits 6 modules to answer one question, some of those boundaries are wrong.
- Use the **Debatable seams** section as your decision log — pick one side per seam and record why.
- Cross-cutting concerns above should be modeled as platform capabilities before Module 1 is written, not bolted on later.
