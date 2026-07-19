# Domain Boundary Analysis

This document maps the WebDE method names, request/response classes, and entity types in CSC to candidate domain boundaries for a future modular monolith. The goal is to identify natural groupings, flag ambiguous operations, and surface tensions where the evidence is mixed.

The analysis is based entirely on what the existing system already implies — naming, operation counts, entity relationships, the separate WebDE accounts (CL_CSC vs CL_MISC), and which handler types are used.

---

## Proposed Domains — Summary

| # | Domain | Operation count (distinct methods) | Confidence |
|---|---|---|---|
| 1 | [Member](#1-member) | ~20 | High |
| 2 | [Claims](#2-claims) | ~12 | High |
| 3 | [Coverage & Benefits](#3-coverage--benefits) | ~8 | High |
| 4 | [Group & Employer](#4-group--employer) | ~12 | High |
| 5 | [Provider & Network](#5-provider--network) | ~12 | High |
| 6 | [Enrollment](#6-enrollment) | ~3 | High |
| 7 | [CDHP](#7-cdhp-consumer-directed-health-plans) | ~4 | High |
| 8 | [Examiner Workflow](#8-examiner-workflow) | ~8 | High |
| 9 | [Authorization & Medical Review](#9-authorization--medical-review) | ~5 | Medium |
| 10 | [Broker & Agent](#10-broker--agent) | ~5 | Medium |
| 11 | [Documents & Correspondence](#11-documents--correspondence) | ~10 | Medium |
| 12 | [Privacy & Permissions](#12-privacy--permissions) | ~4 | Medium |
| 13 | [Financial / FICC](#13-financial--ficc) | ~3 | Low / merge candidate |
| 14 | [Reference Data](#14-reference-data) | ~4 | Low / supporting concern |

---

## 1. Member

**Confidence: High** — the largest single cluster by operation count. A clear, stable core domain.

### Operations
| Method | Notes |
|---|---|
| GetMemberDemographicDetails | Most heavily versioned method in the system (v1.0–v1.12) |
| GetMemberDemographicSummary | |
| GetMemberGroupInfo | Member's group context |
| GetMemberAltId | Alternate identifier lookup |
| GetMembershipRelationshipSummary | Subscriber/dependent relationships |
| GetMembershipTypeSummary | |
| GetMemberTribalInfo | |
| GetMemberStatementInfo | |
| GetTerminationInfo | Termination history |
| GetMemberIDCard | |
| SendMemberIDCard | |
| GetMemberIDCardDocumentIDs | |
| GetMemberIDCardVendor | |
| UpdateMemberPhones | Write operation |
| UpdateMemberEmailAddress | Write operation |
| GetMemberEmailAddressUpdates | |
| SetMemberEComPreference | |
| GetMemberEComPreference | |
| GetCountryInformation | Used for member address context |
| ValidateStateAndZipCode | |

### Entities
`Member`, `Address`, `Phone`, `Email`, `MemberEmail`, `MemberList`, `MembershipSummary`, `TribalField`, `IDCardDocument`, `IDDocument`, `Country`, `State`

### Notes
- `GetMemberGroupInfo` partially overlaps with the Group & Employer domain. Here it answers "which group is *this member* in" — the member is the pivot. In Group & Employer the pivot is the group. This is the clearest boundary test: *whose data is it?*
- The high version count on `GetMemberDemographicDetails` (13 versions, more than any other method) suggests this is the most-changed contract in the system — a sign this is genuinely the core entity everything else orbits.

---

## 2. Claims

**Confidence: High** — the second-largest cluster. Claims data and claims-processing actions are clearly grouped.

### Operations
| Method | Notes |
|---|---|
| GetMemberClaimSummary | Member-pivoted view of claims |
| GetRecentClaimsSummary | |
| GetClaimDetails | |
| GetMemberClaimAddress | Mailing address context for a claim |
| GetClaimTracking | Status tracking |
| GetClaimTypesSummary | |
| GetClaimComments | |
| GetClaimDocument | Document retrieval for a claim (via FTP) |
| GetClaimImages | Via OneImage |
| SubmitClaimCrossover | Write — COB crossover submission |
| SubmitClaimRepricing | Write — repricing request |
| SetAutoCrossover | Write — auto-crossover flag |

### Entities
`Claim`, `ClaimStatus`, `ClaimTracking`, `ClaimTypesSummary`, `Diagnosis`, `DiagnosisCodeRange`, `CostShare`, `ModifierCode`, `Revenue`, `ServiceLine`, `ServiceLineRank`, `Procedure`, `Subrogation`, `Remark`, `RepComment`, `NPI`, `NPIValues`, `Payment`, `PaymentsInfo`, `PendReason`, `PendLetter`, `PendLetterVariable`

### Notes
- `GetClaimDocument` uses `DocumentAccessHandler` (FTP fallback) and `GetClaimImages` uses `OneImageBuilder`. Despite these being infrastructure-level distinctions, the *domain ownership* is unambiguous — these are documents about a claim, owned by this domain.
- `GetNextClaim` is **not** included here — it belongs in Examiner Workflow. The operation is about assigning a claim from a work queue to an examiner, not about reading claim data. See [boundary tensions](#boundary-tensions) below.
- `Subrogation` entity sits here because it tracks claim-level third-party liability. It could arguably be its own sub-domain if subrogation processing is a distinct team/workflow.

---

## 3. Coverage & Benefits

**Confidence: High** — plan enrollment, accumulators, COB, and benefit services are strongly cohesive.

### Operations
| Method | Notes |
|---|---|
| GetMemberCurrentPlan | Current active plan |
| GetMemberPlanProducts | Products/riders within a plan |
| GetMemberPlanTypePeriods | Plan effective periods |
| GetBenefitServicesSummary | Covered services summary |
| GetMemberAccumulators | Deductibles, OOP maximums |
| GetMemberCOBInformation | Coordination of benefits |
| GetMemberSpecialCostShareInfo | Cost share exceptions |
| GetMemberReferrals | Referral authorizations (see tension note) |

### Entities
`Coverage`, `CoveragePlan`, `PlanProduct`, `PlanEffectiveYear`, `MemberProduct`, `MedicaidCoverage`, `Medicare`, `MedicareCoverage`, `MedicareCoverageType`, `OtherCoverage`, `OtherINSCoverage`, `OtherINSDates`, `OtherINSInformation`, `OrderOfPayment`, `SpecialCostShareField`, `OtherCoverage`, `Referral`

### Notes
- Heavy version history on `GetMemberAccumulators` (10 versions) and `GetMemberPlanProducts` (9 versions) signals active business rule evolution — accumulator and plan product logic is a moving target.
- `GetMemberReferrals` is ambiguous (see [boundary tensions](#boundary-tensions)).

---

## 4. Group & Employer

**Confidence: High** — group structure, employer billing, and plan administration for the employer side are clearly distinct from the member side.

### Operations
| Method | Notes |
|---|---|
| GetGroupDetails | |
| GetGroupProductSummary | Products offered by a group |
| GetGroupProductBacklog | Work queue for group-product changes (uses CL_MISC account) |
| GetProductTypeBacklog | Work queue variant |
| GetHoldingCompanies | Corporate hierarchy |
| GetGroupsByHoldingCompany | |
| GetDivisions | Org structure |
| GetDepartments | Org structure |
| GetEmployerPlanInformation | Plan details from employer perspective |
| GetEmployerInvoices | Billing |
| GetInvoiceDocument | Invoice document retrieval |

### Entities
`Group`, `Division`, `Department`, `HoldingCompany`, `GroupProduct`, `GroupProductBacklog`, `ProductTypeBacklog`, `Invoice`, `Carrier`, `PlanProduct`

### Notes
- `GetGroupProductBacklog` and `GetProductTypeBacklog` use a **separate WebDE account** (`CL_MISC` instead of `CL_CSC`). This is a meaningful signal: the DG mainframe already treats these operations as belonging to a different subsystem. In a modular monolith, this could correspond to a separate module; in a microservice split it could become a separate service.
- `GetGroupsByBroker` appears here rather than in Broker & Agent — the group is the noun being returned; the broker is the filter.

---

## 5. Provider & Network

**Confidence: High** — provider data and PCP election/referral management form a coherent sub-system.

### Operations
| Method | Notes |
|---|---|
| GetProviderDemographic | |
| GetNetworkProviders | Network lookup |
| GetProviderTierLevel | Tiered network level |
| GetProvPayeeInfo | Provider payee/remittance info |
| GetPCPLanguages | PCP lookup support |
| GetPCPSpecialities | PCP lookup support |
| GetMemberPCPElections | Member's selected PCP history |
| SetMemberPCPElection | Write — PCP assignment |
| SetPCPReferral | Write — referral from PCP |
| SetPCPManager | Write — administrative |
| GetPCPElectionAuthorizees | Who can elect a PCP on behalf of member |
| GetPCPElectionAuthorizers | Who authorized the election |

### Entities
`Provider`, `PrimaryCareProvider`, `Network`, `NPI`, `NPIValues`, `PCPAuthorizee`, `PCPAuthorizer`, `PCPLanguage`, `ProvPayeeInfo`, `Referral`

### Notes
- `GetProvPayeeInfo` uses `DocumentAccessHandler`, meaning provider payee info can include an FTP-retrieved attachment. Infrastructure detail — domain ownership is still Provider.
- PCP authorization (`GetPCPElectionAuthorizees`, `GetPCPElectionAuthorizers`) is technically a permissions construct, but it is specific to PCP election — these are not generic HIPAA permissions. They belong here, not in Privacy & Permissions.
- `Referral` appears in both Coverage & Benefits (member-initiated referrals) and Provider & Network (PCP-issued referrals). The entity is shared; the use cases differ. See [boundary tensions](#boundary-tensions).

---

## 6. Enrollment

**Confidence: High** — the enrollment submission workflow is architecturally distinct (uses `EnrollmentAccesshandler`, which has custom post-processing for Base64/delimiter-encoded validation data), and has its own plan list support.

### Operations
| Method | Notes |
|---|---|
| SubmitEnrollment | Most complex write operation — heavy versioning (8 versions), custom response decoding |
| GetEnrollmentPlanList | Available plans for enrollment context |

### Entities
`MemberEnrollment`, `EnrollmentPlanProduct`, `Fulfillment`, `FulfillmentAttribute`, `ValidationField`, `ValidationFile`

### Notes
- The `EnrollmentAccesshandler` exists specifically because DG returns enrollment validation results as a Base64-encoded, delimiter-separated blob (`chr(252)` / `chr(253)`) — not XML. This decoding complexity is entirely inside this handler and is invisible to callers. In a new system, this boundary suggests enrollment has specialized integration concerns that justify isolation.
- `GetEnrollmentPlanList` serves the enrollment context (what can I enroll in?), so it belongs here rather than in Coverage & Benefits (what am I *already* enrolled in?).

---

## 7. CDHP (Consumer-Directed Health Plans)

**Confidence: High** — CDHP is a distinct product type (HSA, HRA, FSA) with its own data model and operations. The `HRABridge` entity (bridging HRA funds to medical benefits) reinforces that this is specialized enough to warrant its own boundary.

### Operations
| Method | Notes |
|---|---|
| GetCDHPDetails | Account details |
| GetCDHPAccountTypePeriods | Period-based account breakdown |
| GetCDHPContribution | Employer/employee contribution data |
| GetCDHPBridgeDetails | HRA-to-medical bridge |

### Entities
`CDHP`, `CDHPAccounts`, `HRABridge`, `Contribution`, `AccountType`

### Notes
- `GetFICCBankInfo` / `SetFICCBankInfo` / `SetFICCCardAuth` (see Financial / FICC below) are candidates for merging into CDHP, since bank account and card authorization are often associated with HSA/FSA accounts. Whether they merge depends on whether FICC is a separate product line or an account feature.

---

## 8. Examiner Workflow

**Confidence: High** — this domain is defined by the *actor* (the claims examiner) and the *workflow* (queue-based claim assignment and internal messaging). The operations here have almost no overlap with the data-retrieval operations in Claims.

### Operations
| Method | Notes |
|---|---|
| GetNextClaim | Pull next claim from examiner work queue |
| GetExaminerGroupProduct | Which group/products is this examiner assigned to |
| GetExaminerMessages | Internal messaging between examiners |
| CreateExaminerMessage | Write — send internal message |
| SetExaminerMessageAttribute | Write — mark read, archive, etc. |
| GetStaff | Staff directory lookup |
| GetAnalystInfo | Analyst assignment info |
| GetCallsReferrals | Call log / referral tracking (internal ops context) |

### Entities
`Examiner`, `ExaminerMessage`, `Analyst`, `CobraAnalyst`, `Staff`, `Supervisor`, `AgingBucket`, `PendReason`, `PendLetter`, `PendLetterVariable`, `Note`, `CommentNote`, `InternalCommentNote`, `Appointment`

### Notes
- `GetNextClaim` returns a claim, but its *purpose* is work assignment — not data retrieval. The caller is an examiner pulling from a queue. The pivot is the workflow, not the claim.
- `AgingBucket` — tracks how long claims have been in queue — is a purely operational metric. Its presence here rather than in Claims confirms this is a workflow/operational domain.
- `GetCallsReferrals` name is ambiguous (see [boundary tensions](#boundary-tensions)).
- This domain could be named **Claims Operations** or **Claims Processing** if "Examiner Workflow" is too implementation-specific for the new system.

---

## 9. Authorization & Medical Review

**Confidence: Medium** — pre-certification, predeterminations, and appeals are clinically adjacent to claims but represent *upstream* processes (authorization before a procedure) and *downstream* processes (challenging a decision). Many organizations treat these as their own domain; others merge them into Claims.

### Operations
| Method | Notes |
|---|---|
| GetMemberPreCertSummary | Pre-authorization summary |
| GetMemberPreCertDetail | Pre-authorization detail |
| GetFamilyPredeterminations | Coverage predetermination for a family |
| GetMemberAppeals | Appeal records |
| GetMemberReferrals | Referral authorizations (see tension note) |

### Entities
`PreCert`, `PreCertStatus`, `PreCertStatuses`, `Predetermination`, `PredeterminationStatuses`, `Appeals`, `MedicalReview`, `MedicalReviewStatus`, `Referral`

### Notes
- The presence of `MedicalReview` and `MedicalReviewStatus` entities (in `Aetna.MER.CSC.Entities`) with no corresponding top-level request/response pair suggests these are nested inside PreCert responses — not separate operations. This reinforces that PreCert *is* medical review in this system.
- If the new system's team structure separates the "utilization management" team from the "claims" team, this domain boundary will align naturally with that org boundary. If no such separation exists, merging into Claims is reasonable.

---

## 10. Broker & Agent

**Confidence: Medium** — small operation set, but the distribution/sales channel is typically a distinct subdomain in health insurance with its own stakeholders.

### Operations
| Method | Notes |
|---|---|
| GetBrokerStates | States where a broker is licensed |
| GetBrokersByState | Brokers in a given state |
| GetGroupsByBroker | Groups a broker manages |
| GetAgentDemographicSummary | Agent profile |
| GetProducerAppointments | Producer appointment status |
| GetCommissionSchedule | Commission rates |

### Entities
`Broker`, `DocumentCommission`, `ExternalVendor`

### Notes
- `DocumentCommission` entity (not just a commission record, but a commission *document*) suggests commission statements are a document artifact of this domain.
- Three related concepts appear: Broker (entity), Agent (actor in GetAgentDemographic), Producer (in GetProducerAppointments). These may be distinct roles (agent vs. broker vs. general agent) or the same concept at different stages. Worth clarifying with subject-matter experts before settling on naming.

---

## 11. Documents & Correspondence

**Confidence: Medium** — documents are inherently cross-cutting. The central question is whether each parent domain owns its own documents, or whether there is a unified document management domain.

### Operations
| Method | Notes |
|---|---|
| GetMemberDocument | General member documents |
| GetInvoiceDocument | Invoice-attached documents |
| GetImage | Secured image retrieval (OneImage) |
| SendLetterOfCoverage | Write — generate/send coverage letter |
| GetCorrespondenceSummary | Outbound correspondence history |
| GetCorrespondenceTrackingInfo | Status of a specific correspondence |
| GetAnnouncements | System/plan announcements |
| DGFileTransferNotification | DG-to-external file transfer events |

### Entities
`Document`, `MemberStatementDocument`, `MemberStatementInfo`, `CorrespondenceData`, `CorrespondenceItems`, `FTPInformation`, `Announcement`

### Notes
- **Option A — Unified Document domain:** All document retrieval goes through one module with document-type parameters. Benefits: single FTP/OneImage integration point. Downside: this module becomes a dependency of Claims, Member, and Employer simultaneously.
- **Option B — Documents owned by parent domain:** `GetClaimDocument` lives in Claims, `GetInvoiceDocument` in Group & Employer, `GetMemberDocument` in Member. Correspondence lives in its own small module. Benefits: true domain isolation. Downside: FTP/imaging infrastructure is duplicated or must be a shared internal library.
- `Announcements` could live in a lightweight **Notifications** module alongside `SendLetterOfCoverage` and `GetCorrespondence*`. These are communications *from the plan to the member* — a distinct subdomain from document *retrieval*.

---

## 12. Privacy & Permissions

**Confidence: Medium** — HIPAA authorization management is a specialized area, but small. Whether it warrants its own module or is a feature within Member depends on regulatory change velocity and team ownership.

### Operations
| Method | Notes |
|---|---|
| GetHIPAAPermissionsAuthorized | What is this member authorized to access |
| GetHIPAAPermissionsAuthorizees | Who can access on behalf of member |
| SetHIPAAPermissions | Write — update authorizations |
| GetUserUpdatePermission | What fields can this user update |

### Entities
`Authorizee`, `Authorizer`

### Notes
- `GetUserUpdatePermission` is slightly different in nature — it's about *system user* permissions, not member HIPAA permissions. It could belong in an admin/access control module instead.
- If HIPAA permission data is queried frequently by other domains (e.g., Claims checking before returning data), this should be its own module with a clear API. If it's only managed in a settings UI, it could fold into Member.

---

## 13. Financial / FICC

**Confidence: Low — merge candidate.** Only three operations. FICC likely refers to a Financial Institution Card/Contribution account (HSA/FSA-linked banking).

### Operations
| Method | Notes |
|---|---|
| GetFICCBankInfo | Bank account on file |
| SetFICCBankInfo | Write — update bank account |
| SetFICCCardAuth | Write — card authorization |

### Notes
- **Merge into CDHP** if FICC accounts are always HSA/FSA-linked. The bank account and card auth would then be features of the CDHP account model.
- **Keep separate** only if FICC spans across product types (medical, dental, vision) in a way that CDHP does not.

---

## 14. Reference Data

**Confidence: Low — supporting concern.** These are lookup tables, not a business domain.

### Operations
| Method | Notes |
|---|---|
| GetValidStates | State code list |
| GetCountryInformation | Country code list |
| GetStaticReportCodes | Report code reference data |
| GetDGUserInfo | DG system user lookup |

### Notes
- Reference data is typically handled as a shared library or a lightweight supporting service, not a first-class domain module. In a modular monolith, these can live in a `Reference` or `Lookup` module with no domain logic — pure data.
- `GetDGUserInfo` may belong in an administration/infrastructure module rather than reference data if it's used for operational support purposes.

---

## Boundary Tensions

These operations don't have a clear single home. Each is noted with the tension and a recommended resolution.

### `GetNextClaim` — Claims vs. Examiner Workflow
- Claims perspective: it returns claim data.
- Examiner Workflow perspective: it's a queue-pull operation for an internal user.
- **Recommendation:** Examiner Workflow. The operation is about work assignment, not claim data retrieval. A claims module should not know about examiner queues.

### `GetMemberReferrals` — Coverage & Benefits vs. Provider & Network vs. Authorization & Medical Review
- A referral is: an authorization to see a specialist (utilization management), a record on the member's plan (coverage), and an action a PCP takes (provider).
- **Recommendation:** Authorization & Medical Review if the referral is primarily a prior-auth record. Coverage & Benefits if it is primarily about plan entitlement. Provider & Network if PCP-issued referrals are the primary use case. Need subject-matter expert input to resolve.

### `GetCallsReferrals` — Examiner Workflow vs. Member vs. standalone
- The name suggests a call log ("calls and referrals" as a combined report). The `CallsReferral` entity in the codebase has no detailed examination in this analysis.
- **Recommendation:** Examiner Workflow if this is an internal operations report. Member if it's a member-facing call history. Read the entity class before deciding.

### `GetMemberGroupInfo` — Member vs. Group & Employer
- Returns the group context for a member.
- **Recommendation:** Member. The pivot is the member; group data is returned as contextual information. If the caller already has a group and wants its members, that query belongs in Group & Employer.

### `GetGroupsByBroker` — Group & Employer vs. Broker & Agent
- **Recommendation:** Group & Employer. Same rule: the noun being returned (groups) determines ownership. Broker is the filter parameter, not the owner.

### Documents — unified vs. parent-domain-owned
Described under domain 11 above. This is the most consequential structural decision and should be made early, as it determines how many modules have a direct dependency on FTP/imaging infrastructure.

---

## Signals from the Source Worth Carrying Forward

**Separate WebDE account for Group Product Backlog (`CL_MISC`)**
The DG mainframe already treats `GetGroupProductBacklog` and `GetProductTypeBacklog` as a separate subsystem. This is the strongest infrastructure signal of a natural boundary in the entire codebase. The Group & Employer module, or a dedicated **Enrollment Operations** module, should own these. If the new system ever needs to scale or deploy these operations independently, this is the lowest-risk seam.

**Handler type as domain signal**
- `EnrollmentAccesshandler` (custom decoding) → Enrollment is genuinely different.
- `DocumentAccessHandler` (FTP) → Document retrieval has infrastructure complexity worth isolating.
- `OneImageBuilder` → Image retrieval is a separate integration concern.

In the new system, each of these integration patterns becomes an infrastructure adapter — but the domain boundary analysis above suggests which module owns each adapter.

**Version count as change-frequency signal**
High version counts indicate high business rule churn. These are the contracts most likely to change again:

| Method | Versions | Implication |
|---|---|---|
| GetMemberDemographicDetails | 13 | Member domain has high change velocity |
| GetMemberPlanProducts | 9 | Coverage & Benefits changes frequently |
| GetGroupProductBacklog | 13 | Group operations are actively maintained |
| GetMemberAccumulators | 10 | Accumulator logic evolves often |
| GetClaimDetails | 8 | Claims detail schema is unstable |
| SubmitEnrollment | 8 | Enrollment rules change often |

High change-frequency domains benefit most from isolation — changes are contained and don't ripple.

**Dead-code handlers as historical evidence**
The ~20 legacy handler classes (e.g., `GetClaimDetailHandler`, `GetMemberAccumulatorsHandler`) that were superseded by `DataAccessHandler<T>` are an organic record of which operations were built first and which were iterated on longest. The fact that claims, member demographics, accumulators, and plan products all appear in this list suggests these were the original core use cases of the service — further supporting that Claims, Member, and Coverage & Benefits are the most foundational domains.
