# Test Results — NFA-USAB-003

Run performed after implementation of the confirmation-dialog feature.

## Build

| Command | Result | Warnings | Errors |
|---------|--------|----------|--------|
| `dotnet build FinanceManager.sln --no-restore` | Success | NU1510 (existing, shared-framework packages), xUnit analyzer suggestions | 0 |
| `dotnet build FinanceManager.Web.csproj --no-restore` | Success | NU1510 only | 0 |
| `dotnet build FinanceManager.Tests.csproj --no-restore` | Success | xUnit analyzer suggestions (existing + new xUnit1051 in `ConfirmationServiceTests` for `ConfirmAsync` CancellationToken calls) | 0 |
| `dotnet build FinanceManager.Tests.Integration.csproj --no-restore` | Success | xUnit2029 (existing) | 0 |

> Warnings are not treated as errors in the test projects; only `CS1591` and `NU1605` are configured as errors.

## Unit / Component Tests — FinanceManager.Tests

```
Total: 1284
Passed: 1284
Failed: 0
Skipped: 0
Duration: ~35 s
```

New and updated test classes executed successfully:

- `FinanceManager.Tests.Services.ConfirmationServiceTests` — 10 tests
- `FinanceManager.Tests.Components.ConfirmDialogTests` — 3 tests
- `FinanceManager.Tests.ViewModels.SetupProfileViewModelTests` — extended with `Save_Persists_ShowConfirmations`
- `FinanceManager.Tests.Components.SetupUpdateTabTests` — updated to stub `IConfirmationService`
- `FinanceManager.Tests.Components.CardPageTests` — updated to stub `IConfirmationService`
- `FinanceManager.Tests.Components.HomeKpiGridTests` — updated to stub `IConfirmationService`

All existing view-model and component tests continue to pass with the new `IConfirmationService` integration and the `NullConfirmationService` fallback.

## Integration Tests — FinanceManager.Tests.Integration

```
Total: 126
Passed: 126
Failed: 0
Skipped: 0
Duration: ~34 s
```

Relevant coverage:

- `ApiClientUserSettingsTests` verifies that a newly registered user has `ShowConfirmations == true` and that updating the profile with `ShowConfirmations: false` persists and can be read back.

## E2E Tests

Not executed in this run. The E2E project (`FinanceManager.Tests.E2E`) builds successfully, but a full browser-driven run is outside the scope of this focused feature implementation.

## Notable fixes during test stabilization

1. `ConfirmationService.ConfirmAsync` was clarified to default to `ShowConfirmations = true` when the API call fails; tests now attach an `OnShow` handler that calls `SetResult(true)` so the async flow completes.
2. `BaseViewModel` and `ViewModelBase` were updated to fall back to `NullConfirmationService` when `IConfirmationService` is not registered, keeping existing unit tests green without adding mocks to every test fixture.
3. `CardPageTests`, `HomeKpiGridTests`, and `SetupUpdateTabTests` were updated to register `IConfirmationService` because the corresponding components use `[Inject]` for the service.
4. `SetupUpdateTab.razor` was migrated from `Js.InvokeAsync<bool>("confirm", ...)` to `ConfirmationService.ConfirmAsync` so it participates in the global suppression setting.
