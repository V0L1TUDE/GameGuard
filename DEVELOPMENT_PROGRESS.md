# GameGuard Development Progress

## Official 12-Stage Development Roadmap

### Phase 1: Core Foundation
- [x] **Stage 1: Solution Architecture, Domain Core & SQLite Persistence**
- [x] **Stage 2: Anti-Cheat-Safe Process Detection & Monitoring**

### Phase 2: Time Tracking & Limit Management
- [x] **Stage 3: Time Tracking Engine & Session Lifecycle Management**
- [x] **Stage 4: Daily Limit Management & Usage Calculation**

### Phase 3: Enforcement & Protection
- [x] **Stage 5: Game Enforcement Engine**
- [x] **Stage 6: Relaunch Suppression, Admin Override & Recovery**

### Phase 4: Desktop Application
- [x] **Stage 7: WPF Application Foundation & MVVM Architecture**
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

---

## Stage 5: Game Enforcement Engine

### Status: COMPLETED

### Objectives
Implement safe, reliable, and anti-cheat compliant process termination for monitored games when daily limits expire. Support a two-phase termination strategy (graceful `WM_CLOSE` followed by controlled `PROCESS_TERMINATE`), prevent accidental system or unrelated process termination, safeguard against Windows PID reuse, automatically react to `QuotaExhausted` events, and persist session termination states.

### Components Implemented
1. **Domain Models & Event Contracts (`GameGuard.Core.Models`)**:
   - `EnforcementMethod`: Enum specifying `GracefulClose`, `ForcedTermination`, or `Failed`.
   - `EnforcementResult`: Record capturing `GameProfileId`, `GameName`, `ProcessId`, `ProcessName`, `Success`, `MethodUsed`, `TimestampUtc`, and `ErrorMessage`.
   - `GameEnforcedEventArgs`: Event payload passed whenever an enforcement action completes.

2. **Core Abstractions (`GameGuard.Core.Interfaces`)**:
   - `IProcessTerminator`: Abstraction for Win32 process termination with graceful timeouts and cancellation support.
   - `IEnforcementService`: High-level service managing enforcement lifecycle, event subscriptions, safety checks, and session termination updates.

3. **Safe Win32 Native Implementations (`GameGuard.Infrastructure.Native`)**:
   - `SafeProcessTerminateHandle`: Custom RAII safe handle wrapping `OpenProcess` handles with `CloseHandle`.
   - `Win32ProcessTerminator`:
     - **Two-Phase Enforcement**:
       - *Phase 1 (Graceful)*: Enumerates target process top-level windows using `EnumWindows` and posts non-blocking `WM_CLOSE (0x0010)` messages via `PostMessage`. Waits up to `gracefulTimeout` using `WaitForSingleObject` with `SYNCHRONIZE (0x00100000)` access rights.
       - *Phase 2 (Controlled Forced Termination)*: If the process does not terminate within the graceful window, requests minimal permission `PROCESS_TERMINATE (0x0001)` and issues `TerminateProcess`.
     - **Anti-Cheat Safe**: Requests only `SYNCHRONIZE` and `PROCESS_TERMINATE`. Never requests `PROCESS_ALL_ACCESS`, `PROCESS_VM_WRITE`, or `PROCESS_VM_READ`. Injects no threads, modifies no game code, and requires no kernel drivers (100% compliant with Riot Vanguard, Valve Anti-Cheat, Easy Anti-Cheat, and BattlEye).

4. **Enforcement Coordinator Service (`GameGuard.Infrastructure.Enforcement`)**:
   - `EnforcementService`:
     - Listens to `ILimitManagerService.QuotaExhausted`.
     - **Critical System PID Safeguard**: Immediately refuses to terminate PIDs `<= 4` (System, Idle).
     - **Windows Reused PID Safeguard**: Scans running processes via `IProcessScanner` prior to termination. Verifies that the PID exists and that its executable matches `GameProfile.MatchesProcess(processName)`. If Windows recycled the PID for an unrelated process after the game exited, enforcement is safely aborted.
     - **Database Integration**: Transitions the active session in SQLite to `SessionTerminationReason.LimitEnforced` with detailed explanatory audit notes.
     - **Event Emission**: Dispatches `GameEnforced` to notify interested observers.

5. **Dependency Injection**:
   - Added `AddGameGuardEnforcement` to `ServiceCollectionExtensions`.

### Test Suite Results (`GameGuard.Core.Tests`)
Added 9 automated unit/integration tests in `EnforcementServiceTests`:
1. `EnforceGameAsync_WithGracefulExit_ReturnsGracefulSuccessAndRecordsLimitEnforcedInDb`: Verifies graceful window close and SQLite record update.
2. `EnforceGameAsync_WhenGracefulFails_ForcesTerminationAndRecordsLimitEnforced`: Verifies forced termination fallback when graceful timeout expires.
3. `EnforceGameAsync_WhenProcessAlreadyExited_ReturnsGracefulSuccessWithoutCallingTerminator`: Verifies handling when the game closes immediately prior to enforcement.
4. `EnforceGameAsync_WhenTargetPidIsSystem_AbortsAndRefusesToTerminate`: Verifies that critical Windows system PIDs are never targeted.
5. `EnforceGameAsync_WhenProcessNameMismatched_AbortsDueToReusedPidSafeguard`: Verifies protection against recycled Windows PIDs.
6. `EnforceGameAsync_WhenTerminationFails_ReturnsFailedResult`: Verifies error reporting when termination fails.
7. `QuotaExhaustedEvent_TriggersAutomaticEnforcementForActiveGameSessions`: Verifies end-to-end integration from quota exhaustion to automatic game process termination.
8. `QuotaExhaustedEvent_WhenNoActiveSessions_DoesNotAttemptTermination`: Verifies no-op when games are not running.
9. `EnforcementService_StartAndStop_ManagesLifecycleAndSubscriptions`: Verifies subscription and lifecycle management.

- **Total Test Suite**: **71 passed, 0 failed, 0 skipped (Duration: ~950 ms)**.

### Verification Commands
```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build GameGuard.slnx
dotnet test GameGuard.slnx
```

### Known Limitations
- Stage 5 terminates running game processes when quota is exhausted. However, it does not prevent users from immediately relaunching the executable while quota remains exhausted. This is addressed in Stage 6 (Relaunch Suppression, Admin Override & Recovery).
- Desktop user interface (WPF MVVM) is scheduled for Stages 7, 8, and 9.


---

## Stage 6: Relaunch Suppression, Admin Override & Recovery

### Status: COMPLETED

### Objectives
Prevent casual relaunch bypasses when game limits are exhausted, enforce cooldown periods for aggressive relaunch spamming, provide cryptographic administrator authentication (PBKDF2 with HMAC-SHA256), establish an emergency admin override and bonus-time mechanism, and implement offline anti-tamper clock rollback detection.

### Components Implemented
1. **Domain Models & Event Contracts (`GameGuard.Core.Models`)**:
   - `OverrideType`: Enum specifying `BonusTime` (extra minutes), `TemporaryUnlock` (unconditional play window), and `EmergencyLock` (immediate lock regardless of quota).
   - `AdminOverride`: Domain model with lifetime, revocation tracking, and factory helpers.
   - `RelaunchBlockedEventArgs`: Event contract raised when an exhausted game launch is intercepted and terminated.
   - `RelaunchBlockRecord`: Audit model persisted to SQLite.
   - `ClockTamperEventArgs`: Event contract dispatched upon system clock rollback detection.
   - Updated `LimitCalculationResult`: Seamlessly supports active override states (`HasActiveOverride`, `ActiveOverrideType`, `BonusMinutesApplied`, `OverrideExpiresAtUtc`).

2. **Core Abstractions (`GameGuard.Core.Interfaces`)**:
   - `IAdminOverrideRepository`: Abstraction for persisting and retrieving active and historical overrides.
   - `IAdminAuthService`: Cryptographic admin password validation, modification, and brute-force lockout.
   - `IRelaunchBlockRepository`: Repository for logging and querying blocked launches.
   - `IRelaunchSuppressionService`: Coordination service listening to `ProcessStarted` events and executing fast suppression.
   - `IClockTamperDetector`: Offline anti-tamper watermark tracking for system clock manipulation.

3. **Limit Engine & Persistence Integration**:
   - `LimitCalculator`: Incorporates `AdminOverride` parameters. Correctly factors in `BonusTime` allowances, grants unconditional play for `TemporaryUnlock`, and forces immediate limit exhaustion for `EmergencyLock`.
   - `SqliteDatabaseInitializer`: Added `AdminOverrides`, `AppSettings`, and `RelaunchBlocks` tables with indexes.
   - `SqliteAdminOverrideRepository`: Queries active overrides with specific-game precedence over global overrides.
   - `SqliteRelaunchBlockRepository`: Stores and tallies relaunch block events.
   - `LimitManagerService`: Accepts `IAdminOverrideRepository` and `IClockTamperDetector` for real-time calculation and clock validation.

4. **Security & Protection Services (`GameGuard.Infrastructure.Security` & `GameGuard.Infrastructure.Enforcement`)**:
   - `SqliteAdminAuthService`:
     - Cryptographically secure PBKDF2 hashing (`Rfc2898DeriveBytes.Pbkdf2` using HMAC-SHA256, 100,000 iterations, 16-byte random salt, 32-byte hash).
     - Timing attack defense via `CryptographicOperations.FixedTimeEquals`.
     - Brute-force protection: Enforces a 5-minute lockout after 5 consecutive failed attempts.
   - `ClockTamperDetector`: Tracks the highest observed UTC timestamp and fires `ClockRollbackDetected` if system time is set backwards by more than 1 minute.
   - `RelaunchSuppressionService`:
     - Intercepts process launches of quota-exhausted games.
     - Fast termination via `IProcessTerminator` (50ms graceful timeout to prevent game window exposure).
     - Sliding window relaunch tracking (60s window).
     - Automated cooldown enforcement: 5-minute penalty after 3 rapid launch attempts.
     - Reconciles and zeroes any newly created session in SQLite.

5. **Dependency Injection**:
   - Added `AddGameGuardRelaunchSuppression` and updated `AddGameGuardSqlite` and `AddGameGuardLimitManagement` in `ServiceCollectionExtensions`.

### Test Suite Results (`GameGuard.Core.Tests`)
Added 17 new automated tests (88 total):
- `AdminAuthServiceTests`:
  1. `IsPasswordConfiguredAsync_WhenEmpty_ReturnsFalse`
  2. `SetPasswordAsync_AndValidatePasswordAsync_ValidatesCorrectPassword`
  3. `ValidatePasswordAsync_AfterFiveFailedAttempts_EnforcesLockout`
  4. `ChangePasswordAsync_WithValidCurrentPassword_UpdatesPassword`
- `ClockTamperDetectorTests`:
  5. `ValidateSystemTime_WithNormalForwardProgress_UpdatesWatermarkWithoutEvent`
  6. `ValidateSystemTime_WithMinorJitterWithinTolerance_DoesNotFireRollback`
  7. `ValidateSystemTime_WithRollbackBeyondTolerance_FiresRollbackEvent`
- `AdminOverrideRepositoryAndLimitTests`:
  8. `AddOverrideAsync_AndGetActiveOverrideForGameAsync_RetrievesActiveOverride`
  9. `RevokeOverrideAsync_DeactivatesOverrideImmediately`
  10. `LimitCalculator_WithEmergencyLockOverride_ForcesLimitExceededImmediately`
  11. `LimitCalculator_WithTemporaryUnlockOverride_AllowsPlayEvenIfQuotaExhausted`
  12. `LimitCalculator_WithBonusTimeOverride_AddsAllowedMinutesToQuota`
- `RelaunchSuppressionServiceTests`:
  13. `CheckAndSuppressIfExhaustedAsync_WhenWithinQuota_DoesNotSuppress`
  14. `CheckAndSuppressIfExhaustedAsync_WhenQuotaExhausted_SuppressesAndRecordsBlock`
  15. `CheckAndSuppressIfExhaustedAsync_WithRepeatedAttempts_EnforcesCooldown`
  16. `CheckAndSuppressIfExhaustedAsync_WhenActiveTemporaryUnlockOverrideExists_AllowsLaunch`
  17. `RelaunchSuppressionService_StartAndStop_ManagesLifecycleCorrectly`

- **Total Test Suite**: **88 passed, 0 failed, 0 skipped (Duration: ~1.0 s)**.

### Verification Commands
```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build GameGuard.slnx
dotnet test GameGuard.slnx
```

### Known Limitations
- Stage 6 completes Phase 3 (Enforcement & Protection). All core domain, monitoring, time-tracking, limit management, enforcement, and security engines are complete and operate entirely offline without UI dependencies.
- Next phase is **Phase 4: Desktop Application**, starting with **Stage 7: WPF Application Foundation & MVVM Architecture**.


---

## Stage 7: WPF Application Foundation & MVVM Architecture

### Status: COMPLETED

### Objectives
Scaffold the `GameGuard.UI` WPF desktop presentation layer on .NET 10 LTS (`net10.0-windows`), establish a pure MVVM foundation (`ObservableObject`, `RelayCommand`, `AsyncRelayCommand`, `NavigationService`), implement a modern dark gamer UI theme with cohesive styling and layout grids, integrate Microsoft Dependency Injection to wire up all background monitoring, limit evaluation, and enforcement engines, and construct the shell window (`MainWindow`).

### Components Implemented
1. **WPF Desktop Project (`src/GameGuard.UI`)**:
   - Configured for .NET 10 LTS (`net10.0-windows`) with `<UseWPF>true</UseWPF>`.
   - References `GameGuard.Core` and `GameGuard.Infrastructure`.
   - Added to solution structure in `GameGuard.slnx`.

2. **MVVM Foundation (`src/GameGuard.UI/Mvvm`)**:
   - `ObservableObject`: Lightweight `INotifyPropertyChanged` implementation with type-safe `SetProperty<T>` and `CallerMemberName`.
   - `RelayCommand`: Synchronous `ICommand` implementation with `CanExecuteChanged` integration with `CommandManager.RequerySuggested`.
   - `AsyncRelayCommand`: Asynchronous `ICommand` supporting `IsExecuting` tracking, reentrancy prevention, and centralized exception handling.
   - `INavigationService` & `NavigationService`: Decoupled view-model navigation resolving target ViewModels from `IServiceProvider` with automated `InitializeAsync` triggering and lifecycle disposal.
   - `ViewModelBase`: Base ViewModel with `Title`, `IsBusy`, `InitializeAsync`, and `Dispose` hooks.

3. **Modern Gamer Dark Theme (`src/GameGuard.UI/Themes`)**:
   - `Colors.xaml`: Deep obsidian background (`#0B0E14`), slate surface cards (`#131722`), elevated hover states (`#1A2030`), crisp borders (`#252C40`), and vibrant gamer accents (cyber emerald `#00E676`, electric cyan `#00D2FF`, coral red `#EF4444`, amber `#F59E0B`).
   - `Styles.xaml`: Reusable control templates for cards (`CardBorderStyle`, `ElevatedCardBorderStyle`), navigation buttons (`NavButtonStyle`), primary and outline action buttons, and typography presets.

4. **Shell Window & View Hierarchy (`src/GameGuard.UI/Views` & `src/GameGuard.UI/ViewModels`)**:
   - `MainWindow.xaml`: Modern desktop application shell featuring a left sidebar navigation rail, brand shield icon, live engine status footer (Vanguard & VAC anti-cheat safety badge), top status bar, and central `ContentPresenter`.
   - `MainViewModel`: Coordinates navigation across pages, tracks live active game session counts via `ITimeTrackerService`, and updates status indicators.
   - Page ViewModels & Views:
     - `DashboardViewModel` / `DashboardView`: KPI metric cards (monitored games, active sessions, time used today) and live game allowance progress bars.
     - `GamesViewModel` / `GamesView`: Monitored games library view.
     - `LimitsViewModel` / `LimitsView`: Daily allowance configurations.
     - `HistoryViewModel` / `HistoryView`: Session logs and blocked relaunch audit trail.
     - `SettingsViewModel` / `SettingsView`: PBKDF2 password security status and active overrides.

5. **Application Hosting & Dependency Injection (`src/GameGuard.UI/App.xaml` & `App.xaml.cs`)**:
   - Configures SQLite database path in local application data (`%LOCALAPPDATA%\GameGuard\gameguard.db`).
   - Registers all infrastructure singletons and UI transient view models.
   - Bootstraps SQLite database initialization on startup.
   - Asynchronously starts all background engines (`IProcessMonitor`, `ITimeTrackerService`, `ILimitManagerService`, `IEnforcementService`, `IRelaunchSuppressionService`).
   - Ensures graceful shutdown of background services upon exit.

### Test Suite Results (`GameGuard.Core.Tests`)
Added 8 new automated unit tests (96 total):
- `MvvmTests`:
  1. `ObservableObject_SetProperty_FiresPropertyChangedOnlyWhenValueChanged`: Verifies property change notifications and equality optimization.
  2. `RelayCommand_ExecutesAndRespectsCanExecute`: Verifies predicate checking and execution.
  3. `AsyncRelayCommand_TracksIsExecutingAndExecutesTask`: Verifies asynchronous execution and reentrancy locking.
  4. `AsyncRelayCommand_HandlesExceptionViaCallback`: Verifies exception forwarding to callbacks.
  5. `NavigationService_NavigatesAndManagesLifecycle`: Verifies view-model navigation, initialization, and disposal of previous views.
- `MainViewModelTests`:
  6. `MainViewModel_InitializesAndNavigatesToDashboard`: Verifies startup navigation to dashboard.
  7. `MainViewModel_NavigationCommands_SwitchCurrentViewModel`: Verifies navigation commands switch to Games, Limits, History, and Settings.
  8. `MainViewModel_UpdatesActiveSessionCountsLive`: Verifies active session tracking.

- **Total Test Suite**: **96 passed, 0 failed, 0 skipped (Duration: ~690 ms)**.

### Verification Commands
```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build GameGuard.slnx
dotnet test GameGuard.slnx
```

### Known Limitations
- Stage 7 provides the foundational shell, navigation infrastructure, themes, and read views for the MVVM desktop client. Interactive dialogs for creating/editing game profiles, setting custom daily limits, and entering admin override PINs are implemented in Stage 8.
