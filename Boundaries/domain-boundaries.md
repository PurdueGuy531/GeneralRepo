# Domain Boundary Analysis

This document analyzes the existing HealthConnect codebase to identify candidate domain boundaries for a future modular monolith. The goal is to surface natural seams in the business logic — groupings that could become modules, and eventually independent microservices if needed.

## How the Signals Were Read

Three sources inform the boundaries:

- **Controller names** — the user-facing surface area and capability groupings
- **DataAccess command names** — the actual operations (369 request DTOs, 300+ commands)
- **Which downstream service handles each call** — CSC WCF vs. HC SQL vs. APIC vs. DocStore

The downstream service split is particularly useful. **CSC** is the authoritative health plan system (claims, eligibility, benefits, member records). **HC SQL** is the portal's own operational store (workflows, configuration, content, user accounts). Many domains span both — that cross-cutting is flagged throughout.

---

## Core Business Domains

### 1. Claims

The clearest boundary. All answers to "what happened with a claim?" live here.

**Key operations:**
- `CscMemberClaimSummary`, `CscProductClaimDetail`, `CscProductClaimCrossover`, `CscClaimTypeSummary`, `CscRecentClaimsSummary`
- `ApicGetEob` (EOB document retrieval)
- `HcSubmitClaimInquiry`, `HcClaimInquiryDocument`, `GetClaimSubmissionForms`, `GetGroupClaimSubmissionCommand`

**Portals served:** Member (submission), Employer (inquiry), Provider (inquiry), Admin (support)

> **Note:** CDHP/HSA claims look like claims but follow different rules and a different downstream path — they belong in Health Accounts (domain #7).

---

### 2. Benefits & Coverage

Answers "what is this member entitled to under their health plan?"

**Key operations:**
- `CscMemberBenefitServices`, `CscMemberAccumulators`, `CscMemberPredeterminations`, `CscGetMemberAppeals`
- `CscLetterOfCoverage`, `CscSendLetterOfCoverage`
- `CscMemberCurrentPlan`, `CscMemberPlanProducts`, `CscMemberPlanTypePeriod`
- `CscGetMemberCOBInformation`, `HcSetCOB`, `HcGetCOB` (Coordination of Benefits)

**Portals served:** Member, Employer, Provider, Admin

> **Note:** ID card retrieval straddles this domain and Documents — see Tension Points.

---

### 3. Enrollment

State-machine-heavy, workflow-driven. The largest internal (HC SQL) domain by command count.

**Key operations:**
- Life events: `HcGetEnrollmentLifeEvent`, `HcSetLifeEvent`, `HcGetLifeEvents`
- Transactions: `HcSetEnrollment`, `HcSubmitEnrollment`, `HcSetEnrollmentMemberDetails`, `HcGetEmployeeEnrollmentDetails`
- Workflow engine: `TransactionTask*`, `HcGetTransactionDetails`, `HcGetTransactionStatus`, `HcCancelTransaction`, `HcResetTask`
- Activity log (pending approvals): `HcGetActivityLog`, `HcUpdateActivityLogDetail`, `HcDeleteActivityLog`
- COBRA: `HcSetCobra`, `HcGetCobra*`, `CobraQualifyingEvent`, `CobraInitialNotification`, `CscLoadEligibilityTransaction`

**Portals served:** Employer (primary), Member, Admin (eligibility workflow)

> **COBRA sub-module note:** Federal rules, notification timelines, and a dedicated analyst workflow give COBRA enough weight to be a sub-module within Enrollment, extractable later if it grows.

---

### 4. Member (Identity & Profile)

The health plan member record. Distinct from portal login credentials — those belong to Identity & Account (domain #13).

**Key operations:**
- Demographics: `CscMemberDemographic*`, `CscMemberDemographicSummary`, `CscMemberInfo`, `CscMemberIdentity`
- Search: `CscMemberSearch`, `CscMembershipRelSummary`
- Dependents: `HcSetAddDependent`, `HcSetDependentDetails`, `HcGetDependentCount`
- HIPAA: `CscSetHIPAAPermissions`, `HcSetHipaaAuthorization`, `HcGetHipaaConfirmationValidation`
- Communication preferences: `CscGetMemberEComPref`, `CscSetMemberEComPref`, `HcGetMemberEComPrefForBenefits`
- Updates: `CscUpdateEmailAddress`, `CscUpdateMemberPreferredMobileNumber`, `HcSetMemberDetails`

**Portals served:** All (each portal views member data through a different lens)

---

### 5. Group / Employer Management

The employer as a contracted entity. Distinct from Enrollment (which is what happens to people *within* that group).

**Key operations:**
- Roster & org structure: `CscGetEmployeeSummary`, `CscGetEmployeeMembershipDetails`, `CscGetDivision*`, `CscGetHoldingCompany*`
- Group search & config: `CscGroupSearch`, `CscGetGroupDetails`, `HcGetGroupConfigSettings`, `HcSetGroupSettings`, `HcGetGroupServiceSettings`
- Employer info & permissions: `HcGetEmployerInfo`, `HcGetEmployerPermissions`, `HcSetEmployerDetails`
- Invoices: `CscGetEmployerInvoices`, `CscGetEmployerInvoiceDocument`
- Group setup & automation: `HcGetGroupSetupCheck`, `HcGetGroupAutomation`, `HcSetGroupAutomation`
- Registration: `HcEmployerHasAcceptedRegistrationToken`, `HcSetEmployerRegistrationToken`

**Portals served:** Employer (primary), Admin (support/setup)

---

### 6. Provider & Care Network

"Provider portal" is a user type, not a domain. This domain — provider directory, PCP, referrals, pre-authorization — serves multiple portals.

**Key operations:**
- Network: `CscGetNetworkProviders`, `CscNetworkProviderSummary`, `CscProviderDemoGraphic*`
- PCP: `CscGetMemberPCPElections`, `CscSetMemberPCPElections`, `CscGetPCPLanguages`, `CscGetPCPSpecialties`
- Referrals: `CscSubmitReferral`, `CscGetMemberReferral`
- Pre-authorization/pre-cert: `CscGetPreCertSummary`, `CscGetPreCertDetails`, `CscAuthorizedDetails`, `CscAuthorizeeDetails`
- Provider portal records: `HcGetProviderInfo`, `HcSetProviderInfo`, `HcProviderInfographics`

**Portals served:** Member (PCP, referrals), Employer (PCP, referrals), Provider (all), Admin (support)

---

### 7. CDHP / Health Accounts

HSA, HRA, FSA consumer-directed health accounts. Distinct from Claims — different rules, different vendor paths, different financial model.

**Key operations:**
- `CscCdhpAccount`, `CscCdhpDetail`, `CscCdhpContribution`, `CscCdhpDocument`
- `CscCdhpClaimSummary`, `CscCdhpClaimDetail`, `CscCdhpIntegratedHRAClaimSummary`
- `CscHRABridge`, `CscCdhpSetAutoCrossover`
- `HcGetFSAReimbursementInfo`, `HcSetFSAReimbursementInfo`

**Portals served:** Member, Employer

---

### 8. Producer / Broker

The thinnest, most self-contained domain. Strong candidate for early extraction.

**Key operations:**
- `CscProducerSearch`, `CscGetBrokerByStates`, `CscGetBrokerStates`, `CscGetGroupsByBroker`
- `CscGetProducerCommissionScheduleDocument`, `CscGetProducerInvoices`, `CscGetProducersGroupAndBilling`
- `CscGetStateAndCarrierAppointments`

**Portals served:** Producer, Admin (support)

Minimal overlap with other domains. Mostly read-only CSC calls. Low coupling makes this a good first extraction target.

---

## Supporting / Platform Domains

These domains don't own core health plan business logic but are real modules with their own data and behavior.

### 9. Documents

DocStore operations, group/plan documents, online documents, industry resources. 17 DocStore commands plus HC metadata. More of a shared platform capability than a business domain — EOBs and ID cards conceptually belong to Claims and Benefits respectively, but the delivery mechanism (upload/download/manage) lives here.

### 10. Reporting

EDW static reports, self-service reporting, Atlas reports, RxDC self-reporting. Mostly read-only. Clean boundary because the EDW is a separate external system.

**Key operations:** `GetListOfReport`, `GetSingleReport`, `StaticReportList`, `HcGetSelfServiceReportAccess`, `HcGetSelfServiceReportingSubmission`, `HcGetAtlasReportAccess`, `HcSubmitRxDcSelfReporting`

### 11. Portal Content Management

Account messages, promo tiles, useful links, theming/branding, dashboard configuration, menu items, web links, wellness links. Exclusively HC SQL data. The CMS layer for the portal — groups and admins configure what appears where and how.

**Key operations:** `HcGetAccountMessage*`, `HcValidateAndCreateAccountMessage`, `HcGetPromoTile*`, `HcGetUsefulLinks*`, `HcGetGroupThemeSettings`, `HcGetMenuItems`, `HcGetGroupWebLinks`, `HcGetWellnessLinks`

### 12. Communications & Notifications

CommSvc (communication preferences, letter delivery), email templates/history, contact-us routing. Other domains raise events; this domain handles delivery.

**Key operations:** `CommSvcGetCommPref`, `CommSvcSetCommPref`, `CommSvcGetAcctLetterList`, `CommSvcGetPdfFrom1mage`, `GetGroupContactUs`, email template domain entities

### 13. Identity & Account

User registration, account settings (password, email, security questions), user identity management, impersonation/shadow login. Pure portal credential management — not health plan data. Shared kernel across all portals.

**Key operations:** `HcSetUserIdentity`, `HcSetUserSecurityEncryption`, `HcUserDetailsCommand`, `EmployerRegistrationToken`, `AccountSettingController` (all portals)

### 14. SSO & External Integrations

Inbound/outbound SSO (15+ partners: CareMark, ESI, ActiveHealth, Teladoc, Accolade, Payflex, Magellan, etc.), service partner management, NGA/BFP multi-factor authentication, OneCloud member lookup. Integration plumbing, but non-trivial — each partner has its own token construction, parameter requirements, and security profile.

**Key operations:** `ApicNGA*`, `GetOneCloudMemberCommand`, `HcGetGroupInboundSsoSettings`, `HcGetEmployerInboundSsoSetting`, `HcAddServicePartners`, `HcGetServicePartnerList`

---

## Tension Points — Decisions to Make Explicitly

| Concept | The tension | Suggested resolution |
|---|---|---|
| **Eligibility Transactions** | "Is this member eligible right now?" (real-time provider check) vs. admin audit workflow (analyst/auditor/supervisor reviewing eligibility changes) — same name, different problems | Real-time check → Benefits & Coverage; Transaction audit workflow → Enrollment, or its own Eligibility Operations sub-module |
| **ID Cards** | Proof-of-coverage artifact (Benefits) but delivery spans APIC, misti, CSC, DocStore, email/fax | Benefits owns the concept; Documents owns the delivery mechanism; define an integration seam between them |
| **COB** (Coordination of Benefits) | Complex state machine that belongs in Benefits but has its own pending-transaction lifecycle | Sub-module within Benefits; extract if it grows |
| **Plan / PBM Information** | Serves both "what plans does this group offer?" (Group Management) and "what plan is the member currently on?" (Benefits) | Plan catalog → Group/Employer Management; member's current plan assignment → Benefits & Coverage |
| **Wellness** | Thin today — mostly link management. Spans Portal Content (wellness link config) and Benefits (wellness program eligibility) | Fold into Benefits or Portal Content until it justifies its own module |
| **Pre-auth vs. Referrals** | Different workflows, same purpose (approval for care). Both initiated by providers or PCPs | Both in Provider & Care Network — they share the same network and clinical context |
| **COBRA** | Fits within Enrollment lifecycle but has distinct federal rules, notification timelines, and a dedicated analyst role | Sub-module within Enrollment; extract later if compliance complexity justifies it |

---

## Key Structural Observation

The most consequential seam in the whole system is not between business domains — it is between **CSC** (health plan truth, external WCF) and **HC SQL** (portal operational state, internal database). Nearly every domain above straddles this line.

In a modular monolith this means:

- **Each module's own database slice** can be cleanly partitioned from the HC SQL schema by domain. The HC schema mixes concerns from every domain today; decomposing it into domain-owned tables is mechanical but necessary.
- **Each module needs an anti-corruption layer** around its CSC calls. CSC's data model leaks health-insurance-specific identifiers (`GroupId/MemberId/DepNo` triples, plan product codes, vendor codes) that should not pollute module boundaries or bleed into the new service's API contracts.
- **The hardest problem** will not be slicing the HC SQL schema — it will be deciding each module's read model when it needs data that CSC owns but the module needs to reason about locally (e.g., Claims needs member demographics to render an EOB, but Member owns demographics).

---

## Summary Table

| Domain | Type | Data Authority | Extraction Complexity |
|---|---|---|---|
| Claims | Core | CSC + HC | Medium |
| Benefits & Coverage | Core | CSC + HC | Medium |
| Enrollment | Core | HC (primary) + CSC | High — workflow engine, many states |
| Member | Core | CSC (primary) + HC | Medium |
| Group / Employer Management | Core | CSC + HC | Medium |
| Provider & Care Network | Core | CSC + HC | Medium |
| CDHP / Health Accounts | Core | CSC + HC | Low–Medium |
| Producer / Broker | Core | CSC | **Low — strong first candidate** |
| Documents | Supporting | DocStore + HC | Medium |
| Reporting | Supporting | EDW + HC | Low |
| Portal Content Management | Supporting | HC only | **Low — no CSC dependency** |
| Communications | Supporting | CommSvc + HC | Low |
| Identity & Account | Platform/Kernel | HC only | Medium — shared by all portals |
| SSO & External Integrations | Platform | HC + APIC + WCF | High — per-partner complexity |

---

## Related Documents

- [sub-modules-design.md](sub-modules-design.md) — How sub-modules (e.g. COBRA within Enrollment, COB within Benefits) fit into the .NET 10 modular monolith project structure, with concrete layouts, project reference patterns, and an extraction path
