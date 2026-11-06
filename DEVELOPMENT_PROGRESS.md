# GameGuard Development Progress

## Roadmap Overview

- [x] **Stage 1: Solution Architecture, Domain Core, Data Persistence & Repository Layer, Test Harness**
- [ ] **Stage 2: Process Monitoring Engine & Anti-Cheat Safe Detection**
- [ ] **Stage 3: Time Tracking Engine & Limit Calculation Service**
- [ ] **Stage 4: Enforcement & Anti-Bypass System**
- [ ] **Stage 5: Desktop UI (WPF MVVM Dashboard & Statistics)**
- [ ] **Stage 6: Windows Service Integration & IPC**

---

## Stage 1: Solution Architecture, Domain Core, Data Persistence & Repository Layer

### Status: COMPLETED

### Achievements
1. **Repository & Solution Setup**:
   - Initialized Git repository with comprehensive `.gitignore` for .NET 10, C#, WPF, and SQLite WAL files.
   - Configured modern .NET 10 solution `GameGuard.slnx` with modular projects: `GameGuard.Core`, `GameGuard.Infrastructure`, and `GameGuard.Core.Tests`.

2. **Core Domain Models (`GameGuard.Core.Models`)**:
   - `GameProfile`: Identifies games by name and executable aliases (e.g. `cs2.exe`, `VALORANT-Win64-Shipping.exe`), case-insensitive matching logic with or without extension.
   - `DailyLimit`: Represents configurable individual game limits or global shared pool limits in minutes, with configurable reset hours and grace period durations.
   - `GamingSession`: Tracks process instances, start time, end time, total active duration in seconds, and termination reason.
   - `LimitCalculationResult`: Complete snapshot of current daily usage, remaining time, binding constraints (individual vs shared), grace period states, and violation flags.
   - `SessionTerminationReason`: Enum classifying session exits (Active, UserClosed, LimitEnforced, ForcedByAdmin, CrashOrExternal).

3. **Domain Contracts & Services (`GameGuard.Core.Interfaces` & `GameGuard.Core.Services`)**:
   - `ISystemClock` / `SystemClock`: Testable time provider abstraction.
   - `ILimitCalculator` / `LimitCalculator`: Pure mathematical domain logic for computing effective remaining time under dual constraints (individual limit and shared pool limit) and detecting grace period transitions.
   - `IGameProfileRepository`, `ILimitRepository`, `ISessionRepository`: Asynchronous repository abstractions.
   - `IDatabaseInitializer`: Schema management contract.

4. **Persistence Layer (`GameGuard.Infrastructure.Persistence`)**:
   - `SqliteConnectionFactory`: Configures SQLite with Write-Ahead Logging (`PRAGMA journal_mode=WAL`), `PRAGMA synchronous=NORMAL`, and foreign key enforcement for high performance and durability.
   - `SqliteDatabaseInitializer`: Creates tables (`GameProfiles`, `DailyLimits`, `GamingSessions`) and indexes (`IX_GamingSessions_GameProfile_StartTime`, `IX_GamingSessions_Active`, `IX_DailyLimits_Shared`). Automatically seeds default profiles for Counter-Strike 2 and Valorant.
   - `SqliteGameProfileRepository`: Lightweight ADO.NET repository with JSON-serialized process aliases.
   - `SqliteLimitRepository`: Individual and shared limit upsert and query operations.
   - `SqliteSessionRepository`: Start, heartbeat updates, end-of-session logging, and aggregate day-usage SQL queries.
   - `ServiceCollectionExtensions`: Clean Microsoft Dependency Injection extensions.

5. **Test Harness (`GameGuard.Core.Tests`)**:
   - 23 unit and integration tests covering:
     - Pure limit calculations (individual, shared, competing constraints, active session durations, grace periods, zero-limit scenarios).
     - Process name matching (case insensitivity, extension stripping, aliases).
     - Full SQLite persistence lifecycle (schema seeding, game CRUD, limits, session heartbeats, daily duration aggregates).
   - Test execution result: **23 passed, 0 failed, 0 skipped**.

### Verification Commands
```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build GameGuard.slnx
dotnet test GameGuard.slnx
```

### Remaining Limitations
- Live process scanning is not yet wired to active system processes (scheduled for Stage 2).
- Automatic time tracking loop and enforcement policies are not yet active as a background runner (scheduled for Stage 3 & 4).
- User interface and Windows Service extraction remain for subsequent stages.
