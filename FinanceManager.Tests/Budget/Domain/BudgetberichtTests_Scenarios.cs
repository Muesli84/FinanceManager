using FinanceManager.Domain.Budget.ReportCalculation;
using FinanceManager.Shared.Dtos.Budget;
using FluentAssertions;
using Xunit;
using static FinanceManager.Tests.Budget.Domain.BudgetberichtTestFixtures;

namespace FinanceManager.Tests.Budget.Domain;

/// <summary>
/// End-to-end scenario tests running the full <c>Budgetbericht</c> lifecycle (Planning through Output)
/// for the complex real-world cases called out in the feature's bestandsaufnahme: several purposes per
/// category, mixed income/expense rules, overruns and cost-neutral transfers.
/// </summary>
public sealed class BudgetberichtTests_Scenarios
{
    /// <summary>
    /// End-to-end run of a category with two contact-group purposes, confirming that per-purpose actuals
    /// are kept separate while the category row correctly sums both purposes' budgeted and actual amounts -
    /// the full pipeline from planning through output for the "multiple purposes per category" shape.
    /// </summary>
    [Fact]
    public void Scenario_ShoppingAndFood_CategorizedWithMultiplePurposes()
    {
        var category = CreateCategory("Shopping & Food");
        var foodContactGroup = Guid.NewGuid();
        var bakeryContactGroup = Guid.NewGuid();
        var foodPurpose = CreatePurpose("Food", BudgetSourceType.ContactGroup, foodContactGroup, category.Id, BudgetValuationType.TotalBudget);
        var bakeryPurpose = CreatePurpose("Bakeries", BudgetSourceType.ContactGroup, bakeryContactGroup, category.Id, BudgetValuationType.TotalBudget);
        var rules = new[]
        {
            CreatePurposeRule(foodPurpose.Id, -300m, BudgetIntervalType.Monthly, new DateOnly(2026, 1, 1)),
            CreatePurposeRule(bakeryPurpose.Id, -40m, BudgetIntervalType.Monthly, new DateOnly(2026, 1, 1))
        };

        var budgetbericht = new Budgetbericht(new DateOnly(2026, 1, 1), 1, BudgetReportInterval.Month, BudgetReportDateBasis.BookingDate);
        budgetbericht.SetPlanung(new[] { category }, new[] { foodPurpose, bakeryPurpose }, rules);

        budgetbericht.AddPosting(CreateContactPosting(-45.30m, new DateTime(2026, 1, 4), Guid.NewGuid(), foodContactGroup), BudgetReportDateBasis.BookingDate);
        budgetbericht.AddPosting(CreateContactPosting(-6.20m, new DateTime(2026, 1, 6), Guid.NewGuid(), bakeryContactGroup), BudgetReportDateBasis.BookingDate);
        budgetbericht.Finish();

        var group = budgetbericht.MonthlyResults.Single().ExpectationGroups.Single();
        group.Purposes.Single(p => p.Name == "Food").SumActualAmount.Should().Be(-45.30m);
        group.Purposes.Single(p => p.Name == "Bakeries").SumActualAmount.Should().Be(-6.20m);

        var categoryRow = budgetbericht.GetCurrentResult().Single(e => e.RowKind == BudgetReportEntryRowKind.Category);
        categoryRow.BudgetedAmount.Should().Be(-340m);
        categoryRow.ActualAmount.Should().Be(-51.50m);
    }

    /// <summary>
    /// End-to-end run for a category that only has a category-level budget rule while its contact-group
    /// purposes have no own rules: actual postings must still be attributed to the matching purpose rows
    /// (not only to the category subtotal), so the detail table remains diagnostically useful.
    /// </summary>
    [Fact]
    public void Scenario_CategoryRuleOnly_WithGroupedPurposes_StillShowsPurposeActuals()
    {
        var category = CreateCategory("Shopping & Food");
        var marketContactGroup = Guid.NewGuid();
        var bakeryContactGroup = Guid.NewGuid();
        var marketPurpose = CreatePurpose("Markets", BudgetSourceType.ContactGroup, marketContactGroup, category.Id, BudgetValuationType.TotalBudget);
        var bakeryPurpose = CreatePurpose("Bakeries", BudgetSourceType.ContactGroup, bakeryContactGroup, category.Id, BudgetValuationType.TotalBudget);
        var rules = new[]
        {
            CreateCategoryRule(category.Id, -300m, BudgetIntervalType.Monthly, new DateOnly(2026, 1, 1))
        };

        var budgetbericht = new Budgetbericht(new DateOnly(2026, 1, 1), 1, BudgetReportInterval.Month, BudgetReportDateBasis.BookingDate);
        budgetbericht.SetPlanung(new[] { category }, new[] { marketPurpose, bakeryPurpose }, rules);

        budgetbericht.AddPosting(CreateContactPosting(-45.30m, new DateTime(2026, 1, 4), Guid.NewGuid(), marketContactGroup), BudgetReportDateBasis.BookingDate);
        budgetbericht.AddPosting(CreateContactPosting(-6.20m, new DateTime(2026, 1, 6), Guid.NewGuid(), bakeryContactGroup), BudgetReportDateBasis.BookingDate);
        budgetbericht.Finish();

        var group = budgetbericht.MonthlyResults.Single().ExpectationGroups.Single();
        group.Purposes.Single(p => p.Name == "Markets").SumExpectedAmount.Should().Be(0m);
        group.Purposes.Single(p => p.Name == "Markets").SumActualAmount.Should().Be(-45.30m);
        group.Purposes.Single(p => p.Name == "Bakeries").SumExpectedAmount.Should().Be(0m);
        group.Purposes.Single(p => p.Name == "Bakeries").SumActualAmount.Should().Be(-6.20m);
        group.DirectExpectations.Single().SumExpectedAmount.Should().Be(-300m);
        group.DirectExpectations.Single().SumActualAmount.Should().Be(0m);

        var subtotalRow = budgetbericht.GetCurrentResult().Single(e => e.RowKind == BudgetReportEntryRowKind.Subtotal);
        subtotalRow.BudgetedAmount.Should().Be(-300m);
        subtotalRow.ActualAmount.Should().Be(-51.50m);
    }

    /// <summary>
    /// End-to-end run over a full 12-month report combining a recurring monthly expense with a one-off
    /// yearly income rule, verifying the Total row's budgeted amount correctly sums twelve monthly
    /// occurrences plus a single yearly occurrence, and the actual amount reflects only the two postings
    /// that actually occurred.
    /// </summary>
    [Fact]
    public void Scenario_MixedIncomeAndExpense_MonthlyExpenseAndYearlyIncome()
    {
        var expenseContact = Guid.NewGuid();
        var incomeContact = Guid.NewGuid();
        var expensePurpose = CreatePurpose("Subscription", BudgetSourceType.Contact, expenseContact);
        var incomePurpose = CreatePurpose("Bonus", BudgetSourceType.Contact, incomeContact);
        var rules = new[]
        {
            CreatePurposeRule(expensePurpose.Id, -12m, BudgetIntervalType.Monthly, new DateOnly(2026, 1, 1)),
            CreatePurposeRule(incomePurpose.Id, 1000m, BudgetIntervalType.Yearly, new DateOnly(2026, 3, 1))
        };

        var budgetbericht = new Budgetbericht(new DateOnly(2026, 1, 1), 12, BudgetReportInterval.Month, BudgetReportDateBasis.BookingDate);
        budgetbericht.SetPlanung(Array.Empty<BudgetCategoryDto>(), new[] { expensePurpose, incomePurpose }, rules);

        budgetbericht.AddPosting(CreateContactPosting(-12m, new DateTime(2026, 1, 5), expenseContact), BudgetReportDateBasis.BookingDate);
        budgetbericht.AddPosting(CreateContactPosting(1000m, new DateTime(2026, 3, 15), incomeContact), BudgetReportDateBasis.BookingDate);
        budgetbericht.Finish();

        var entries = budgetbericht.GetCurrentResult();
        entries.Single(e => e.RowKind == BudgetReportEntryRowKind.Total).BudgetedAmount.Should().Be((-12m * 12) + 1000m);
        entries.Single(e => e.RowKind == BudgetReportEntryRowKind.Total).ActualAmount.Should().Be(-12m + 1000m);
    }

    /// <summary>
    /// End-to-end run of the "Streaming Provider" overrun case: three postings exceed the -10 total
    /// budget by -5.98, verifying through the public <c>GetCurrentResult()</c> output that the purpose
    /// row caps its actual amount at the budget, the excess is attributed to the purpose's occurrence
    /// (not lost), and the report-wide Unbudgeted row stays at zero.
    /// </summary>
    [Fact]
    public void Scenario_Overrun_StreamingProvider()
    {
        var contactId = Guid.NewGuid();
        var purpose = CreatePurpose("Streaming Provider", BudgetSourceType.Contact, contactId, valuationType: BudgetValuationType.TotalBudget);
        var rule = CreatePurposeRule(purpose.Id, -10m, BudgetIntervalType.Monthly, new DateOnly(2026, 1, 1));

        var budgetbericht = new Budgetbericht(new DateOnly(2026, 1, 1), 1, BudgetReportInterval.Month, BudgetReportDateBasis.BookingDate);
        budgetbericht.SetPlanung(Array.Empty<BudgetCategoryDto>(), new[] { purpose }, new[] { rule });

        budgetbericht.AddPosting(CreateContactPosting(-4.99m, new DateTime(2026, 1, 3), contactId), BudgetReportDateBasis.BookingDate);
        budgetbericht.AddPosting(CreateContactPosting(-4.99m, new DateTime(2026, 1, 10), contactId), BudgetReportDateBasis.BookingDate);
        budgetbericht.AddPosting(CreateContactPosting(-6.00m, new DateTime(2026, 1, 17), contactId), BudgetReportDateBasis.BookingDate);
        budgetbericht.Finish();

        var purposeRow = budgetbericht.GetCurrentResult().Single(e => e.RowKind == BudgetReportEntryRowKind.Purpose);
        purposeRow.ActualAmount.Should().Be(-10m);

        // The -5.98 overrun stays attributed to this purpose (visible via UnvaluedMatchedPostings), not
        // the generic Unbudgeted row, which is reserved for postings matching no purpose at all.
        var expectation = budgetbericht.MonthlyResults.Single().ExpectationGroups.Single().Purposes.Single();
        expectation.Postings.SelectMany(p => p.UnvaluedMatchedPostings).Sum(p => p.Amount).Should().Be(-5.98m);

        var unbudgetedRow = budgetbericht.GetCurrentResult().Single(e => e.RowKind == BudgetReportEntryRowKind.Unbudgeted);
        unbudgetedRow.ActualAmount.Should().Be(0m);
    }

    /// <summary>
    /// End-to-end mirror of <see cref="Scenario_Overrun_StreamingProvider"/> for an income overrun: a
    /// salary posting of 3450 against a 3000 expectation caps the actual amount and deviation at zero
    /// while the surplus 450 stays attributed to the purpose rather than appearing as Unbudgeted -
    /// confirming overrun capping applies symmetrically to income, not only expenses.
    /// </summary>
    [Fact]
    public void Scenario_Salary_Income_Overrun()
    {
        var contactId = Guid.NewGuid();
        var purpose = CreatePurpose("Salary", BudgetSourceType.Contact, contactId);
        var rule = CreatePurposeRule(purpose.Id, 3000m, BudgetIntervalType.Monthly, new DateOnly(2026, 1, 1));

        var budgetbericht = new Budgetbericht(new DateOnly(2026, 1, 1), 1, BudgetReportInterval.Month, BudgetReportDateBasis.BookingDate);
        budgetbericht.SetPlanung(Array.Empty<BudgetCategoryDto>(), new[] { purpose }, new[] { rule });

        budgetbericht.AddPosting(CreateContactPosting(3450m, new DateTime(2026, 1, 25), contactId), BudgetReportDateBasis.BookingDate);
        budgetbericht.Finish();

        var purposeRow = budgetbericht.GetCurrentResult().Single(e => e.RowKind == BudgetReportEntryRowKind.Purpose);
        purposeRow.ActualAmount.Should().Be(3000m);
        purposeRow.Deviation.Should().Be(0m);

        // The 450 overrun stays attributed to this purpose, not the generic Unbudgeted row.
        var expectation = budgetbericht.MonthlyResults.Single().ExpectationGroups.Single().Purposes.Single();
        expectation.Postings.SelectMany(p => p.UnvaluedMatchedPostings).Sum(p => p.Amount).Should().Be(450m);

        budgetbericht.GetCurrentResult().Single(e => e.RowKind == BudgetReportEntryRowKind.Unbudgeted).ActualAmount.Should().Be(0m);
    }

    /// <summary>
    /// End-to-end verification of the Total row's "Endsumme" definition from the feature's issue
    /// specification: a cost-neutral self-transfer and a genuinely unbudgeted posting are kept in
    /// separate rows (CostNeutral vs. Unbudgeted) in <c>GetCurrentResult()</c>, yet the report-wide Total
    /// row's actual amount explicitly includes both - the CostNeutral row is excluded from the narrower
    /// Unbudgeted row but not from the overall total.
    /// </summary>
    [Fact]
    public void Scenario_CostNeutralTransfer_WithGroupIdAndSelfContact_DoesNotCountAsUnbudgeted()
    {
        var budgetbericht = new Budgetbericht(new DateOnly(2026, 1, 1), 1, BudgetReportInterval.Month, BudgetReportDateBasis.BookingDate);
        budgetbericht.SetPlanung(Array.Empty<BudgetCategoryDto>(), Array.Empty<BudgetPurposeDto>(), Array.Empty<BudgetRuleDto>());

        var mirrorGroupId = Guid.NewGuid();
        budgetbericht.AddPosting(CreateUnattributedPosting(-5m, new DateTime(2026, 1, 10), groupId: mirrorGroupId, isSelfContact: true), BudgetReportDateBasis.BookingDate);
        budgetbericht.AddPosting(CreateUnattributedPosting(-49.90m, new DateTime(2026, 1, 15)), BudgetReportDateBasis.BookingDate);
        budgetbericht.Finish();

        var monthResult = budgetbericht.MonthlyResults.Single();
        monthResult.CostNeutralPostings.Should().ContainSingle(p => p.Amount == -5m);
        monthResult.UnbudgetedPostings.Should().ContainSingle(p => p.Amount == -49.90m);

        var entries = budgetbericht.GetCurrentResult();
        entries.Single(e => e.RowKind == BudgetReportEntryRowKind.Unbudgeted).ActualAmount.Should().Be(-49.90m);
        entries.Single(e => e.RowKind == BudgetReportEntryRowKind.CostNeutral).ActualAmount.Should().Be(-5m);
        // issue.md describes the additional rows in this exact order: Subtotal, Unbudgeted, Subtotal
        // (including Unbudgeted), CostNeutral, "Endsumme" (Total) - explicitly "inklusive der Zeile
        // Kostenneutral". So the Total row's ActualAmount is the unbudgeted amount plus the cost-neutral
        // amount, even though cost-neutral postings are excluded from the (narrower) Unbudgeted row itself.
        entries.Single(e => e.RowKind == BudgetReportEntryRowKind.Total).ActualAmount.Should().Be(-54.90m,
            "the Total row is the 'Endsumme' which issue.md explicitly defines as including the CostNeutral row");
    }

    /// <summary>
    /// End-to-end run with no purposes or rules planned at all, confirming a real-world posting still
    /// flows through the full pipeline into the Unbudgeted bucket rather than being rejected or ignored
    /// simply because the report has no budget configuration to match against.
    /// </summary>
    [Fact]
    public void Scenario_UnbudgetedPostings_ForPurposesWithoutAnyMatchingRule()
    {
        var budgetbericht = new Budgetbericht(new DateOnly(2026, 1, 1), 1, BudgetReportInterval.Month, BudgetReportDateBasis.BookingDate);
        budgetbericht.SetPlanung(Array.Empty<BudgetCategoryDto>(), Array.Empty<BudgetPurposeDto>(), Array.Empty<BudgetRuleDto>());

        budgetbericht.AddPosting(CreateUnattributedPosting(-120m, new DateTime(2026, 1, 20), purpose: "Random one-off expense"), BudgetReportDateBasis.BookingDate);
        budgetbericht.Finish();

        budgetbericht.MonthlyResults.Single().UnbudgetedPostings.Should().ContainSingle(p => p.Amount == -120m);
    }
}
