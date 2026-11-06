# GameGuard

GameGuard is a lightweight, offline-first Windows desktop application and background service designed to monitor gaming applications, track actual runtime, and enforce user-configurable daily limits (individual game allowances and shared pools).

---

## Architectural Principles & Security Stance

- **Anti-Cheat Safe**: GameGuard strictly operates out-of-process via standard Windows Win32 / Toolhelp / Process APIs. It **never** injects code, hooks routines, modifies game binaries, or deploys kernel drivers. It is completely safe with Vanguard, Valve Anti-Cheat (VAC), Easy Anti-Cheat (EAC), and BattlEye.
- **Resource Efficient**: Designed to stay within a minimal memory footprint (15–35 MB background service, 50–120 MB full WPF desktop client) with negligible idle CPU utilization.
- **Offline Persistent**: Uses local SQLite in Write-Ahead Logging (WAL) mode with zero external cloud dependencies.
- **Modular Monolith**: Clean separation between domain abstractions (`GameGuard.Core`), persistence & infrastructure (`GameGuard.Infrastructure`), monitoring engine, and presentation layer.

---

## Solution Structure

```
├── GameGuard.slnx                     # Modern .NET 10 Solution definition
├── src/
│   ├── GameGuard.Core/                # Domain models, calculation logic, contracts
│   │   ├── Models/                    # GameProfile, DailyLimit, GamingSession, LimitCalculationResult
│   │   ├── Interfaces/                # IGameProfileRepository, ILimitRepository, ISessionRepository, ILimitCalculator
│   │   └── Services/                  # LimitCalculator, SystemClock
│   └── GameGuard.Infrastructure/      # SQLite persistence and DI registration
│       ├── Persistence/               # Connection factory (WAL mode), Database Initializer, Repositories
│       └── DependencyInjection/       # ServiceCollectionExtensions
└── tests/
    └── GameGuard.Core.Tests/          # xUnit test suite (LimitCalculator, Profile matching, SQLite repos)
```

---

## Prerequisites & Setup

1. **Operating System**: Windows 10 / Windows 11 (x64)
2. **Framework**: .NET 10 LTS SDK (10.0.403+)
   - To verify: `dotnet --version`
   - If installed in user directory: `$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"`

---

## Build & Test Instructions

### Building the Solution
```powershell
dotnet build GameGuard.slnx
```

### Running Tests
```powershell
dotnet test GameGuard.slnx
```
All unit tests and SQLite persistence integration tests run in memory / temp databases without side effects on host configuration.
