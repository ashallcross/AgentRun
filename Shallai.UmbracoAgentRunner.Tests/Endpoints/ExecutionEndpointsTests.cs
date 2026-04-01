using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shallai.UmbracoAgentRunner.Endpoints;
using Shallai.UmbracoAgentRunner.Engine;
using Shallai.UmbracoAgentRunner.Instances;
using Shallai.UmbracoAgentRunner.Models.ApiModels;
using Shallai.UmbracoAgentRunner.Workflows;

namespace Shallai.UmbracoAgentRunner.Tests.Endpoints;

[TestFixture]
public class ExecutionEndpointsTests
{
    private IActiveInstanceRegistry _activeInstanceRegistry = null!;
    private ExecutionEndpoints _endpoints = null!;

    [SetUp]
    public void SetUp()
    {
        _activeInstanceRegistry = Substitute.For<IActiveInstanceRegistry>();

        _endpoints = new ExecutionEndpoints(
            Substitute.For<IInstanceManager>(),
            Substitute.For<IProfileResolver>(),
            Substitute.For<IWorkflowOrchestrator>(),
            Substitute.For<IWorkflowRegistry>(),
            _activeInstanceRegistry,
            NullLogger<ExecutionEndpoints>.Instance,
            NullLoggerFactory.Instance);
    }

    [Test]
    public void SendMessage_NoRunningInstance_Returns409()
    {
        _activeInstanceRegistry.GetMessageWriter("inst-001").Returns((ChannelWriter<string>?)null);

        var result = _endpoints.SendMessage("inst-001", new SendMessageRequest { Message = "Hello" });

        Assert.That(result, Is.InstanceOf<ConflictObjectResult>());
        var conflict = (ConflictObjectResult)result;
        var error = conflict.Value as ErrorResponse;
        Assert.That(error?.Error, Is.EqualTo("not_running"));
    }

    [Test]
    public void SendMessage_EmptyMessage_Returns400()
    {
        var result = _endpoints.SendMessage("inst-001", new SendMessageRequest { Message = "   " });

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result;
        var error = badRequest.Value as ErrorResponse;
        Assert.That(error?.Error, Is.EqualTo("empty_message"));
    }

    [Test]
    public void SendMessage_RunningInstance_WritesToChannelAndReturns200()
    {
        var channel = Channel.CreateUnbounded<string>();
        _activeInstanceRegistry.GetMessageWriter("inst-001").Returns(channel.Writer);

        var result = _endpoints.SendMessage("inst-001", new SendMessageRequest { Message = "Hello agent" });

        Assert.That(result, Is.InstanceOf<OkResult>());
        Assert.That(channel.Reader.TryRead(out var msg), Is.True);
        Assert.That(msg, Is.EqualTo("Hello agent"));
    }
}
