# GameGuard Development Progress

## Official 12-Stage Development Roadmap

### Phase 1: Core Foundation
- [x] **Stage 1: Solution Architecture, Domain Core & SQLite Persistence**
- [x] **Stage 2: Anti-Cheat-Safe Process Detection & Monitoring**

### Phase 2: Time Tracking & Limit Management
- [x] **Stage 3: Time Tracking Engine & Session Lifecycle Management**
- [x] **Stage 4: Daily Limit Management & Usage Calculation**

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

### Achievements
- `IMonotonicClock` / `MonotonicClock`: High-resolution monotonic time measurement (`Stopwatch.GetTimestamp()` / `Stopwatch.GetElapsedTime()`) immune to wall-clock time shifts or Daylight Saving Time changes.
- `TimeTrackerService`: Subscribes to process lifecycle events, creates sessions on launch, tracks elapsed gameplay, flushes heartbeats periodically to SQLite (default 15s interval), and concludes sessions upon game exit.
- Sleep & Hibernation Protection: Detects abnormal tick deltas and clamps elapsed time to prevent Windows sleep from counting as active gameplay.
- Startup Session Recovery: Reconciles unclosed sessions on startup, detects and finalizes stale sessions, and validates process executable names to defend against Windows PID reuse.
- Concurrency & Thread Safety: Multi-game concurrent tracking isolated by PID; serialized SQLite writes via `SemaphoreSlim`.
- Expanded test suite to 49 passing tests.

---

## Stage 4: Daily Limit Management & Usage Calculation

### Status: COMPLETED

### Objectives
Provide automated daily quota management, real-time usage aggregation combining SQLite completed history with in-memory active gameplay, local timezone and midnight boundary calculations, warning threshold notifications, grace-period monitoring, and quota exhaustion detection.

### Components Implemented
1. **Day Boundary Management (`GameGuard.Core.Interfaces` & `GameGuard.Infrastructure.Limits`)**:
   - `IDayBoundaryCalculator`: Interface for computing local/UTC calendar boundaries.
   - `DayBoundaryCalculator`: Converts UTC timestamps to the target local timezone, evaluates custom reset hours (0–23), accounts for Daylight Saving Time gaps/overlaps, and returns exact `[StartUtc, EndUtc)` ranges.
   - Accurately assigns times before the reset hour (e.g. 03:00 AM when reset is 04:00 AM) to the previous cycle.

2. **Event Contracts (`GameGuard.Core.Models`)**:
   - `LimitWarningEventArgs`: Triggered when remaining playtime crosses configured warning thresholds (e.g., 15m, 5m, 1m).
   - `GracePeriodEventArgs`: Triggered when quota is exhausted and grace countdown is active (`GracePeriodStarted` and periodic `GracePeriodUpdated`).
   - `QuotaExhaustedEventArgs`: Triggered when both daily allowance and grace period have expired (`QuotaExhausted`).
   - `DayRolloverEventArgs`: Triggered when the clock crosses the reset boundary (`DayRolloverOccurred`).

3. **Limit Manager Service (`GameGuard.Infrastructure.Limits`)**:
   - `LimitManagerService`:
     - Implements `ILimitManagerService`.
     - Integrates `IGameProfileRepository`, `ILimitRepository`, `ISessionRepository`, `ITimeTrackerService`, `ILimitCalculator`, `IDayBoundaryCalculator`, and `ISystemClock`.
     - **Usage Aggregation**: Combines completed session seconds today from SQLite (`WHERE EndTimeUtc IS NOT NULL`) with authoritative live duration from `ITimeTrackerService`, preventing any double-counting of checkpointed active sessions.
     - **Dual Limit Evaluation**: Automatically determines whether individual or shared limits bind for a game.
     - **One-Shot Threshold Warnings**: Tracks which warning thresholds have already been emitted per game to prevent duplicate alerts during consecutive ticks.
     - **Grace Period State Machine**: Emits `GracePeriodStarted` exactly once on transition, followed by `GracePeriodUpdated` on subsequent ticks.
     - **Automatic Midnight Rollover**: Automatically resets fired warning flags, clears grace states, and raises `DayRolloverOccurred` when midnight or the custom reset hour passes.
     - **Asynchronous Event-Driven Evaluation**: Listens to `SessionUpdated` events from `TimeTrackerService` and operates a background evaluation loop.

4. **Repository Refinements**:
   - Updated `SqliteSessionRepository.GetTotalSecondsUsedForGameTodayAsync` and `GetTotalSharedSecondsUsedTodayAsync` to query `AND EndTimeUtc IS NOT NULL`, properly separating completed historical sessions from active live tracking.

5. **Dependency Injection**:
   - Added `AddGameGuardLimitManagement` to `ServiceCollectionExtensions`.

### Test Suite Results (`GameGuard.Core.Tests`)
Added 13 new automated tests:
- `DayBoundaryCalculatorTests`:
  1. `GetDayBoundary_InUtcAtMidnight_CalculatesAccurateDayBounds`
  2. `GetDayBoundary_WhenTimeIsBeforeResetHour_BelongsToPreviousDayCycle`
  3. `GetDayBoundary_WhenTimeIsAfterResetHour_BelongsToCurrentDayCycle`
  4. `GetDayBoundary_WithNonUtcTimeZone_ConvertsAccurately`
- `LimitManagerServiceTests`:
  5. `GetCurrentStatus_WithIndividualLimitOnly_CalculatesUsageAndRemainingCorrectly`
  6. `GetCurrentStatus_WithSharedLimitOnly_AggregatesAcrossAllGames`
  7. `GetCurrentStatus_WithDualLimits_IdentifiesBindingConstraint`
  8. `CheckLimits_WhenApproachingThresholds_FiresLimitWarningIssuedOncePerThreshold`
  9. `CheckLimits_WhenTimeExpires_FiresGracePeriodStartedAndUpdated`
  10. `CheckLimits_WhenGracePeriodExhausted_FiresQuotaExhausted`
  11. `CheckLimits_WhenDayRolloverOccurs_FiresDayRolloverOccurredAndResetsWarnings`
  12. `GetAllCurrentStatuses_ReturnsStatusForEveryMonitoredGame`
  13. `LimitManagerService_StartAndStop_ManagesLifecycleCorrectly`

- **Total Test Suite**: **62 passed, 0 failed, 0 skipped (Duration: ~740 ms)**.

### Verification Commands
```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build GameGuard.slnx
dotnet test GameGuard.slnx
```

### Known Limitations
- Stage 4 computes limits, emits warnings, and detects quota exhaustion. It deliberately does not perform process termination or kill game processes (scheduled for Stage 5: Game Enforcement Engine).
- Relaunch suppression and administrator override tokens are scheduled for Stage 6.
- Desktop UI (WPF MVVM) is scheduled for Stages 7, 8, and 9.
