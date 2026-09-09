using System.Globalization;
using System.Text;
using FinanceManager.Application.Accounts;
using FinanceManager.Application.Budget;
using FinanceManager.Application.Contacts;
using FinanceManager.Application.Demo;
using FinanceManager.Application.Reports;
using FinanceManager.Application.Savings;
using FinanceManager.Application.Securities;
using FinanceManager.Application.Statements;
using FinanceManager.Shared.Dtos.Accounts;
using FinanceManager.Shared.Dtos.Budget;
using FinanceManager.Shared.Dtos.Contacts;
using FinanceManager.Shared.Dtos.HomeKpi;
using FinanceManager.Shared.Dtos.Reports;
using FinanceManager.Shared.Dtos.SavingsPlans;
using FinanceManager.Shared.Dtos.Securities;
using FinanceManager.Shared.Dtos.Statements;
using Microsoft.Extensions.Logging;

namespace FinanceManager.Infrastructure.Demo;

/// <summary>
/// Creates deterministic demo data using the same business services as the interactive user flows.
/// </summary>
public sealed class DemoDataService : IDemoDataService
{
    private readonly IAccountService _accountService;
    private readonly IContactService _contactService;
    private readonly IContactCategoryService _contactCategoryService;
    private readonly ISavingsPlanCategoryService _savingsPlanCategoryService;
    private readonly ISavingsPlanService _savingsPlanService;
    private readonly ISecurityService _securityService;
    private readonly ISecurityCategoryService _securityCategoryService;
    private readonly ISecurityPriceImportServiceFactory _securityPriceImportServiceFactory;
    private readonly IStatementDraftService _statementDraftService;
    private readonly IBudgetCategoryService _budgetCategoryService;
    private readonly IBudgetPurposeService _budgetPurposeService;
    private readonly IBudgetRuleService _budgetRuleService;
    private readonly IHomeKpiService _homeKpiService;
    private readonly IReportFavoriteService _reportFavoriteService;
    private readonly ILogger<DemoDataService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DemoDataService"/> class.
    /// </summary>
    /// <param name="accountService">Service used to create demo accounts.</param>
    /// <param name="contactService">Service used to create demo contacts.</param>
    /// <param name="contactCategoryService">Service used to create contact groups.</param>
    /// <param name="savingsPlanCategoryService">Service used to create savings plan categories.</param>
    /// <param name="savingsPlanService">Service used to create savings plans.</param>
    /// <param name="securityService">Service used to create securities.</param>
    /// <param name="securityCategoryService">Service used to create security categories.</param>
    /// <param name="securityPriceImportServiceFactory">Factory used to resolve the security price import service.</param>
    /// <param name="statementDraftService">Service used to create and book statement drafts.</param>
    /// <param name="budgetCategoryService">Service used to create budget categories.</param>
    /// <param name="budgetPurposeService">Service used to create budget purposes.</param>
    /// <param name="budgetRuleService">Service used to create budget rules.</param>
    /// <param name="homeKpiService">Service used to create default home KPI tiles.</param>
    /// <param name="reportFavoriteService">Service used to create default report favorites.</param>
    /// <param name="logger">Logger instance.</param>
    public DemoDataService(
        IAccountService accountService,
        IContactService contactService,
        IContactCategoryService contactCategoryService,
        ISavingsPlanCategoryService savingsPlanCategoryService,
        ISavingsPlanService savingsPlanService,
        ISecurityService securityService,
        ISecurityCategoryService securityCategoryService,
        ISecurityPriceImportServiceFactory securityPriceImportServiceFactory,
        IStatementDraftService statementDraftService,
        IBudgetCategoryService budgetCategoryService,
        IBudgetPurposeService budgetPurposeService,
        IBudgetRuleService budgetRuleService,
        IHomeKpiService homeKpiService,
        IReportFavoriteService reportFavoriteService,
        ILogger<DemoDataService> logger)
    {
        _accountService = accountService ?? throw new ArgumentNullException(nameof(accountService));
        _contactService = contactService ?? throw new ArgumentNullException(nameof(contactService));
        _contactCategoryService = contactCategoryService ?? throw new ArgumentNullException(nameof(contactCategoryService));
        _savingsPlanCategoryService = savingsPlanCategoryService ?? throw new ArgumentNullException(nameof(savingsPlanCategoryService));
        _savingsPlanService = savingsPlanService ?? throw new ArgumentNullException(nameof(savingsPlanService));
        _securityService = securityService ?? throw new ArgumentNullException(nameof(securityService));
        _securityCategoryService = securityCategoryService ?? throw new ArgumentNullException(nameof(securityCategoryService));
        _securityPriceImportServiceFactory = securityPriceImportServiceFactory ?? throw new ArgumentNullException(nameof(securityPriceImportServiceFactory));
        _statementDraftService = statementDraftService ?? throw new ArgumentNullException(nameof(statementDraftService));
        _budgetCategoryService = budgetCategoryService ?? throw new ArgumentNullException(nameof(budgetCategoryService));
        _budgetPurposeService = budgetPurposeService ?? throw new ArgumentNullException(nameof(budgetPurposeService));
        _budgetRuleService = budgetRuleService ?? throw new ArgumentNullException(nameof(budgetRuleService));
        _homeKpiService = homeKpiService ?? throw new ArgumentNullException(nameof(homeKpiService));
        _reportFavoriteService = reportFavoriteService ?? throw new ArgumentNullException(nameof(reportFavoriteService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates the full demo data set for the provided user.
    /// </summary>
    /// <param name="userId">Identifier of the user who receives demo data.</param>
    /// <param name="createPostings">When true, statement drafts and postings for the past 24 months are generated.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task CreateDemoDataAsync(Guid userId, bool createPostings, CancellationToken ct)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("userId required", nameof(userId));
        }

        var referenceMonthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var random = new Random(BuildDeterministicSeed());

        var (
            selfContact,
            giroBankContact,
            mamaContact,
            telkommiContact,
            employerContact,
            insuranceContact,
            rentContact,
            marketContacts,
            onlineShopContacts,
            bakeryContacts,
            giroAccount,
            primarySavingsAccount,
            secondarySavingsAccount,
            sdacPlan,
            householdPlan,
            autoPlan,
            generalPlan,
            vacationPlan,
            securityBuySavingPlan,
            worldSecurity,
            postSecurity,
            worldPriceHistory,
            postPriceHistory,
            householdContractNumber) = await CreateDemoDataSetAsync(userId, referenceMonthStart, random, ct);

        if (createPostings)
        {
            await CreateMonthlyPostingPlanAsync(
                userId,
                referenceMonthStart,
                random,
                selfContact,
                giroBankContact,
                mamaContact,
                telkommiContact,
                employerContact,
                insuranceContact,
                rentContact,
                marketContacts,
                onlineShopContacts,
                bakeryContacts,
                giroAccount,
                primarySavingsAccount,
                secondarySavingsAccount,
                sdacPlan,
                householdPlan,
                autoPlan,
                generalPlan,
                vacationPlan,
                securityBuySavingPlan,
                worldSecurity,
                postSecurity,
                worldPriceHistory,
                postPriceHistory,
                householdContractNumber,
                ct);
        }

        await EnsureDefaultHomeKpisAsync(userId, ct);
        await EnsureDefaultReportFavoritesAsync(userId, ct);
    }

    private async Task<(
        ContactDto selfContact,
        ContactDto giroBankContact,
        ContactDto mamaContact,
        ContactDto telkommiContact,
        ContactDto employerContact,
        ContactDto insuranceContact,
        ContactDto rentContact,
        IReadOnlyList<ContactDto> marketContacts,
        IReadOnlyList<ContactDto> onlineShops,
        IReadOnlyList<ContactDto> bakeryContacts,
        AccountDto giroAccount,
        AccountDto primarySavingsAccount,
        AccountDto secondarySavingsAccount,
        SavingsPlanDto sdacPlan,
        SavingsPlanDto householdPlan,
        SavingsPlanDto autoPlan,
        SavingsPlanDto generalPlan,
        SavingsPlanDto vacationPlan,
        SavingsPlanDto securityBuySavingPlan,
        SecurityDto worldSecurity,
        SecurityDto postSecurity,
        Dictionary<DateTime, decimal> worldPriceHistory,
        Dictionary<DateTime, decimal> postPriceHistory,
        string householdContractNumber)> CreateDemoDataSetAsync(Guid userId, DateTime referenceMonthStart, Random random, CancellationToken ct)
    {
        var selfContacts = await _contactService.ListAsync(userId, 0, 1, ContactType.Self, null, ct);
        var selfContact = selfContacts.FirstOrDefault()
                          ?? await _contactService.CreateAsync(userId, "Self", ContactType.Self, null, null, false, ct);

        var firstMonth = referenceMonthStart.AddMonths(-23);
        var nextJanuary = new DateTime(referenceMonthStart.Year, 1, 1);
        if (nextJanuary <= referenceMonthStart)
        {
            nextJanuary = nextJanuary.AddYears(1);
        }

        var nextDecember = new DateTime(referenceMonthStart.Year, 12, 1);
        if (nextDecember <= referenceMonthStart)
        {
            nextDecember = nextDecember.AddYears(1);
        }

        var banksGroup = await _contactCategoryService.CreateAsync(userId, "Banken", ct);
        var workGroup = await _contactCategoryService.CreateAsync(userId, "Arbeit", ct);
        var insuranceGroup = await _contactCategoryService.CreateAsync(userId, "Versicherungen", ct);
        var serviceGroup = await _contactCategoryService.CreateAsync(userId, "Dienstleister", ct);
        var marketGroup = await _contactCategoryService.CreateAsync(userId, "Supermärkte & Einzelhandel", ct);
        var onlineShopGroup = await _contactCategoryService.CreateAsync(userId, "Onlineshops", ct);
        var bakeryGroup = await _contactCategoryService.CreateAsync(userId, "Bäckereien & Cafés", ct);

        var giroBankContact = await _contactService.CreateAsync(userId, "Musterbank Nord", ContactType.Bank, banksGroup.Id, null, false, ct);
        var secondBankContact = await _contactService.CreateAsync(userId, "Musterbank Süd", ContactType.Bank, banksGroup.Id, null, false, ct);
        var mamaContact = await _contactService.CreateAsync(userId, "Mama", ContactType.Person, null, null, false, ct);
        var telkommiContact = await _contactService.CreateAsync(userId, "Telkommi", ContactType.Organization, serviceGroup.Id, null, false, ct);
        var employerContact = await _contactService.CreateAsync(userId, "Arbeitgeber GmbH", ContactType.Organization, workGroup.Id, null, false, ct);
        var insuranceContact = await _contactService.CreateAsync(userId, "Zentrial Versicherung", ContactType.Organization, insuranceGroup.Id, null, false, ct);
        var sdacContact = await _contactService.CreateAsync(userId, "SDAC", ContactType.Organization, insuranceGroup.Id, null, false, ct);
        var rentContact = await _contactService.CreateAsync(userId, "Sabbel Lüchtenhausen", ContactType.Person, serviceGroup.Id, null, false, ct);
        var onlineShops = new List<ContactDto>
        {
            await _contactService.CreateAsync(userId, "Pear Store", ContactType.Organization, onlineShopGroup.Id, null, false, ct),
            await _contactService.CreateAsync(userId, "Borneon", ContactType.Organization, onlineShopGroup.Id, null, false, ct),
            await _contactService.CreateAsync(userId, "Anna", ContactType.Organization, onlineShopGroup.Id, null, false, ct)
        };
        var marketContacts = new List<ContactDto>
        {
            await _contactService.CreateAsync(userId, "Adli", ContactType.Organization, marketGroup.Id, null, false, ct),
            await _contactService.CreateAsync(userId, "Didl", ContactType.Organization, marketGroup.Id, null, false, ct),
            await _contactService.CreateAsync(userId, "Adeka", ContactType.Organization, marketGroup.Id, null, false, ct)
        };
        var bakeryContacts = new List<ContactDto>
        {
            await _contactService.CreateAsync(userId, "Bäckerei Kramphove", ContactType.Organization, bakeryGroup.Id, null, false, ct),
            await _contactService.CreateAsync(userId, "Bäckerei Feiping", ContactType.Organization, bakeryGroup.Id, null, false, ct),
            await _contactService.CreateAsync(userId, "Bäckerei Schlonz", ContactType.Organization, bakeryGroup.Id, null, false, ct)
        };

        var recurringExpensesCategory = await _savingsPlanCategoryService.CreateAsync(userId, "Wiederkehrende Ausgaben", ct);
        var investmentCategory = await _savingsPlanCategoryService.CreateAsync(userId, "Anlage", ct);

        var sdacContractNumber = $"{random.Next(100000, 999999)}-{random.Next(1000, 9999)}";
        var householdContractNumber = $"{random.Next(100000, 999999)}-{random.Next(1000, 9999)}";

        var sdacPlan = await _savingsPlanService.CreateAsync(
            userId,
            "SDAC Gebühr",
            SavingsPlanType.Recurring,
            99.00m,
            nextJanuary,
            SavingsPlanInterval.Annually,
            recurringExpensesCategory.Id,
            sdacContractNumber,
            ct);

        var householdPlan = await _savingsPlanService.CreateAsync(
            userId,
            "Hausratversicherung",
            SavingsPlanType.Recurring,
            62.60m,
            nextDecember,
            SavingsPlanInterval.Annually,
            recurringExpensesCategory.Id,
            householdContractNumber,
            ct);

        var autoPlan = await _savingsPlanService.CreateAsync(
            userId,
            "Auto",
            SavingsPlanType.OneTime,
            14000.00m,
            new DateTime(referenceMonthStart.Year + 10, 7, 6),
            null,
            investmentCategory.Id,
            null,
            ct);

        var securityBuySavingPlan = await _savingsPlanService.CreateAsync(
            userId,
            "Rückstellung Wertpapierneukauf",
            SavingsPlanType.Open,
            null,
            null,
            null,
            null,
            null,
            ct);

        var vacationPlan = await _savingsPlanService.CreateAsync(
            userId,
            "Urlaub",
            SavingsPlanType.Open,
            null,
            null,
            null,
            null,
            null,
            ct);

        var generalPlan = await _savingsPlanService.CreateAsync(
            userId,
            "Sparplan Allgemein",
            SavingsPlanType.Open,
            null,
            null,
            null,
            null,
            null,
            ct);

        var budgetCategoryWork = await _budgetCategoryService.CreateAsync(userId, "Arbeit", ct);
        var budgetCategoryInsurance = await _budgetCategoryService.CreateAsync(userId, "Versicherungen", ct);
        var budgetCategoryHousing = await _budgetCategoryService.CreateAsync(userId, "Wohnen", ct);
        var budgetCategoryShopping = await _budgetCategoryService.CreateAsync(userId, "Einkaufen & Verpflegung", ct);

        var budgetStart = DateOnly.FromDateTime(firstMonth);
        var decemberStart = new DateOnly(firstMonth.Year, 12, 1);
        var januaryStart = new DateOnly(firstMonth.Year, 1, 1);

        var salaryPurpose = await _budgetPurposeService.CreateAsync(userId, "Gehalt", BudgetSourceType.Contact, employerContact.Id, null, budgetCategoryWork.Id, ct);
        await _budgetRuleService.CreateAsync(userId, salaryPurpose.Id, 3642.50m, BudgetIntervalType.Monthly, null, budgetStart, null, null, false, ct);

        var householdReservePurpose = await _budgetPurposeService.CreateAsync(userId, "Rückstellung Hausratversicherung", BudgetSourceType.Contact, selfContact.Id, null, budgetCategoryInsurance.Id, ct);
        await _budgetRuleService.CreateAsync(userId, householdReservePurpose.Id, -5.22m, BudgetIntervalType.Monthly, null, budgetStart, null, null, false, ct);
        await _budgetRuleService.CreateAsync(userId, householdReservePurpose.Id, 62.64m, BudgetIntervalType.Yearly, null, decemberStart, null, null, false, ct);

        var householdPurpose = await _budgetPurposeService.CreateAsync(userId, "Hausratversicherung", BudgetSourceType.Contact, insuranceContact.Id, null, budgetCategoryInsurance.Id, ct);
        await _budgetRuleService.CreateAsync(userId, householdPurpose.Id, -62.60m, BudgetIntervalType.Yearly, null, decemberStart, null, null, false, ct);

        var sdacReservePurpose = await _budgetPurposeService.CreateAsync(userId, "Rückstellung SDAC", BudgetSourceType.Contact, selfContact.Id, null, budgetCategoryInsurance.Id, ct);
        await _budgetRuleService.CreateAsync(userId, sdacReservePurpose.Id, -8.25m, BudgetIntervalType.Monthly, null, budgetStart, null, null, false, ct);
        await _budgetRuleService.CreateAsync(userId, sdacReservePurpose.Id, 99.00m, BudgetIntervalType.Yearly, null, januaryStart, null, null, false, ct);

        var sdacPurpose = await _budgetPurposeService.CreateAsync(userId, "SDAC", BudgetSourceType.Contact, sdacContact.Id, null, budgetCategoryInsurance.Id, ct);
        await _budgetRuleService.CreateAsync(userId, sdacPurpose.Id, -99.00m, BudgetIntervalType.Yearly, null, januaryStart, null, null, false, ct);

        var rentPurpose = await _budgetPurposeService.CreateAsync(userId, "Wohnungsmiete", BudgetSourceType.Contact, rentContact.Id, null, budgetCategoryHousing.Id, ct);
        await _budgetRuleService.CreateAsync(userId, rentPurpose.Id, -845.00m, BudgetIntervalType.Monthly, null, budgetStart, null, null, false, ct);

        var telcomPurpose = await _budgetPurposeService.CreateAsync(userId, "Strom", BudgetSourceType.Contact, telkommiContact.Id, null, budgetCategoryHousing.Id, ct);
        await _budgetRuleService.CreateAsync(userId, telcomPurpose.Id, -49.90m, BudgetIntervalType.Monthly, null, budgetStart, null, null, false, ct);

        await _budgetPurposeService.CreateAsync(
            userId,
            "Supermärkte & Einzelhandel",
            BudgetSourceType.ContactGroup,
            marketGroup.Id,
            null,
            budgetCategoryShopping.Id,
            ct,
            BudgetValuationType.TotalBudget);

        await _budgetPurposeService.CreateAsync(
            userId,
            "Bäckereien & Cafés",
            BudgetSourceType.ContactGroup,
            bakeryGroup.Id,
            null,
            budgetCategoryShopping.Id,
            ct,
            BudgetValuationType.TotalBudget);

        await _budgetRuleService.CreateForCategoryAsync(userId, budgetCategoryShopping.Id, -300.00m, BudgetIntervalType.Monthly, null, budgetStart, null, ct);

        var etfCategory = await _securityCategoryService.CreateAsync(userId, "ETF", ct);
        var stockCategory = await _securityCategoryService.CreateAsync(userId, "Aktien", ct);

        var worldSecurity = await _securityService.CreateAsync(
            userId,
            "USHSIV-MSCI WLD",
            "LU00ABACAD96",
            "UShares MSCI World ETF",
            string.Empty,
            "EUR",
            etfCategory.Id,
            ct,
            "Global",
            "MSCI World");

        var postSecurity = await _securityService.CreateAsync(
            userId,
            "Inländische Post AG",
            "DE0001112026",
            null,
            null,
            "EUR",
            stockCategory.Id,
            ct,
            "DE",
            "Logistik");

        var worldPriceHistory = await CreateSecurityPriceHistoryAsync(userId, worldSecurity, referenceMonthStart, 11.36m, random, ct);
        var postPriceHistory = await CreateSecurityPriceHistoryAsync(userId, postSecurity, referenceMonthStart, 44.25m, random, ct);

        var giroAccount = await _accountService.CreateAsync(
            userId,
            "Girokonto",
            AccountType.Giro,
            "DE12500105170648489890",
            giroBankContact.Id,
            SavingsPlanExpectation.Optional,
            true,
            false,
            ct);

        var primarySavingsAccount = await _accountService.CreateAsync(
            userId,
            "Sparkonto Rücklagen",
            AccountType.Savings,
            "DE44500105175407324931",
            giroBankContact.Id,
            SavingsPlanExpectation.None,
            true,
            false,
            ct);

        var secondarySavingsAccount = await _accountService.CreateAsync(
            userId,
            "Sparkonto Allgemein",
            AccountType.Savings,
            "DE21500105176123456789",
            secondBankContact.Id,
            SavingsPlanExpectation.None,
            true,
            false,
            ct);

        return (
            selfContact,
            giroBankContact,
            mamaContact,
            telkommiContact,
            employerContact,
            insuranceContact,
            rentContact,
            marketContacts,
            onlineShops,
            bakeryContacts,
            giroAccount,
            primarySavingsAccount,
            secondarySavingsAccount,
            sdacPlan,
            householdPlan,
            autoPlan,
            generalPlan,
            vacationPlan,
            securityBuySavingPlan,
            worldSecurity,
            postSecurity,
            worldPriceHistory,
            postPriceHistory,
            householdContractNumber);
    }

    private async Task CreateMonthlyPostingPlanAsync(
        Guid userId,
        DateTime referenceMonthStart,
        Random random,
        ContactDto selfContact,
        ContactDto giroBankContact,
        ContactDto mamaContact,
        ContactDto telkommiContact,
        ContactDto employerContact,
        ContactDto insuranceContact,
        ContactDto rentContact,
        IReadOnlyList<ContactDto> marketContacts,
        IReadOnlyList<ContactDto> onlineShopContacts,
        IReadOnlyList<ContactDto> bakeryContacts,
        AccountDto giroAccount,
        AccountDto primarySavingsAccount,
        AccountDto secondarySavingsAccount,
        SavingsPlanDto sdacPlan,
        SavingsPlanDto householdPlan,
        SavingsPlanDto autoPlan,
        SavingsPlanDto generalPlan,
        SavingsPlanDto vacationPlan,
        SavingsPlanDto securityBuySavingPlan,
        SecurityDto worldSecurity,
        SecurityDto postSecurity,
        Dictionary<DateTime, decimal> worldPriceHistory,
        Dictionary<DateTime, decimal> postPriceHistory,
        string householdContractNumber,
        CancellationToken ct)
    {
        var shopContacts = marketContacts.Concat(bakeryContacts).ToArray();
        var firstMonth = referenceMonthStart.AddMonths(-23);
        var now = DateTime.UtcNow.Date;

        async Task<Guid> CreateDraftAsync(AccountDto account, DateTime monthStart)
        {
            var draft = await _statementDraftService.CreateEmptyDraftAsync(userId, $"{account.Name}-{monthStart:yyyy-MM}.csv", ct);
            if (draft is null)
            {
                throw new InvalidOperationException("Statement draft could not be created.");
            }

            var withAccount = await _statementDraftService.SetAccountAsync(draft.DraftId, userId, account.Id, ct);
            if (withAccount is null)
            {
                throw new InvalidOperationException("Statement draft account could not be assigned.");
            }

            return draft.DraftId;
        }

        async Task AddDraftEntryAsync(
            Guid draftId,
            DateTime bookingDate,
            decimal amount,
            string subject,
            Guid contactId,
            Guid? savingsPlanId = null,
            Guid? securityId = null,
            SecurityTransactionType? securityTransactionType = null,
            decimal? securityQuantity = null,
            decimal? securityFee = null,
            decimal? securityTax = null)
        {
            var draft = await _statementDraftService.AddEntryAsync(draftId, userId, bookingDate, amount, subject, ct);
            if (draft is null)
            {
                throw new InvalidOperationException("Statement draft entry could not be created.");
            }

            var entry = draft.Entries.OrderByDescending(x => x.EntryNumber).First();
            var withContact = await _statementDraftService.SetEntryContactAsync(draftId, entry.Id, contactId, userId, ct);
            if (withContact is null)
            {
                throw new InvalidOperationException("Statement draft entry contact could not be assigned.");
            }

            if (savingsPlanId.HasValue)
            {
                await _statementDraftService.AssignSavingsPlanAsync(draftId, entry.Id, savingsPlanId, userId, ct);
            }

            if (securityId.HasValue)
            {
                var securityResult = await _statementDraftService.SetEntrySecurityAsync(
                    draftId,
                    entry.Id,
                    securityId,
                    securityTransactionType,
                    securityQuantity,
                    securityFee,
                    securityTax,
                    userId,
                    ct);
                if (securityResult is null)
                {
                    throw new InvalidOperationException("Statement draft entry security could not be assigned.");
                }
            }
        }

        async Task BookDraftAsync(Guid draftId)
        {
            var result = await _statementDraftService.BookAsync(draftId, null, userId, true, ct);
            if (!result.Success)
            {
                var errors = string.Join("; ", result.Validation.Messages.Select(x => x.Message));
                throw new InvalidOperationException($"Booking statement draft failed: {errors}");
            }
        }

        decimal GetPriceForDate(Dictionary<DateTime, decimal> priceHistory, DateTime date)
        {
            var cursor = date.Date;
            decimal price;
            while (!priceHistory.TryGetValue(cursor, out price))
            {
                cursor = cursor.AddDays(-1);
                if (cursor < priceHistory.Keys.Min())
                {
                    throw new InvalidOperationException("No price available for requested date.");
                }
            }

            return price;
        }

        DateTime ClampToBusinessDay(DateTime date, DateTime monthStart, DateTime monthEnd)
        {
            var cursor = date.Date;
            if (cursor < monthStart)
            {
                cursor = monthStart;
            }

            if (cursor > monthEnd)
            {
                cursor = monthEnd;
            }

            if (cursor.DayOfWeek == DayOfWeek.Saturday)
            {
                if (cursor.AddDays(2) <= monthEnd)
                {
                    cursor = cursor.AddDays(2);
                }
                else
                {
                    cursor = cursor.AddDays(-1);
                }
            }
            else if (cursor.DayOfWeek == DayOfWeek.Sunday)
            {
                if (cursor.AddDays(1) <= monthEnd)
                {
                    cursor = cursor.AddDays(1);
                }
                else
                {
                    cursor = cursor.AddDays(-2);
                }
            }

            return cursor;
        }

        var householdReservePot = 0m;
        var sdacReservePot = 0m;
        var totalMonthCount = 24;

        for (var monthIndex = 0; monthIndex < totalMonthCount; monthIndex++)
        {
            ct.ThrowIfCancellationRequested();

            var dividendAmount = 0m;

            var monthStart = firstMonth.AddMonths(monthIndex);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var isCurrentMonth = monthStart.Year == referenceMonthStart.Year && monthStart.Month == referenceMonthStart.Month;
            var firstBusinessDay = GetFirstBusinessDayOfMonth(monthStart);
            var lastBusinessDay = GetLastBusinessDayOfMonth(monthStart);

            var giroDraftId = await CreateDraftAsync(giroAccount, monthStart);
            var primarySavingsDraftId = await CreateDraftAsync(primarySavingsAccount, monthStart);
            var secondarySavingsDraftId = await CreateDraftAsync(secondarySavingsAccount, monthStart);

            if (!isCurrentMonth || now >= lastBusinessDay)
            {
                await AddDraftEntryAsync(giroDraftId, lastBusinessDay, 3642.50m, "Gehalt", employerContact.Id);
            }

            if (monthIndex == 0)
            {
                await AddDraftEntryAsync(giroDraftId, firstBusinessDay, 5000.00m, "Startgeld", mamaContact.Id);
            }

            await AddDraftEntryAsync(giroDraftId, firstBusinessDay, -5.22m, "Rückstellung Hausratversicherung", selfContact.Id, householdPlan.Id);
            await AddDraftEntryAsync(primarySavingsDraftId, firstBusinessDay, 5.22m, "Rückstellung Hausratversicherung", selfContact.Id);
            householdReservePot += 5.22m;

            await AddDraftEntryAsync(giroDraftId, firstBusinessDay, -50.00m, "Rückstellung Urlaub", selfContact.Id, vacationPlan.Id);
            await AddDraftEntryAsync(primarySavingsDraftId, firstBusinessDay, 50.00m, "Rückstellung Urlaub", selfContact.Id);

            await AddDraftEntryAsync(giroDraftId, firstBusinessDay, -100.00m, "Sparplan Allgemein", selfContact.Id, generalPlan.Id);
            await AddDraftEntryAsync(secondarySavingsDraftId, firstBusinessDay, 100.00m, "Sparplan Allgemein", selfContact.Id);

            await AddDraftEntryAsync(giroDraftId, firstBusinessDay, -70.00m, "Rückstellung Auto", selfContact.Id, autoPlan.Id);
            await AddDraftEntryAsync(primarySavingsDraftId, firstBusinessDay, 70.00m, "Rückstellung Auto", selfContact.Id);

            await AddDraftEntryAsync(giroDraftId, firstBusinessDay, -8.25m, "Rückstellung SDAC Jahresgebühr", selfContact.Id, sdacPlan.Id);
            await AddDraftEntryAsync(primarySavingsDraftId, firstBusinessDay, 8.25m, "Rückstellung SDAC Jahresgebühr", selfContact.Id);
            sdacReservePot += 8.25m;

            await AddDraftEntryAsync(giroDraftId, firstBusinessDay, -845.00m, "Wohnungsmiete", rentContact.Id);
            await AddDraftEntryAsync(giroDraftId, firstBusinessDay, -49.90m, "Mobilfunkvertrag Telkommi", telkommiContact.Id);

            if (monthStart.Month == 12)
            {
                var insuranceChargeDay = new DateTime(monthStart.Year, 12, 16);
                while (insuranceChargeDay.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                {
                    insuranceChargeDay = insuranceChargeDay.AddDays(1);
                }

                var householdRelease = Math.Min(householdReservePot, 62.64m);
                householdReservePot -= householdRelease;
                await AddDraftEntryAsync(
                    giroDraftId,
                    firstBusinessDay,
                    householdRelease,
                    "Auflösung Rückstellung Hausratversicherung",
                    selfContact.Id,
                    householdPlan.Id);

                await AddDraftEntryAsync(
                    primarySavingsDraftId,
                    firstBusinessDay,
                    -householdRelease,
                    "Auflösung Rückstellung Hausratversicherung",
                    selfContact.Id);

                await AddDraftEntryAsync(
                    giroDraftId,
                    insuranceChargeDay,
                    -62.60m,
                    $"Beitrag Hausratversicherung {monthStart.Year}, Vertragsnummer {householdContractNumber}",
                    insuranceContact.Id);
            }

            if (monthStart.Month == 1)
            {
                var sdacRelease = Math.Min(sdacReservePot, 99.00m);
                sdacReservePot -= sdacRelease;
                await AddDraftEntryAsync(
                    giroDraftId,
                    firstBusinessDay,
                    sdacRelease,
                    "Auflösung Rückstellung SDAC Jahresgebühr",
                    selfContact.Id,
                    sdacPlan.Id);

                await AddDraftEntryAsync(
                    primarySavingsDraftId,
                    firstBusinessDay,
                    -sdacRelease,
                    "Auflösung Rückstellung SDAC Jahresgebühr",
                    selfContact.Id);
            }

            var onlineStoreCount = random.Next(0, 2);
            if (onlineStoreCount == 0 && monthIndex >= totalMonthCount - 2)
                onlineStoreCount = 1;
            for (var storeIndex = 0; storeIndex < onlineStoreCount; storeIndex++)
            {
                var shopOffset = random.Next(0, onlineShopContacts.Count);
                var shop = onlineShopContacts[shopOffset];
                var paymentDay = ClampToBusinessDay(firstBusinessDay.AddDays(random.Next(0, 20)), monthStart, monthEnd);
                var amount = Math.Round(10m + ((decimal)random.NextDouble() * 20m), 2, MidpointRounding.AwayFromZero);
                var invoiceNo = $"INV-{random.Next(100000, 999999)}";
                var paymentSubject = $"{invoiceNo}, Ihr Einkauf bei {shop.Name}";
                await AddDraftEntryAsync(
                    giroDraftId,
                    paymentDay,
                    -amount,
                    paymentSubject,
                    shop.Id);
            }

            var weekCounter = 0;
            for (var weekStart = monthStart; weekStart <= monthEnd; weekStart = weekStart.AddDays(7))
            {
                ct.ThrowIfCancellationRequested();
                weekCounter++;

                var paymentsThisWeek = random.Next(1, 3);
                var priceFactor = (decimal)(3 / paymentsThisWeek);
                for (var paymentIndex = 0; paymentIndex < paymentsThisWeek; paymentIndex++)
                {
                    var shopOffset = random.Next(0, shopContacts.Length);
                    var shop = shopContacts[shopOffset];

                    var paymentDay = ClampToBusinessDay(weekStart.AddDays(random.Next(0, 7)), monthStart, monthEnd);
                    var amount = Math.Round(priceFactor * (10m + ((decimal)random.NextDouble() * 20m)), 2, MidpointRounding.AwayFromZero);
                    var paymentSubject = $"Kartenzahlung {shop.Name} W{weekCounter:D2}-{paymentIndex + 1:D2}";
                    await AddDraftEntryAsync(
                        giroDraftId,
                        paymentDay,
                        -amount,
                        paymentSubject,
                        shop.Id);
                }
            }

            if (monthIndex == 2)
            {
                var price = GetPriceForDate(worldPriceHistory, firstBusinessDay);
                var quantity = Math.Round(2000.00m / price, 6, MidpointRounding.AwayFromZero);
                await AddDraftEntryAsync(
                    giroDraftId,
                    firstBusinessDay,
                    -2000.00m,
                    "Wertpapierkauf USHSIV-MSCI WLD",
                    giroBankContact.Id,
                    null,
                    worldSecurity.Id,
                    SecurityTransactionType.Buy,
                    quantity,
                    null,
                    null);
            }

            if (monthIndex >= 2 && (monthIndex - 2) % 3 == 0)
            {
                var gross = Math.Round(15m + ((decimal)random.NextDouble() * 15m), 2, MidpointRounding.AwayFromZero);
                var tax = Math.Round(gross * 0.25m, 2, MidpointRounding.AwayFromZero);
                var net = gross - tax;
                await AddDraftEntryAsync(
                    giroDraftId,
                    firstBusinessDay,
                    net,
                    "Dividende USHSIV-MSCI WLD",
                    giroBankContact.Id,
                    null,
                    worldSecurity.Id,
                    SecurityTransactionType.Dividend,
                    null,
                    null,
                    tax);
                dividendAmount += net;
            }

            if (monthIndex == 5)
            {
                var price = GetPriceForDate(postPriceHistory, firstBusinessDay);
                var tradeAmount = Math.Round(price * 62.00m, 2, MidpointRounding.AwayFromZero);
                await AddDraftEntryAsync(
                    giroDraftId,
                    firstBusinessDay,
                    -tradeAmount,
                    "Wertpapierkauf Inländische Post AG",
                    giroBankContact.Id,
                    null,
                    postSecurity.Id,
                    SecurityTransactionType.Buy,
                    62.00m,
                    null,
                    null);
            }

            if (monthIndex >= 5 && monthStart.Month == 5)
            {
                var price = GetPriceForDate(postPriceHistory, firstBusinessDay);
                var currentValue = Math.Round(price * 62.00m, 2, MidpointRounding.AwayFromZero);
                var gross = Math.Round(currentValue * 0.04m, 2, MidpointRounding.AwayFromZero);
                var tax = Math.Round(gross * 0.25m, 2, MidpointRounding.AwayFromZero);
                var net = gross - tax;
                await AddDraftEntryAsync(
                    giroDraftId,
                    firstBusinessDay,
                    net,
                    "Dividende Inländische Post AG",
                    giroBankContact.Id,
                    null,
                    postSecurity.Id,
                    SecurityTransactionType.Dividend,
                    null,
                    null,
                    tax);
                dividendAmount += net;
            }

            if (dividendAmount > 0)
            {
                await AddDraftEntryAsync(
                    giroDraftId,
                    lastBusinessDay,
                    -dividendAmount,
                    "Rückstellung Aktienneukauf",
                    selfContact.Id,
                    securityBuySavingPlan.Id,
                    null,
                    null,
                    null,
                    null,
                    null);
                await AddDraftEntryAsync(
                    primarySavingsDraftId,
                    firstBusinessDay,
                    dividendAmount,
                    "Rückstellung Aktienneukauf",
                    giroBankContact.Id);
            }

            if (!isCurrentMonth)
            {
                await BookDraftAsync(giroDraftId);
                await BookDraftAsync(primarySavingsDraftId);
                await BookDraftAsync(secondarySavingsDraftId);
            }
        }
    }

    private async Task EnsureDefaultHomeKpisAsync(Guid userId, CancellationToken ct)
    {
        var existingKpis = await _homeKpiService.ListAsync(userId, ct);
        if (existingKpis.Count > 0)
        {
            return;
        }

        var defaults = new[]
        {
            HomeKpiPredefined.AccountsAggregates,
            HomeKpiPredefined.SavingsPlanAggregates,
            HomeKpiPredefined.SecuritiesDividends,
            HomeKpiPredefined.MonthlyBudget,
            HomeKpiPredefined.OpenStatementDraftsCount
        };

        for (var sortOrder = 0; sortOrder < defaults.Length; sortOrder++)
        {
            await _homeKpiService.CreateAsync(
                userId,
                new HomeKpiCreateRequest(
                    HomeKpiKind.Predefined,
                    null,
                    defaults[sortOrder],
                    null,
                    HomeKpiDisplayMode.TotalOnly,
                    sortOrder),
                ct);
        }
    }

    private async Task EnsureDefaultReportFavoritesAsync(Guid userId, CancellationToken ct)
    {
        var existingFavorites = await _reportFavoriteService.ListAsync(userId, ct);
        if (existingFavorites.Count > 0)
        {
            return;
        }

        await _reportFavoriteService.CreateAsync(
            userId,
            new ReportFavoriteCreateRequest(
                "Contacts Monthly Analysis",
                PostingKind.Contact,
                includeCategory: true,
                ReportInterval.Month,
                comparePrevious: true,
                compareYear: true,
                compareProjection: false,
                showChart: true,
                expandable: true),
            ct);

        await _reportFavoriteService.CreateAsync(
            userId,
            new ReportFavoriteCreateRequest(
                "Securities Projection",
                PostingKind.Security,
                includeCategory: false,
                ReportInterval.Month,
                comparePrevious: false,
                compareYear: false,
                compareProjection: true,
                showChart: true,
                expandable: true),
            ct);
    }

    private async Task<Dictionary<DateTime, decimal>> CreateSecurityPriceHistoryAsync(
        Guid userId,
        SecurityDto security,
        DateTime referenceMonthStart,
        decimal startPrice,
        Random random,
        CancellationToken ct)
    {
        var culture = CultureInfo.GetCultureInfo("de-DE");
        var priceHistory = new Dictionary<DateTime, decimal>();
        var firstDate = referenceMonthStart.AddYears(-2);
        var lastDate = referenceMonthStart;

        var close = startPrice;
        var firstPriceCreated = false;
        for (var day = firstDate; day <= lastDate; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            if (firstPriceCreated)
            {
                var factor = -0.010m + ((decimal)random.NextDouble() * 0.021m);
                close = Math.Round(close * (1m + factor), 2, MidpointRounding.AwayFromZero);
                if (close <= 0m)
                {
                    close = 0.01m;
                }
            }

            priceHistory[day.Date] = close;
            firstPriceCreated = true;
        }

        var csv = new StringBuilder();
        csv.AppendLine("Wertpapierhistorie");
        csv.AppendLine($"Zeit;{security.Name}");
        foreach (var item in priceHistory.OrderBy(x => x.Key))
        {
            csv.Append(item.Key.ToString("dd.MM.yyyy HH:mm:ss", culture));
            csv.Append(';');
            csv.AppendLine(item.Value.ToString("0.00", culture));
        }

        var context = new SecurityPriceImportContext("ing", $"demo-{security.Identifier}.csv", "text/csv");
        var importService = _securityPriceImportServiceFactory.Resolve(context);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv.ToString()));
        var importResult = await importService.ImportAsync(userId, security.Id, stream, context, ct);
        if (importResult.Errors.Count > 0)
        {
            var error = string.Join("; ", importResult.Errors.Select(x => $"L{x.LineNumber}: {x.Message}"));
            throw new InvalidOperationException($"Security price import failed for {security.Name}: {error}");
        }

        _logger.LogInformation(
            "Imported {Inserted} security prices for {SecurityName} ({SecurityId})",
            importResult.Inserted + importResult.Updated + importResult.Unchanged,
            security.Name,
            security.Id);

        return priceHistory;
    }

    private static DateTime GetFirstBusinessDayOfMonth(DateTime monthStart)
    {
        var day = new DateTime(monthStart.Year, monthStart.Month, 1);
        while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            day = day.AddDays(1);
        }

        return day;
    }

    private static DateTime GetLastBusinessDayOfMonth(DateTime monthStart)
    {
        var day = new DateTime(monthStart.Year, monthStart.Month, 1).AddMonths(1).AddDays(-1);
        while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            day = day.AddDays(-1);
        }

        return day;
    }

    private static int BuildDeterministicSeed()
        => 907_240_113;
}
