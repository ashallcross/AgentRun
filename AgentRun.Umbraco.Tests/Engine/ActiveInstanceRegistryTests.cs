using AgentRun.Umbraco.Engine;

namespace AgentRun.Umbraco.Tests.Engine;

[TestFixture]
public class ActiveInstanceRegistryTests
{
    private ActiveInstanceRegistry _registry = null!;

    [SetUp]
    public void SetUp()
    {
        _registry = new ActiveInstanceRegistry();
    }

    [Test]
    public void GetMessageReader_UnregisteredInstance_ReturnsNull()
    {
        var reader = _registry.GetMessageReader("unknown-id");

        Assert.That(reader, Is.Null);
    }

    [Test]
    public void GetMessageWriter_UnregisteredInstance_ReturnsNull()
    {
        var writer = _registry.GetMessageWriter("unknown-id");

        Assert.That(writer, Is.Null);
    }

    [Test]
    public void RegisterInstance_ReturnsReader_WriterAvailable()
    {
        var reader = _registry.RegisterInstance("inst-001");

        Assert.That(reader, Is.Not.Null);
        Assert.That(_registry.GetMessageWriter("inst-001"), Is.Not.Null);
    }

    [Test]
    public void RegisterInstance_WriteThenRead_RoundTrips()
    {
        var reader = _registry.RegisterInstance("inst-001");
        var writer = _registry.GetMessageWriter("inst-001")!;

        writer.TryWrite("hello");

        Assert.That(reader.TryRead(out var msg), Is.True);
        Assert.That(msg, Is.EqualTo("hello"));
    }

    [Test]
    public void UnregisterInstance_RemovesChannel()
    {
        _registry.RegisterInstance("inst-001");

        _registry.UnregisterInstance("inst-001");

        Assert.That(_registry.GetMessageReader("inst-001"), Is.Null);
        Assert.That(_registry.GetMessageWriter("inst-001"), Is.Null);
    }

    [Test]
    public void UnregisterInstance_NonExistent_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => _registry.UnregisterInstance("nonexistent"));
    }

    [Test]
    public void RegisterInstance_Twice_ReplacesChannel()
    {
        var reader1 = _registry.RegisterInstance("inst-001");
        var reader2 = _registry.RegisterInstance("inst-001");

        Assert.That(reader2, Is.Not.SameAs(reader1));
    }
}
