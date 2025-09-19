using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using FinanceManager.Application;
using FinanceManager.Shared.Dtos;
using FinanceManager.Web.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceManager.Tests.Controllers;

public sealed class BackgroundTasksControllerTests
{
    private static (BackgroundTasksController controller, Guid userA, Guid userB, BackgroundTaskManager manager) Create()
    {
        var manager = new BackgroundTaskManager();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var controller = new BackgroundTasksController(manager, NullLogger<BackgroundTasksController>.Instance);
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userA.ToString()) }, "test"));
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return (controller, userA, userB, manager);
    }

    [Fact]
    public void Enqueue_ShouldReturnTask()
    {
        var (controller, userA, _, manager) = Create();
        var result = controller.Enqueue(BackgroundTaskType.BackupRestore, false); // allowDuplicate false
        var ok = result.Result as OkObjectResult;
        ok.Should().NotBeNull();
        var info = ok!.Value as BackgroundTaskInfo;
        info.Should().NotBeNull();
        info!.UserId.Should().Be(userA);
        manager.GetAll().Should().ContainSingle(t => t.Id == info.Id);
    }

    [Fact]
    public void Enqueue_ShouldReturnExisting_WhenDuplicateNotAllowed()
    {
        var (controller, _, _, _) = Create();
        var first = (controller.Enqueue(BackgroundTaskType.BookAllDrafts, false).Result as OkObjectResult)!.Value as BackgroundTaskInfo;
        var second = (controller.Enqueue(BackgroundTaskType.BookAllDrafts, false).Result as OkObjectResult)!.Value as BackgroundTaskInfo;
        second!.Id.Should().Be(first!.Id); // same
    }

    [Fact]
    public void Enqueue_ShouldAllowDuplicate_WhenFlagTrue()
    {
        var (controller, _, _, _) = Create();
        var first = (controller.Enqueue(BackgroundTaskType.ClassifyAllDrafts, true).Result as OkObjectResult)!.Value as BackgroundTaskInfo;
        var second = (controller.Enqueue(BackgroundTaskType.ClassifyAllDrafts, true).Result as OkObjectResult)!.Value as BackgroundTaskInfo;
        second!.Id.Should().NotBe(first!.Id); // different
    }

    [Fact]
    public void GetActiveAndQueued_ShouldFilterByUser()
    {
        var (controller, userA, userB, manager) = Create();
        // Enqueue task for current user (userA)
        controller.Enqueue(BackgroundTaskType.BackupRestore, false);
        // Manually enqueue for other user by bypassing controller
        manager.Enqueue(BackgroundTaskType.BookAllDrafts, userB);
        var listResult = controller.GetActiveAndQueued();
        var ok = listResult.Result as OkObjectResult;
        ok.Should().NotBeNull();
        var tasks = ((System.Collections.Generic.IEnumerable<BackgroundTaskInfo>)ok!.Value!).ToList();
        tasks.Should().OnlyContain(t => t.UserId == userA);
    }

    [Fact]
    public void CancelOrRemove_ShouldCancelRunning()
    {
        var (controller, userA, _, manager) = Create();
        var info = (controller.Enqueue(BackgroundTaskType.BackupRestore, false).Result as OkObjectResult)!.Value as BackgroundTaskInfo;
        // Update to running
        manager.UpdateTaskInfo(info! with { Status = BackgroundTaskStatus.Running, StartedUtc = DateTime.UtcNow });
        var response = controller.CancelOrRemove(info!.Id);
        response.Should().BeOfType<NoContentResult>();
        var updated = manager.Get(info.Id);
        updated!.Status.Should().Be(BackgroundTaskStatus.Cancelled);
    }

    [Fact]
    public void CancelOrRemove_ShouldRemoveQueued()
    {
        var (controller, _, _, manager) = Create();
        var info = (controller.Enqueue(BackgroundTaskType.BookAllDrafts, false).Result as OkObjectResult)!.Value as BackgroundTaskInfo;
        var response = controller.CancelOrRemove(info!.Id);
        response.Should().BeOfType<NoContentResult>();
        manager.Get(info.Id).Should().BeNull();
    }

    [Fact]
    public void GetDetail_ShouldReturnNotFound_ForOtherUser()
    {
        var (controller, _, userB, manager) = Create();
        // add task for userB directly
        var otherTask = manager.Enqueue(BackgroundTaskType.ClassifyAllDrafts, userB);
        var resp = controller.GetDetail(otherTask.Id);
        resp.Result.Should().BeOfType<NotFoundResult>();
    }
}
