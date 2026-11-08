# GameGuard Development Progress

## Roadmap Overview

- [x] **Stage 1: Solution Architecture, Domain Core, Data Persistence & Repository Layer, Test Harness**
- [x] **Stage 2: Process Monitoring Engine & Anti-Cheat Safe Detection**
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
   - 23 unit and integration tests covering limit calculations, profile matching, and SQLite persistence.

---

## Stage 2: Process Monitoring Engine & Anti-Cheat Safe Detection

### Status: COMPLETED

### Achievements
1. **Process Inspection Data Contracts (`GameGuard.Core.Models` & `GameGuard.Core.Interfaces`)**:
   - `DetectedProcessInfo`: Lightweight record (`ProcessId`, `ProcessName`, `ExecutablePath`, `ParentProcessId`) avoiding heavy managed process objects.
   - `ProcessLifecycleEventArgs`: Carries `GameProfile`, `DetectedProcessInfo`, `ProcessEventType` (`Started`, `Running`, `Exited`), and UTC timestamp.
   - `IProcessScanner`: Abstraction for scanning system processes.
   - `IProcessMonitor`: Polling engine interface with `ProcessStarted`, `ProcessRunning`, and `ProcessExited` lifecycle events.

2. **Anti-Cheat Safe Win32 Enumeration (`GameGuard.Infrastructure.Native`)**:
   - `Win32ProcessScanner`: Implements OS-level process enumeration using `CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)` and `Process32FirstW` / `Process32NextW`.
   - **Zero Anti-Cheat Interference**: Requests zero handles to game memory, performs no thread injection, hooks no binaries, and deploys no kernel drivers. Completely safe against Riot Vanguard, Valve Anti-Cheat (VAC), EasyAntiCheat, and BattlEye.
   - **Non-Invasive Path Resolution**: Uses `OpenProcess` strictly with `PROCESS_QUERY_LIMITED_INFORMATION (0x1000)` and `QueryFullProcessImageNameW`. If access is restricted (e.g. for protected games or system processes), it gracefully continues without throwing or blocking.
   - `SafeSnapshotHandle` & `SafeProcessQueryHandle`: RAII wrapper handles derived from `SafeHandleZeroOrMinusOneIsInvalid` guaranteeing leak-free OS handle disposal.

3. **Background Monitoring Engine (`GameGuard.Infrastructure.Monitoring`)**:
   - `ProcessMonitor`: High-performance background polling engine maintaining `ConcurrentDictionary<int, TrackedProcessState>`.
   - Real-time diffing between scans to trigger `ProcessStarted`, `ProcessRunning`, and `ProcessExited` events.
   - Configurable polling frequency (default 1000ms), asynchronous cancellation, and isolated exception handling for subscriber event handlers.
   - Exposed `CheckProcessesOnceAsync` method enabling synchronous lifecycle evaluation and deterministic testing.

4. **Dependency Injection Registration (`GameGuard.Infrastructure.DependencyInjection`)**:
   - Added `AddGameGuardProcessMonitoring` extension method registering `IProcessScanner` -> `Win32ProcessScanner` and `IProcessMonitor` -> `ProcessMonitor`.

5. **Test Harness Expansion (`GameGuard.Core.Tests`)**:
   - Added `MockProcessScanner` and `MockGameProfileRepository` for isolated process lifecycle verification.
   - Added 7 tests in `ProcessMonitorTests` (start detection, running persistence, exit detection, unmonitored filtering, disabled profile handling, concurrent multi-game tracking, engine start/stop).
   - Added 3 integration tests in `Win32ProcessScannerTests` validating real-time Win32 snapshot execution on Windows.
   - Overall test suite: **33 passed, 0 failed, 0 skipped (Duration: ~200 ms)**.

### Verification Commands
```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build GameGuard.slnx
dotnet test GameGuard.slnx
```

### Remaining Limitations
- Dynamic time tracking engine and automated session persistence to SQLite are not yet connected to the process monitor events (scheduled for Stage 3).
- Warning notifications, termination actions, and relaunch blocking are not yet active (scheduled for Stage 4).
- Desktop GUI (WPF MVVM) and Windows Service extraction remain for Stages 5 and 6.
