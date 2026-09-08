## `BackgroundTaskType`
Datei: `FinanceManager.Shared/Dtos/Admin/BackgroundTaskType.cs`

| Wert | Bedeutung |
|------|-----------|
| `ClassifyAllDrafts` | Klassifizierung von Kontoauszugsentwürfen |
| `BookAllDrafts` | Buchung von Kontoauszugsentwürfen |
| `BackupRestore` | Backup-Wiederherstellung |
| `SecurityPricesBackfill` | Kursdaten-Historienauffüllung |
| `RebuildAggregates` | Neuaufbau von Aggregaten |
| `RefreshBudgetReportCache` | Aktualisierung Budget-Report-Cache |
| `CreateDemoData` | Demo-Daten-Erzeugung für neu registrierten Erstbenutzer |

## `BackgroundTaskStatus`
Datei: `FinanceManager.Shared/Dtos/Admin/BackgroundTaskStatus.cs`

| Wert | Bedeutung |
|------|-----------|
| `Queued` | Wartet auf Ausführung |
| `Running` | Wird ausgeführt |
| `Completed` | Erfolgreich abgeschlossen |
| `Failed` | Mit Fehler beendet |
| `Cancelled` | Abgebrochen |

## `ContactType`
Datei: `FinanceManager.Shared/Dtos/Contacts/ContactType.cs`

| Wert | Bedeutung |
|------|-----------|
| `Self` | Eigener Kontakt des Benutzers |
| `Bank` | Bankkontakt |
| `Person` | Natürliche Person |
| `Organization` | Organisation/Firma |
| `Other` | Sonstiger Typ |

## `AccountType`
Datei: `FinanceManager.Shared/Dtos/Accounts/AccountType.cs`

| Wert | Bedeutung |
|------|-----------|
| `Giro` | Girokonto |
| `Savings` | Sparkonto |

## `SavingsPlanExpectation`
Datei: `FinanceManager.Shared/Dtos/Accounts/SavingsPlanExpectation.cs`

| Wert | Bedeutung |
|------|-----------|
| `None` | Kein Sparplan erwartet |
| `Optional` | Sparplan optional |
| `Required` | Sparplan erforderlich |

## `SavingsPlanType`
Datei: `FinanceManager.Shared/Dtos/SavingsPlans/SavingsPlanType.cs`

| Wert | Bedeutung |
|------|-----------|
| `OneTime` | Einmaliger Sparplan |
| `Recurring` | Wiederkehrender Sparplan |
| `Open` | Unbefristeter Sparplan |

## `SavingsPlanInterval`
Datei: `FinanceManager.Shared/Dtos/SavingsPlans/SavingsPlanInterval.cs`

| Wert | Bedeutung |
|------|-----------|
| `Monthly` | Monatlich |
| `BiMonthly` | Zweimonatlich |
| `Quarterly` | Quartalsweise |
| `SemiAnnually` | Halbjährlich |
| `Annually` | Jährlich |

## `BudgetSourceType`
Datei: `FinanceManager.Shared/Dtos/Budget/BudgetSourceType.cs`

| Wert | Bedeutung |
|------|-----------|
| `Contact` | Istwerte aus Buchungen eines Kontakts |
| `ContactGroup` | Istwerte aus Buchungen einer Kontaktgruppe |
| `SavingsPlan` | Istwerte aus Sparplan-Buchungen |

## `BudgetIntervalType`
Datei: `FinanceManager.Shared/Dtos/Budget/BudgetIntervalType.cs`

| Wert | Bedeutung |
|------|-----------|
| `Monthly` | Monatliche Budgetregel |
| `Quarterly` | Quartalsregel |
| `Yearly` | Jahresregel |
| `CustomMonths` | Individuelles Monatsintervall |
