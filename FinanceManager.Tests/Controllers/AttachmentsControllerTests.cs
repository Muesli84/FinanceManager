using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Application;
using FinanceManager.Application.Attachments;
using FinanceManager.Domain.Attachments;
using FinanceManager.Shared.Dtos;
using FinanceManager.Web.Controllers;
using FinanceManager.Web.Infrastructure.Attachments;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FinanceManager.Tests.Controllers;

public sealed class AttachmentsControllerTests
{
    private sealed class TestCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; set; } = Guid.NewGuid();
        public string? PreferredLanguage => null;
        public bool IsAuthenticated => true;
        public bool IsAdmin => false;
    }

    private static (
        AttachmentsController controller,
        Mock<IAttachmentService> service,
        Mock<IAttachmentCategoryService> cats,
        TestCurrentUser current
    ) Create(AttachmentUploadOptions? options = null)
    {
        var svc = new Mock<IAttachmentService>(MockBehavior.Strict);
        var cats = new Mock<IAttachmentCategoryService>(MockBehavior.Strict);
        var current = new TestCurrentUser();
        var opts = Options.Create(options ?? new AttachmentUploadOptions
        {
            MaxSizeBytes = 10 * 1024, // 10 KB for tests
            AllowedMimeTypes = new[] { "application/pdf", "image/png", "text/plain" }
        });
        var controller = new AttachmentsController(svc.Object, cats.Object, current, NullLogger<AttachmentsController>.Instance, opts);
        return (controller, svc, cats, current);
    }

    [Fact]
    public async Task UploadAsync_ShouldReject_EmptyFile()
    {
        var (controller, _, _, _) = Create();
        var stream = new MemoryStream(Array.Empty<byte>());
        var formFile = new FormFile(stream, 0, 0, "file", "a.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };

        var resp = await controller.UploadAsync((short)AttachmentEntityKind.Contact, Guid.NewGuid(), formFile, null, null, CancellationToken.None);

        var bad = resp.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.Value!.ToString()!.ToLowerInvariant().Should().Contain("empty file");
    }

    [Fact]
    public async Task UploadAsync_ShouldReject_TooLarge()
    {
        var opts = new AttachmentUploadOptions { MaxSizeBytes = 5, AllowedMimeTypes = new[] { "application/pdf" } };
        var (controller, _, _, _) = Create(opts);
        var data = new byte[6];
        var formFile = new FormFile(new MemoryStream(data), 0, data.Length, "file", "a.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };

        var resp = await controller.UploadAsync((short)AttachmentEntityKind.Contact, Guid.NewGuid(), formFile, null, null, CancellationToken.None);

        var bad = resp.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.Value!.ToString()!.ToLowerInvariant().Should().Contain("file too large");
    }

    [Fact]
    public async Task UploadAsync_ShouldReject_UnsupportedContentType()
    {
        var opts = new AttachmentUploadOptions { MaxSizeBytes = 1024, AllowedMimeTypes = new[] { "application/pdf" } };
        var (controller, _, _, _) = Create(opts);
        var data = new byte[10];
        // content type empty simulates browser not sending type reliably
        var formFile = new FormFile(new MemoryStream(data), 0, data.Length, "file", "a.bin") { Headers = new HeaderDictionary(), ContentType = string.Empty };

        var resp = await controller.UploadAsync((short)AttachmentEntityKind.Contact, Guid.NewGuid(), formFile, null, null, CancellationToken.None);

        var bad = resp.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.Value!.ToString()!.ToLowerInvariant().Should().Contain("unsupported content type");
    }

    [Fact]
    public async Task UploadAsync_ShouldAccept_ValidPdf()
    {
        var opts = new AttachmentUploadOptions { MaxSizeBytes = 1024, AllowedMimeTypes = new[] { "application/pdf" } };
        var (controller, service, _, current) = Create(opts);
        var data = new byte[10];
        var formFile = new FormFile(new MemoryStream(data), 0, data.Length, "file", "doc.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
        var dto = new AttachmentDto(
            Id: Guid.NewGuid(),
            EntityKind: (short)AttachmentEntityKind.Contact,
            EntityId: Guid.NewGuid(),
            FileName: "doc.pdf",
            ContentType: "application/pdf",
            SizeBytes: 10,
            CategoryId: null,
            UploadedUtc: DateTime.UtcNow,
            IsUrl: false);

        service.Setup(s => s.UploadAsync(current.UserId, AttachmentEntityKind.Contact, It.IsAny<Guid>(), It.IsAny<Stream>(), "doc.pdf", "application/pdf", null, It.IsAny<CancellationToken>()))
               .ReturnsAsync(dto);

        var resp = await controller.UploadAsync((short)AttachmentEntityKind.Contact, Guid.NewGuid(), formFile, null, null, CancellationToken.None);

        var ok = resp.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeAssignableTo<AttachmentDto>();
        service.VerifyAll();
    }

    [Fact]
    public async Task UploadAsync_ShouldCreateUrl_WhenUrlProvided()
    {
        var (controller, service, _, current) = Create();
        var entityId = Guid.NewGuid();
        var dto = new AttachmentDto(
            Id: Guid.NewGuid(),
            EntityKind: (short)AttachmentEntityKind.Contact,
            EntityId: entityId,
            FileName: "http://example",
            ContentType: "text/plain",
            SizeBytes: 0,
            CategoryId: null,
            UploadedUtc: DateTime.UtcNow,
            IsUrl: true);

        service.Setup(s => s.CreateUrlAsync(current.UserId, AttachmentEntityKind.Contact, entityId, "http://example", null, null, It.IsAny<CancellationToken>()))
               .ReturnsAsync(dto);

        var resp = await controller.UploadAsync((short)AttachmentEntityKind.Contact, entityId, null, null, "http://example", CancellationToken.None);

        var ok = resp.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeAssignableTo<AttachmentDto>();
        service.VerifyAll();
    }

    [Fact]
    public async Task UploadAsync_ShouldReject_WhenNeitherFileNorUrlProvided()
    {
        var (controller, _, _, _) = Create();

        var resp = await controller.UploadAsync((short)AttachmentEntityKind.Contact, Guid.NewGuid(), null, null, null, CancellationToken.None);

        var bad = resp.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.Value!.ToString()!.ToLowerInvariant().Should().Contain("file or url");
    }

    [Fact]
    public async Task UploadAsync_ShouldReject_InvalidEntityKind()
    {
        var (controller, _, _, _) = Create();
        var resp = await controller.UploadAsync(short.MaxValue, Guid.NewGuid(), null, null, "http://example", CancellationToken.None);
        var bad = resp.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.Value!.ToString()!.ToLowerInvariant().Should().Contain("invalid entitykind");
    }

    [Fact]
    public async Task UploadAsync_ShouldPass_CategoryId_ToService_OnUpload()
    {
        var opts = new AttachmentUploadOptions { MaxSizeBytes = 1024, AllowedMimeTypes = new[] { "application/pdf" } };
        var (controller, service, _, current) = Create(opts);
        var entityId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var data = new byte[10];
        var formFile = new FormFile(new MemoryStream(data), 0, data.Length, "file", "doc.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };

        service.Setup(s => s.UploadAsync(current.UserId, AttachmentEntityKind.Contact, entityId, It.IsAny<Stream>(), "doc.pdf", "application/pdf", categoryId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new AttachmentDto(Guid.NewGuid(), (short)AttachmentEntityKind.Contact, entityId, "doc.pdf", "application/pdf", 10, categoryId, DateTime.UtcNow, false));

        var resp = await controller.UploadAsync((short)AttachmentEntityKind.Contact, entityId, formFile, categoryId, null, CancellationToken.None);
        resp.Should().BeOfType<OkObjectResult>();
        service.VerifyAll();
    }

    [Fact]
    public async Task UploadAsync_ShouldPass_CategoryId_ToService_OnCreateUrl()
    {
        var (controller, service, _, current) = Create();
        var entityId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        service.Setup(s => s.CreateUrlAsync(current.UserId, AttachmentEntityKind.Contact, entityId, "http://example", null, categoryId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new AttachmentDto(Guid.NewGuid(), (short)AttachmentEntityKind.Contact, entityId, "http://example", "text/plain", 0, categoryId, DateTime.UtcNow, true));

        var resp = await controller.UploadAsync((short)AttachmentEntityKind.Contact, entityId, null, categoryId, "http://example", CancellationToken.None);
        resp.Should().BeOfType<OkObjectResult>();
        service.VerifyAll();
    }

    [Fact]
    public async Task DownloadAsync_ShouldReturn_NotFound_WhenMissing()
    {
        var (controller, service, _, current) = Create();
        var id = Guid.NewGuid();
        service.Setup(s => s.DownloadAsync(current.UserId, id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(((Stream, string, string)?)null);

        var resp = await controller.DownloadAsync(id, CancellationToken.None);
        resp.Should().BeOfType<NotFoundResult>();
        service.VerifyAll();
    }

    [Fact]
    public async Task DownloadAsync_ShouldReturn_FileContentResult()
    {
        var (controller, service, _, current) = Create();
        var id = Guid.NewGuid();
        var content = new MemoryStream(new byte[] { 1, 2, 3 });
        service.Setup(s => s.DownloadAsync(current.UserId, id, It.IsAny<CancellationToken>()))
               .ReturnsAsync((content, "file.bin", "application/octet-stream"));

        var resp = await controller.DownloadAsync(id, CancellationToken.None);
        var file = resp.Should().BeOfType<FileStreamResult>().Subject;
        file.FileDownloadName.Should().Be("file.bin");
        file.ContentType.Should().Be("application/octet-stream");
        service.VerifyAll();
    }

    [Fact]
    public async Task DeleteAsync_ShouldReturn_NoContent_WhenDeleted()
    {
        var (controller, service, _, current) = Create();
        var id = Guid.NewGuid();
        service.Setup(s => s.DeleteAsync(current.UserId, id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var resp = await controller.DeleteAsync(id, CancellationToken.None);
        resp.Should().BeOfType<NoContentResult>();
        service.VerifyAll();
    }

    [Fact]
    public async Task DeleteAsync_ShouldReturn_NotFound_WhenMissing()
    {
        var (controller, service, _, current) = Create();
        var id = Guid.NewGuid();
        service.Setup(s => s.DeleteAsync(current.UserId, id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var resp = await controller.DeleteAsync(id, CancellationToken.None);
        resp.Should().BeOfType<NotFoundResult>();
        service.VerifyAll();
    }

    [Fact]
    public async Task UpdateAsync_ShouldReturn_NoContent_WhenUpdated()
    {
        var (controller, service, _, current) = Create();
        var id = Guid.NewGuid();
        service.Setup(s => s.UpdateCoreAsync(current.UserId, id, "name.pdf", null, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var resp = await controller.UpdateAsync(id, new AttachmentsController.UpdateCoreRequest("name.pdf", null), CancellationToken.None);
        resp.Should().BeOfType<NoContentResult>();
        service.VerifyAll();
    }

    [Fact]
    public async Task UpdateAsync_ShouldReturn_NotFound_WhenMissing()
    {
        var (controller, service, _, current) = Create();
        var id = Guid.NewGuid();
        service.Setup(s => s.UpdateCoreAsync(current.UserId, id, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var resp = await controller.UpdateAsync(id, new AttachmentsController.UpdateCoreRequest(null, null), CancellationToken.None);
        resp.Should().BeOfType<NotFoundResult>();
        service.VerifyAll();
    }

    [Fact]
    public async Task UpdateCategoryAsync_ShouldReturn_NoContent_WhenUpdated()
    {
        var (controller, service, _, current) = Create();
        var id = Guid.NewGuid();
        var cat = Guid.NewGuid();
        service.Setup(s => s.UpdateCategoryAsync(current.UserId, id, cat, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var resp = await controller.UpdateCategoryAsync(id, new AttachmentsController.UpdateCategoryRequest(cat), CancellationToken.None);
        resp.Should().BeOfType<NoContentResult>();
        service.VerifyAll();
    }

    [Fact]
    public async Task UpdateCategoryAsync_ShouldReturn_NotFound_WhenMissing()
    {
        var (controller, service, _, current) = Create();
        var id = Guid.NewGuid();
        service.Setup(s => s.UpdateCategoryAsync(current.UserId, id, null, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var resp = await controller.UpdateCategoryAsync(id, new AttachmentsController.UpdateCategoryRequest(null), CancellationToken.None);
        resp.Should().BeOfType<NotFoundResult>();
        service.VerifyAll();
    }

    [Fact]
    public async Task ListAsync_ShouldReject_InvalidEntityKind()
    {
        var (controller, _, _, _) = Create();
        var resp = await controller.ListAsync(short.MaxValue, Guid.NewGuid(), 0, 50, null, null, null, CancellationToken.None);
        var bad = resp.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.Value!.ToString()!.ToLowerInvariant().Should().Contain("invalid entitykind");
    }

    [Fact]
    public async Task ListAsync_ShouldReturn_EnvelopeWithItems()
    {
        var (controller, service, _, current) = Create();
        var entityId = Guid.NewGuid();
        var list = new[] { new AttachmentDto(Guid.NewGuid(), (short)AttachmentEntityKind.Contact, entityId, "a.pdf", "application/pdf", 1, null, DateTime.UtcNow, false) } as IReadOnlyList<AttachmentDto>;
        service.Setup(s => s.ListAsync(current.UserId, AttachmentEntityKind.Contact, entityId, 0, 50, null, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(list);
        service.Setup(s => s.CountAsync(current.UserId, AttachmentEntityKind.Contact, entityId, null, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var resp = await controller.ListAsync((short)AttachmentEntityKind.Contact, entityId, 0, 50, null, null, null, CancellationToken.None);
        var ok = resp.Should().BeOfType<OkObjectResult>().Subject;
        var page = ok.Value.Should().BeAssignableTo<PageResult<AttachmentDto>>().Subject;
        page.Items.Should().BeEquivalentTo(list);
        page.HasMore.Should().BeFalse();
        page.Total.Should().Be(1);
        service.VerifyAll();
    }

    [Fact]
    public async Task ListCategoriesAsync_ShouldReturn_ListFromService()
    {
        var (controller, _, cats, current) = Create();
        var list = new[] { new AttachmentCategoryDto(Guid.NewGuid(), "Docs", false, false) } as IReadOnlyList<AttachmentCategoryDto>;
        cats.Setup(s => s.ListAsync(current.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(list);

        var resp = await controller.ListCategoriesAsync(CancellationToken.None);
        var ok = resp.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(list);
        cats.VerifyAll();
    }

    [Fact]
    public async Task CreateCategoryAsync_ShouldReturn_Created()
    {
        var (controller, _, cats, current) = Create();
        var dto = new AttachmentCategoryDto(Guid.NewGuid(), "Invoices", false, false);
        cats.Setup(s => s.CreateAsync(current.UserId, "Invoices", It.IsAny<CancellationToken>())).ReturnsAsync(dto);

        var resp = await controller.CreateCategoryAsync(new AttachmentsController.CreateCategoryRequest("Invoices"), CancellationToken.None);
        var created = resp.Should().BeOfType<CreatedResult>().Subject;
        created.Value.Should().Be(dto);
        created.Location.Should().Be("/api/attachments/categories");
        created.StatusCode.Should().Be(201);
        cats.VerifyAll();
    }

    [Fact]
    public async Task CreateCategoryAsync_ShouldReturn_ValidationProblem_WhenModelInvalid()
    {
        var (controller, _, cats, _) = Create();
        controller.ModelState.AddModelError("Name", "Required");

        var resp = await controller.CreateCategoryAsync(new AttachmentsController.CreateCategoryRequest(""), CancellationToken.None);
        resp.Should().BeOfType<ObjectResult>(); // ValidationProblem returns ObjectResult in unit tests
        cats.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateCategoryNameAsync_ShouldReturn_Ok()
    {
        var (controller, _, cats, current) = Create();
        var id = Guid.NewGuid();
        var dto = new AttachmentCategoryDto(id, "NewName", false, true);
        cats.Setup(s => s.UpdateAsync(current.UserId, id, "NewName", It.IsAny<CancellationToken>())).ReturnsAsync(dto);

        var resp = await controller.UpdateCategoryNameAsync(id, new AttachmentsController.UpdateCategoryNameRequest("NewName"), CancellationToken.None);
        var ok = resp.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().Be(dto);
        cats.VerifyAll();
    }

    [Fact]
    public async Task UpdateCategoryNameAsync_ShouldReturn_Conflict_OnInvalidOperation()
    {
        var (controller, _, cats, current) = Create();
        var id = Guid.NewGuid();
        cats.Setup(s => s.UpdateAsync(current.UserId, id, "Dup", It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("duplicate"));

        var resp = await controller.UpdateCategoryNameAsync(id, new AttachmentsController.UpdateCategoryNameRequest("Dup"), CancellationToken.None);
        resp.Should().BeOfType<ConflictObjectResult>();
        cats.VerifyAll();
    }

    [Fact]
    public async Task UpdateCategoryNameAsync_ShouldReturn_ValidationProblem_WhenModelInvalid()
    {
        var (controller, _, cats, _) = Create();
        controller.ModelState.AddModelError("Name", "too short");

        var resp = await controller.UpdateCategoryNameAsync(Guid.NewGuid(), new AttachmentsController.UpdateCategoryNameRequest(""), CancellationToken.None);
        resp.Should().BeOfType<ObjectResult>(); // ValidationProblem
        cats.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateCategoryNameAsync_ShouldReturn_NotFound_WhenServiceReturnsNull()
    {
        var (controller, _, cats, current) = Create();
        var id = Guid.NewGuid();
        cats.Setup(s => s.UpdateAsync(current.UserId, id, "Missing", It.IsAny<CancellationToken>())).ReturnsAsync((AttachmentCategoryDto?)null);

        var resp = await controller.UpdateCategoryNameAsync(id, new AttachmentsController.UpdateCategoryNameRequest("Missing"), CancellationToken.None);
        resp.Should().BeOfType<NotFoundResult>();
        cats.VerifyAll();
    }

    [Fact]
    public async Task DeleteCategoryAsync_ShouldReturn_NoContent_WhenDeleted()
    {
        var (controller, _, cats, current) = Create();
        var id = Guid.NewGuid();
        cats.Setup(s => s.DeleteAsync(current.UserId, id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var resp = await controller.DeleteCategoryAsync(id, CancellationToken.None);
        resp.Should().BeOfType<NoContentResult>();
        cats.VerifyAll();
    }

    [Fact]
    public async Task DeleteCategoryAsync_ShouldReturn_NotFound_WhenMissing()
    {
        var (controller, _, cats, current) = Create();
        var id = Guid.NewGuid();
        cats.Setup(s => s.DeleteAsync(current.UserId, id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var resp = await controller.DeleteCategoryAsync(id, CancellationToken.None);
        resp.Should().BeOfType<NotFoundResult>();
        cats.VerifyAll();
    }

    [Fact]
    public async Task DeleteCategoryAsync_ShouldReturn_Conflict_OnInvalidOperation()
    {
        var (controller, _, cats, current) = Create();
        var id = Guid.NewGuid();
        cats.Setup(s => s.DeleteAsync(current.UserId, id, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("in use"));

        var resp = await controller.DeleteCategoryAsync(id, CancellationToken.None);
        resp.Should().BeOfType<ConflictObjectResult>();
        cats.VerifyAll();
    }

    [Fact]
    public async Task CreateCategoryAsync_ShouldReturn_BadRequest_OnArgumentException()
    {
        var (controller, _, cats, current) = Create();
        cats.Setup(s => s.CreateAsync(current.UserId, "Bad", It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("bad"));

        var resp = await controller.CreateCategoryAsync(new AttachmentsController.CreateCategoryRequest("Bad"), CancellationToken.None);
        resp.Should().BeOfType<BadRequestObjectResult>();
        cats.VerifyAll();
    }

    [Fact]
    public async Task UploadAsync_ShouldReturn_BadRequest_WhenServiceThrowsArgumentException_OnUrl()
    {
        var (controller, service, _, current) = Create();
        var entityId = Guid.NewGuid();
        service.Setup(s => s.CreateUrlAsync(current.UserId, AttachmentEntityKind.Contact, entityId, "http://bad", null, null, It.IsAny<CancellationToken>()))
               .ThrowsAsync(new ArgumentException("bad url"));

        var resp = await controller.UploadAsync((short)AttachmentEntityKind.Contact, entityId, null, null, "http://bad", CancellationToken.None);
        resp.Should().BeOfType<BadRequestObjectResult>();
        service.VerifyAll();
    }

    [Fact]
    public async Task UploadAsync_ShouldReturn_Problem500_WhenServiceThrows_OnUrl()
    {
        var (controller, service, _, current) = Create();
        var entityId = Guid.NewGuid();
        service.Setup(s => s.CreateUrlAsync(current.UserId, AttachmentEntityKind.Contact, entityId, "http://err", null, null, It.IsAny<CancellationToken>()))
               .ThrowsAsync(new Exception("boom"));

        var resp = await controller.UploadAsync((short)AttachmentEntityKind.Contact, entityId, null, null, "http://err", CancellationToken.None);
        var problem = resp.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(500);
        service.VerifyAll();
    }

    [Fact]
    public async Task UploadAsync_ShouldReturn_BadRequest_WhenServiceThrowsArgumentException_OnFile()
    {
        var opts = new AttachmentUploadOptions { MaxSizeBytes = 1024, AllowedMimeTypes = new[] { "application/pdf" } };
        var (controller, service, _, current) = Create(opts);
        var entityId = Guid.NewGuid();
        var data = new byte[10];
        var formFile = new FormFile(new MemoryStream(data), 0, data.Length, "file", "doc.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
        service.Setup(s => s.UploadAsync(current.UserId, AttachmentEntityKind.Contact, entityId, It.IsAny<Stream>(), "doc.pdf", "application/pdf", null, It.IsAny<CancellationToken>()))
               .ThrowsAsync(new ArgumentException("invalid"));

        var resp = await controller.UploadAsync((short)AttachmentEntityKind.Contact, entityId, formFile, null, null, CancellationToken.None);
        resp.Should().BeOfType<BadRequestObjectResult>();
        service.VerifyAll();
    }

    [Fact]
    public async Task UploadAsync_ShouldReturn_Problem500_WhenServiceThrows_OnFile()
    {
        var opts = new AttachmentUploadOptions { MaxSizeBytes = 1024, AllowedMimeTypes = new[] { "application/pdf" } };
        var (controller, service, _, current) = Create(opts);
        var entityId = Guid.NewGuid();
        var data = new byte[10];
        var formFile = new FormFile(new MemoryStream(data), 0, data.Length, "file", "doc.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
        service.Setup(s => s.UploadAsync(current.UserId, AttachmentEntityKind.Contact, entityId, It.IsAny<Stream>(), "doc.pdf", "application/pdf", null, It.IsAny<CancellationToken>()))
               .ThrowsAsync(new Exception("err"));

        var resp = await controller.UploadAsync((short)AttachmentEntityKind.Contact, entityId, formFile, null, null, CancellationToken.None);
        var problem = resp.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(500);
        service.VerifyAll();
    }

    [Fact]
    public async Task UploadAsync_ShouldAccept_ContentType_CaseInsensitive()
    {
        var opts = new AttachmentUploadOptions { MaxSizeBytes = 1024, AllowedMimeTypes = new[] { "application/pdf" } };
        var (controller, service, _, current) = Create(opts);
        var data = new byte[10];
        var formFile = new FormFile(new MemoryStream(data), 0, data.Length, "file", "doc.pdf") { Headers = new HeaderDictionary(), ContentType = "Application/PDF" };
        service.Setup(s => s.UploadAsync(current.UserId, AttachmentEntityKind.Contact, It.IsAny<Guid>(), It.IsAny<Stream>(), "doc.pdf", "Application/PDF", null, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new AttachmentDto(Guid.NewGuid(), (short)AttachmentEntityKind.Contact, Guid.NewGuid(), "doc.pdf", "Application/PDF", 10, null, DateTime.UtcNow, false));

        var resp = await controller.UploadAsync((short)AttachmentEntityKind.Contact, Guid.NewGuid(), formFile, null, null, CancellationToken.None);
        resp.Should().BeOfType<OkObjectResult>();
        service.VerifyAll();
    }

    [Fact]
    public async Task UploadAsync_ShouldAccept_WhenAllowedMimeTypesEmpty()
    {
        var opts = new AttachmentUploadOptions { MaxSizeBytes = 1024, AllowedMimeTypes = Array.Empty<string>() };
        var (controller, service, _, current) = Create(opts);
        var data = new byte[10];
        var formFile = new FormFile(new MemoryStream(data), 0, data.Length, "file", "a.bin") { Headers = new HeaderDictionary(), ContentType = "application/x-bin" };
        service.Setup(s => s.UploadAsync(current.UserId, AttachmentEntityKind.Contact, It.IsAny<Guid>(), It.IsAny<Stream>(), "a.bin", "application/x-bin", null, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new AttachmentDto(Guid.NewGuid(), (short)AttachmentEntityKind.Contact, Guid.NewGuid(), "a.bin", "application/x-bin", 10, null, DateTime.UtcNow, false));

        var resp = await controller.UploadAsync((short)AttachmentEntityKind.Contact, Guid.NewGuid(), formFile, null, null, CancellationToken.None);
        resp.Should().BeOfType<OkObjectResult>();
        service.VerifyAll();
    }
}
