# Test Results — NFA-USAB-003

Run performed after the fix for non-functional delete confirmations and the addition of E2E coverage.

## Build

| Command | Result | Warnings | Errors |
|---------|--------|----------|--------|
| `dotnet build FinanceManager.sln --no-restore` | Success | NU1510 (existing, shared-framework packages), xUnit analyzer suggestions | 0 |
| `dotnet build FinanceManager.Web.csproj --no-restore` | Success | NU1510 only | 0 |
| `dotnet build FinanceManager.Tests.csproj --no-restore` | Success | xUnit analyzer suggestions (existing) | 0 |
| `dotnet build FinanceManager.Tests.Integration.csproj --no-restore` | Success | xUnit2029 (existing) | 0 |
| `dotnet build FinanceManager.Tests.E2E.csproj --no-restore` | Success | NU1510 only | 0 |

> Warnings are not treated as errors in the test projects; only `CS1591` and `NU1605` are configured as errors.

## Unit / Component Tests — FinanceManager.Tests

```
Total: 1285
Passed: 1285
Failed: 0
Skipped: 0
Duration: ~36 s
```

New and updated test classes executed successfully:

- `FinanceManager.Tests.Services.ConfirmationServiceTests` — 10 tests
- `FinanceManager.Tests.Components.ConfirmDialogTests` — 3 tests
- `FinanceManager.Tests.Components.ConfirmationDialogHostTests` — 1 test
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
Duration: ~47 s
```

Relevant coverage:

- `ApiClientUserSettingsTests` verifies that a newly registered user has `ShowConfirmations == true` and that updating the profile with `ShowConfirmations: false` persists and can be read back.

## E2E Tests — FinanceManager.Tests.E2E

A focused E2E run was executed against a real browser instance:

```
Total: 1
Passed: 1
Failed: 0
Skipped: 0
Duration: ~3 s
```

- `FinanceManager.Tests.E2E.ConfirmationDialogE2ETests.AccountDelete_RibbonAction_ShowsConfirmationAndDeletesOnConfirm`
  - Opens an account card, clicks the ribbon Delete button, asserts the confirmation dialog appears.
  - Cancelling the dialog keeps the account on the card page.
  - Confirming the dialog deletes the account and navigates back to the account list.

A regression check of an existing E2E test also passed:

- `FinanceManager.Tests.E2E.AccountsOverviewStatisticsPlaywrightTests.AccountsOverview_ShowsStatisticsAlongsideTable`

## Notable fixes during test stabilization

1. `Routes.razor` was set to `@rendermode InteractiveServer` so that `ConfirmationDialogHost` and routed pages share the same Blazor circuit and the same scoped `IConfirmationService` instance.
2. `CardPage.razor` no longer calls `ConfirmAsync` itself; deletion confirmations are handled exclusively by the view-model's `DeleteAsync`, avoiding a double-dialog bug.
3. `ConfirmationService` now creates its `TaskCompletionSource` with `TaskCreationOptions.RunContinuationsAsynchronously` to prevent synchronous re-entry into the Blazor UI thread when a dialog result is set.
4. `BaseViewModel` and `ViewModelBase` fall back to `NullConfirmationService` when `IConfirmationService` is not registered, keeping existing unit tests green without adding mocks to every test fixture.
5. `SetupUpdateTab.razor` was migrated from `Js.InvokeAsync<bool>("confirm", ...)` to `ConfirmationService.ConfirmAsync` so it participates in the global suppression setting.
