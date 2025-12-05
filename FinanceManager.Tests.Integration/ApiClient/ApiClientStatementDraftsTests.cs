using System.Net.Http.Headers;
using FinanceManager.Shared;
using FinanceManager.Shared.Dtos.Accounts;
using FinanceManager.Shared.Dtos.Contacts;
using FinanceManager.Shared.Dtos.Statements;
using FinanceManager.Shared.Dtos.Users;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FinanceManager.Tests.Integration.ApiClient;

public class ApiClientStatementDraftsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ApiClientStatementDraftsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private FinanceManager.Shared.ApiClient CreateClient()
    {
        var http = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        return new FinanceManager.Shared.ApiClient(http);
    }

    private async Task EnsureAuthenticatedAsync(FinanceManager.Shared.ApiClient api)
    {
        var username = $"user_{Guid.NewGuid():N}";
        await api.Auth_RegisterAsync(new RegisterRequest(username, "Secret123", PreferredLanguage: null, TimeZoneId: null));
    }

    [Fact]
    public async Task StatementDrafts_Flow_Upload_List_Get_SetAccount_AddEntry_Validate_Book_DeleteAll()
    {
        var api = CreateClient();
        await EnsureAuthenticatedAsync(api);

        // Ensure account exists
        var accounts = await api.GetAccountsAsync();
        Guid accountId;
        string accountIban = "";
        if (accounts.Count == 0)
        {
            var acc = await api.CreateAccountAsync(new AccountCreateRequest(
                Name: "Test Account",
                Type: AccountType.Giro,
                Iban: "DE50700500000007882989",
                BankContactId: null,
                NewBankContactName: "Test Bank",
                SymbolAttachmentId: null,
                SavingsPlanExpectation: SavingsPlanExpectation.Optional));
            accountId = acc.Id;
            accountIban = acc.Iban;
        }
        else { accountId = accounts[0].Id; accountIban = accounts[0].Iban; }

        // Initially no drafts
        var open = await api.StatementDrafts_ListOpenAsync(0, 3);
        open.Should().NotBeNull();
        open.Should().BeEmpty();

        // Upload
        var csv = "Umsatzanzeige;Datei erstellt am: 02.12.2025 19:04\r\n" +
            "\r\n" +
            $"IBAN;{accountIban}\r\n" +
            "Kontoname;Girokonto\r\n" +
            "Bank;ING\r\n" +
            "Kunde;Admin\r\n" +
            "Zeitraum;02.11.2025 - 02.12.2025\r\n" +
            "Saldo;2.776,45;EUR\r\n" +
            "\r\n" +
            "Sortierung;Datum absteigend\r\n" +
            "\r\n" +
            "\r\n" +
            "Buchung;Wertstellungsdatum;Auftraggeber/Empfänger;Buchungstext;Verwendungszweck;Saldo;Währung;Betrag;Währung\r\n" +
            "02.12.2025;02.12.2025;Testempfänger;Überweisung;Ihr Einkauf;2.776,45;EUR;-206,44;EUR\r\n";
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var upload = await api.StatementDrafts_UploadAsync(ms, "statement.csv");
        upload.Should().NotBeNull();
        var first = upload!.FirstDraft;
        first.Should().NotBeNull();
        first!.DetectedAccountId.Should().Be(accountId);

        // List open drafts should have one
        open = await api.StatementDrafts_ListOpenAsync(0, 3);
        open.Should().HaveCount(1);

        // Get detail
        var detail = await api.StatementDrafts_GetAsync(first!.DraftId);
        detail.Should().NotBeNull();
        detail!.DraftId.Should().Be(first!.DraftId);
        detail.Entries.Should().NotBeNull();
        detail.Entries.Count.Should().BeGreaterThan(0);

        // Switch account
        var secondAccount = await api.CreateAccountAsync(new AccountCreateRequest(
                Name: "Second Account",
                Type: AccountType.Giro,
                Iban: "DE50700500000007882990",
                BankContactId: null,
                NewBankContactName: "Test Bank",
                SymbolAttachmentId: null,
                SavingsPlanExpectation: SavingsPlanExpectation.Optional));
        var afterAccount = await api.StatementDrafts_SetAccountAsync(first.DraftId, secondAccount.Id);
        afterAccount.Should().NotBeNull();
        afterAccount!.DetectedAccountId.Should().Be(secondAccount.Id);

        // Add a manual entry
        var added = await api.StatementDrafts_AddEntryAsync(first.DraftId, new StatementDraftAddEntryRequest(DateTime.UtcNow.Date, 10.00m, "Manual"));
        added.Should().NotBeNull();
        added!.Entries.Should().NotBeNull();
        added!.Entries.Any(e => e.Subject == "Manual").Should().BeTrue();

        // Validate draft
        var val = await api.StatementDrafts_ValidateAsync(first.DraftId);
        val.Should().NotBeNull();
        val!.DraftId.Should().Be(first.DraftId);

        // Attempt booking (expect warning/error due to missing contact on first entry)
        var book = await api.StatementDrafts_BookAsync(first.DraftId, forceWarnings: false);
        book.Should().NotBeNull();
        book!.Success.Should().BeFalse();
        book.HasWarnings.Should().BeFalse();
        book.Validation.Messages.Any(m => m.Code == "ENTRY_NO_CONTACT" || m.Message.Contains("Kein Kontakt")).Should().BeTrue();

        // Assign a contact to the first entry then re-book
        var firstEntryId = detail.Entries.First().Id;
        // Create a real contact to assign
        var contact = await api.Contacts_CreateAsync(new ContactCreateRequest(
            Name: "Test Contact",
            Description: "For Statement Draft",
            Type: ContactType.Other,
            CategoryId: null,
            IsPaymentIntermediary: false));
        contact.Should().NotBeNull();
        var assign = await api.StatementDrafts_SetEntryContactAsync(first.DraftId, firstEntryId, new StatementDraftSetContactRequest(contact!.Id));
        assign.Should().NotBeNull();
        assign.ContactId.Should().Be(contact.Id);

        var book2 = await api.StatementDrafts_BookAsync(first.DraftId, forceWarnings: true);
        book2.Should().NotBeNull();
        book2!.Success.Should().BeTrue();

        // After successful booking, the draft should be gone
        open = await api.StatementDrafts_ListOpenAsync(0, 3);
        open.Should().NotBeNull();
        open.Should().BeEmpty();
    }

    [Fact]
    public async Task StatementDrafts_Upload_And_DeleteAll_Works()
    {
        var api = CreateClient();
        await EnsureAuthenticatedAsync(api);

        // ensure account
        var accounts = await api.GetAccountsAsync();
        Guid accountId;
        string accountIban = "";
        if (accounts.Count == 0)
        {
            var acc = await api.CreateAccountAsync(new AccountCreateRequest(
                Name: "Test Account",
                Type: AccountType.Giro,
                Iban: "DE50700500000007882991",
                BankContactId: null,
                NewBankContactName: "Test Bank",
                SymbolAttachmentId: null,
                SavingsPlanExpectation: SavingsPlanExpectation.Optional));
            accountId = acc.Id;
            accountIban = acc.Iban;
        }
        else { accountId = accounts[0].Id; accountIban = accounts[0].Iban; }

        var csv = "Umsatzanzeige;Datei erstellt am: 02.12.2025 19:04\r\n\r\n" +
                  $"IBAN;{accountIban}\r\n" +
                  "Kontoname;Girokonto\r\nBank;ING\r\nKunde;Admin\r\n" +
                  "Zeitraum;02.11.2025 - 02.12.2025\r\nSaldo;2.776,45;EUR\r\n\r\n" +
                  "Sortierung;Datum absteigend\r\n\r\n\r\n" +
                  "Buchung;Wertstellungsdatum;Auftraggeber/Empfänger;Buchungstext;Verwendungszweck;Saldo;Währung;Betrag;Währung\r\n" +
                  "02.12.2025;02.12.2025;Testempfänger;Überweisung;Ihr Einkauf;2.776,45;EUR;-206,44;EUR\r\n";
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var upload = await api.StatementDrafts_UploadAsync(ms, "statement2.csv");
        upload.Should().NotBeNull();
        var first = upload!.FirstDraft;
        first.Should().NotBeNull();

        var open = await api.StatementDrafts_ListOpenAsync(0, 3);
        open.Should().NotBeNull();
        open.Should().NotBeEmpty();

        var ok = await api.StatementDrafts_DeleteAllAsync();
        ok.Should().BeTrue();

        open = await api.StatementDrafts_ListOpenAsync(0, 3);
        open.Should().NotBeNull();
        open.Should().BeEmpty();
    }
}
