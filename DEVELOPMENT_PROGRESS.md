# GameGuard Development Progress

## Official 12-Stage Development Roadmap

### Phase 1: Core Foundation
- [x] **Stage 1: Solution Architecture, Domain Core & SQLite Persistence**
- [x] **Stage 2: Anti-Cheat-Safe Process Detection & Monitoring**

### Phase 2: Time Tracking & Limit Management
- [x] **Stage 3: Time Tracking Engine & Session Lifecycle Management**
- [ ] **Stage 4: Daily Limit Management & Usage Calculation**

### Phase 3: Enforcement & Protection
- [ ] **Stage 5: Game Enforcement Engine**
- [ ] **Stage 6: Relaunch Suppression, Admin Override & Recovery**

### Phase 4: Desktop Application
- [ ] **Stage 7: WPF Application Foundation & MVVM Architecture**
- [ ] **Stage 8: Dashboard, Game Configuration & Limit Management UI**
- [ ] **Stage 9: Session History, Analytics, System Tray & Notifications**

### Phase 5: Background Architecture
- [ ] **Stage 10: Windows Background Service & Monitoring Extraction**
- [ ] **Stage 11: Named Pipes IPC, Service Recovery & Security**

### Phase 6: Production Readiness
- [ ] **Stage 12: Integration Testing, Optimization, Packaging & Release**

---

## Stage 1: Solution Architecture, Domain Core & SQLite Persistence

### Status: COMPLETED

### Achievements
- Configured .NET 10 LTS modular monolith solution (`GameGuard.slnx`).
- Core domain models: `GameProfile`, `DailyLimit`, `GamingSession`, `LimitCalculationResult`, `SessionTerminationReason`.
- High-performance SQLite persistence with WAL mode (`PRAGMA journal_mode=WAL`), foreign keys, and indexes.
- Pure domain business logic `LimitCalculator` for dual limit evaluation (individual and shared) and grace periods.
- Seeding for Counter-Strike 2 and Valorant.
- 23 unit and integration tests.

---

## Stage 2: Anti-Cheat-Safe Process Detection & Monitoring

### Status: COMPLETED

### Achievements
- Implemented `Win32ProcessScanner` using Windows Toolhelp32 snapshots (`CreateToolhelp32Snapshot`).
- **Zero Anti-Cheat Risk**: Operates out-of-process, requests zero game memory handles, performs no injection, and requires no kernel drivers (safe for Vanguard, VAC, EAC, BattlEye).
- Read-only limited image inspection using `PROCESS_QUERY_LIMITED_INFORMATION (0x1000)` and `QueryFullProcessImageNameW`.
- Custom RAII safe handles `SafeSnapshotHandle` and `SafeProcessQueryHandle`.
- Background diffing engine `ProcessMonitor` with `ProcessStarted`, `ProcessRunning`, and `ProcessExited` events.
- Expanded test suite to 33 passing tests.

---

## Stage 3: Time Tracking Engine & Session Lifecycle Management

### Status: COMPLETED

### Objectives
Connect the Stage 2 process-monitoring engine with the SQLite session repository to track actual gameplay duration in real time, persist checkpoints periodically, handle process exits, prevent sleep/hibernation time inflation, recover from unexpected shutdowns, and track multiple simultaneous games cleanly.

### Components Implemented
1. **Monotonic Time Abstraction (`GameGuard.Core.Interfaces` & `GameGuard.Core.Services`)**:
   - `IMonotonicClock`: Contract providing high-resolution monotonic time readings independent of system wall-clock adjustments or daylight saving shifts.
   - `MonotonicClock`: Production implementation utilizing high-precision `Stopwatch.GetTimestamp()` and `Stopwatch.GetElapsedTime()`.

2. **Session Lifecycle Event Contracts (`GameGuard.Core.Models` & `GameGuard.Core.Interfaces`)**:
   - `SessionLifecycleEventArgs`: Carries `GameProfile`, `GamingSession`, UTC timestamp, calculated `TimeSpan Duration`, and `SessionTerminationReason`.
   - `ITimeTrackerService`: Session coordination contract exposing `SessionStarted`, `SessionUpdated`, and `SessionEnded` events, active session lookups, and startup recovery methods.

3. **Time Tracking Engine (`GameGuard.Infrastructure.Tracking`)**:
   - `TimeTrackerService`:
     - Subscribes to process monitor lifecycle events (`ProcessStartedAsync`, `ProcessRunningAsync`, `ProcessExitedAsync`).
     - **Session Creation**: Creates a new `GamingSession` on process arrival, writes to SQLite, and initializes thread-safe in-memory tracking. Prevents duplicate session creation for the same PID.
     - **Monotonic Duration Tracking**: Measures exact elapsed time delta-by-delta using `IMonotonicClock`.
     - **Periodic Checkpointing**: Automatically flushes active gameplay duration to SQLite at a configurable checkpoint interval (default 15 seconds) to avoid database write contention.
     - **Process Exit Finalization**: On game exit, captures final elapsed seconds, persists the closed session with `EndTimeUtc`, records `SessionTerminationReason.UserClosed`, and cleans up tracking state.
     - **Sleep & Hibernation Detection**: Evaluates consecutive polling deltas. If a delta exceeds the suspension threshold (e.g. laptop lid closed or Windows sleep), clamps the interval to prevent sleep time from falsely counting as gaming time.

4. **Startup Session Recovery & Reused-PID Safeguards**:
   - `ReconcileOrphanedSessionsAsync`:
     - On startup, queries all unclosed sessions in SQLite (`EndTimeUtc IS NULL`).
     - Scans running OS processes and verifies both Process ID AND process executable identity matching the game profile.
     - **Reused PID Protection**: If a process with the same PID exists but has a different executable name (Windows reused the PID for another app), the session is identified as stale rather than recovered.
     - **Stale Finalization**: Orphaned sessions whose processes died during an unexpected shutdown are marked with `SessionTerminationReason.CrashOrExternal` and their estimated end timestamp.
     - **Ongoing Session Recovery**: Surviving game processes are re-attached to the active tracking dictionary and registered with `ProcessMonitor` to prevent duplicate start events.

5. **Concurrency & Thread Safety**:
   - `ConcurrentDictionary<int, ActiveSessionTracker>` for independent tracking of multiple simultaneous games (e.g. CS2 and Valorant running concurrently).
   - Dedicated `_dbWriteLock` (`SemaphoreSlim`) serializing SQLite write operations, guaranteeing zero `SQLITE_BUSY` contention during concurrent game heartbeats.

6. **Repository & Pipeline Enhancements**:
   - Extended `ISessionRepository.EndSessionAsync` and `SqliteSessionRepository.EndSessionAsync` with an optional `durationSeconds` parameter to ensure the exact final duration is persisted atomically upon session close.
   - Enhanced `IProcessMonitor` with async event delegates (`ProcessStartedAsync`, `ProcessRunningAsync`, `ProcessExitedAsync`) and `RegisterTrackedProcess`.

### Test Suite Results (`GameGuard.Core.Tests`)
Added 16 new automated tests in `TimeTrackerServiceTests`:
1. `Lifecycle_StartingMonitoredGame_CreatesSessionInDatabaseAndMemory`
2. `Lifecycle_RunningGame_UpdatesSessionDuration`
3. `Lifecycle_ExitingGame_FinalizesSessionInDatabaseAndMemory`
4. `Lifecycle_DuplicateProcessStartedEvent_DoesNotCreateDuplicateSession`
5. `Lifecycle_UnmonitoredProcess_DoesNotCreateSession`
6. `TimeTracking_PeriodicHeartbeat_PersistsAtConfiguredInterval`
7. `TimeTracking_Exit_PersistsFinalDurationEvenIfCheckpointNotReached`
8. `TimeTracking_SystemClockJump_DoesNotCorruptMonotonicElapsedCalculations`
9. `TimeTracking_SleepOrSuspensionGap_IsClampedToAvoidInflatedPlaytime`
10. `TimeTracking_LongRunningSession_MaintainsAccuracy` (10-hour 3600-cycle session)
11. `Recovery_ValidActiveSession_IsRecoveredOnStartup`
12. `Recovery_StaleSession_IsFinalizedOnStartup`
13. `Recovery_ReusedPidWithDifferentProcessName_IsRecognizedAsStaleAndFinalized`
14. `Recovery_DoesNotCreateDuplicateSessions_WhenProcessScannedMultipleTimes`
15. `Concurrency_MultipleGamesSimultaneously_AreTrackedIndependently`
16. `Concurrency_ConcurrentLifecycleEvents_MaintainConsistency` (10 simultaneous processes)

- **Total Test Suite**: **49 passed, 0 failed, 0 skipped (Duration: ~780 ms)**.

### Verification Commands
```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build GameGuard.slnx
dotnet test GameGuard.slnx
```

### Known Limitations
- Stage 3 focuses exclusively on time tracking and session persistence. Daily limits, midnight rollovers, remaining quota calculations, and warning thresholds are not evaluated dynamically at runtime (scheduled for Stage 4).
- Soft warnings, hard game termination, and relaunch suppression are scheduled for Stages 5 and 6.
