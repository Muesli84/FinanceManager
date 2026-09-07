# Code Review — NFA-USAB-003

## Architecture

### Central Service

- `IConfirmationService` defines a small, Blazor-friendly contract: `ConfirmAsync`, `SetResult`, `Cancel`, `InvalidateCacheAsync`, `CurrentRequest`, `OnShow`, `OnChanged`.
- `ConfirmationService` is registered as scoped in `ProgramExtensions.cs` (`AddScoped<IConfirmationService, ConfirmationService>`).
- `NullConfirmationService` provides a headless fallback for unit tests and view models that resolve the service from a bare `ServiceCollection`.

### Dialog Hosting

- `ConfirmationDialogHost` is placed in `MainLayout.razor` and renders `ConfirmDialog` whenever `IConfirmationService.CurrentRequest` is not null.
- `ConfirmDialog` is a stateless parameter component. It focuses the Cancel button on first render, uses localized resource keys, and applies a severity CSS class.
- `OnChanged` event ensures the host re-renders when a request is shown, confirmed, or cancelled, preventing the dialog from staying on screen after an action.

### View-Model Integration

- `BaseViewModel` and `ViewModelBase` expose a lazy `ConfirmationService` property that falls back to `NullConfirmationService.Instance` when no service is registered.
- Destructive action handlers follow a consistent pattern:

  ```csharp
  if (!await ConfirmationService.ConfirmAsync(new(
      TitleResourceKey: "Confirmation_Delete_Title",
      MessageResourceKey: "Confirmation_Delete_Message",
      Severity: ConfirmationSeverity.Critical)))
  {
      return false;
  }
  ```

## Data Layer

- `User.ShowConfirmations` default `true`, settable via `SetShowConfirmations(bool)`.
- EF mapping: `b.Property(x => x.ShowConfirmations).HasDefaultValue(true).IsRequired()`.
- Migration `20260907185551_AddShowConfirmationsToUser` adds the column with `defaultValue: true` and drops it on `Down`. Safe for existing installations — existing users retain `true`.
- API DTO/request objects include `ShowConfirmations` with a default of `true` for backwards compatibility.

## Tests

### New Tests

- `ConfirmationServiceTests` — 7 cases covering suppression, confirm/cancel, API-failure fallback, caching, invalidation, and `CurrentRequest` exposure.
- `ConfirmDialogTests` — 3 bUnit cases for rendering, confirm, and cancel callbacks.
- `SetupProfileViewModelTests.Save_Persists_ShowConfirmations` — verifies the setting round-trips through the view model.
- `ApiClientUserSettingsTests` integration cases — default `true` and update/readback `false`.

### Test Stabilization

- Updated `CardPageTests`, `HomeKpiGridTests`, and `SetupUpdateTabTests` to register `IConfirmationService` because the tested components inject it.
- `SetupUpdateTabTests` mocks `ConfirmAsync` to return `true` so update-install tests can proceed without a real dialog.

## Findings

| # | Finding | Severity | Status |
|---|---------|----------|--------|
| 1 | `ConfirmDialog` had no host-driven re-render on `SetResult`/`Cancel` — dialog could remain visible after user action. | Major | Fixed by adding `IConfirmationService.OnChanged` and subscribing in `ConfirmationDialogHost`. |
| 2 | `SetupUpdateTab` still used `Js.InvokeAsync<bool>("confirm", ...)` bypassing the global setting. | Major | Migrated to `ConfirmationService.ConfirmAsync`. |
| 3 | Existing view-model tests would fail with `GetRequiredService<IConfirmationService>` not registered. | Minor | Fixed by using `GetService<IConfirmationService>()` with `NullConfirmationService` fallback. |
| 4 | `ConfirmationRequest.cs` had an unresolved `cref` to `ConfirmDialog`. | Trivial | Fixed by rewording the XML doc comment. |

## Quality Gates

- [x] Solution builds with 0 errors.
- [x] `FinanceManager.Tests` passes (1281 tests).
- [x] `FinanceManager.Tests.Integration` passes (126 tests).
- [x] Migration reviewed for data safety.
- [x] No remaining `Js.InvokeAsync<bool>("confirm", ...)` calls in production code.
- [x] `IConfirmationService` injected components are covered by existing component tests.

## Recommendations (non-blocking)

1. ~~Add an explicit `ConfirmationDialogHost` component test that simulates show/confirm/hide.~~ Done — `ConfirmationDialogHostTests` now covers the host re-render behavior.
2. Consider extending the E2E test to toggle `ShowConfirmations` and verify the dialog is suppressed across a real action.
3. Document the new `ConfirmationService` usage pattern in `AGENTS.md` or the project programming guidelines (see generated `AGENTS.md`).
