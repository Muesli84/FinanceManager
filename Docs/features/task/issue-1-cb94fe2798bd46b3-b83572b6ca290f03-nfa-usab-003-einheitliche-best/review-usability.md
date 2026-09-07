# Usability Review — NFA-USAB-003

## Requirement Coverage

| Requirement | Status | Notes |
|-------------|--------|-------|
| Centralized confirmation mechanism | ✅ | `IConfirmationService` / `ConfirmationService` in `FinanceManager.Web.Services` |
| Persisted per-user `ShowConfirmations` setting, default `true` | ✅ | `User.ShowConfirmations` defaults to `true`; EF mapping `HasDefaultValue(true)`; migration `AddShowConfirmationsToUser` |
| Profile UI exposes the setting | ✅ | `SetupProfileTab` + `SetupProfileViewModel` bind to `ShowConfirmations` |
| Suppress dialogs when disabled | ✅ | `ConfirmationService.ConfirmAsync` returns `true` immediately when `ShowConfirmations == false` |
| Confirm destructive / irreversible / high-impact actions | ✅ | Delete, archive, merge, book/finalize, reset, update install, and mass actions covered (see `inventory.md`) |
| No confirmations for navigation, edit forms, reversible saves | ✅ | Only destructive actions invoke `ConfirmAsync` |
| Avoid duplicate dialogs | ✅ | Each action has a single `ConfirmAsync` call; no nested confirmation paths detected |
| Localized text with German fallback | ✅ | Titles/messages use `.resx` keys; missing keys fall back to the resource name |
| Focus Cancel button on open | ✅ | `ConfirmDialog` calls `_cancelButtonRef.FocusAsync()` on first render |

## User-Facing Behavior

### Dialog Flow

1. Action handler calls `await ConfirmationService.ConfirmAsync(...)`.
2. Service reads `ShowConfirmations` once and caches it.
3. If disabled, the action proceeds without a dialog.
4. If enabled, `ConfirmationDialogHost` renders `ConfirmDialog` with localized title, message, and severity class.
5. The dialog displays **Confirm** (primary) and **Cancel** (focused by default) buttons plus a close icon.
6. Clicking **Cancel**, the backdrop, or the close icon returns `false`; **Confirm** returns `true`.

### Global Setting

- Path: Setup → Profile → "Bestätigungsdialoge anzeigen".
- Default: `true` for all new users.
- Changing and saving the profile invalidates the in-memory confirmation cache (`SetupProfileViewModel.InvalidateConfirmationCache`).

### Accessibility

- `role="dialog"`, `aria-modal="true"`, `aria-labelledby`, and `aria-describedby` are present.
- Initial focus is on the **Cancel** button, reducing accidental destructive activation.

## Risk Areas

1. **Mass actions**: `StatementDraftsListViewModel` and `HomeViewModel` now ask once per mass operation, but the user still confirms each selected item as a single batch. This matches the requirement and prevents accidental bulk operations.
2. **Update install**: Previously used `Js.InvokeAsync<bool>("confirm", ...)`. Migrated to `ConfirmationService.ConfirmAsync` so it honors the global setting and the unified dialog.
3. **API failure**: If `UserSettings_GetProfileAsync` fails, the service defaults to `ShowConfirmations = true` (ask the user) rather than silently proceeding.

## Open Points

- E2E coverage was added with `ConfirmationDialogE2ETests`, which verifies the dialog renders, can be cancelled, and performs the delete on confirm. Keyboard-focus behavior is covered by the `ConfirmDialogTests` bUnit tests; a dedicated Playwright focus assertion can be added later if required.
