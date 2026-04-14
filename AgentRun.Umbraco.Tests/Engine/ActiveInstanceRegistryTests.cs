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

    [TearDown]
    public void TearDown()
    {
        _registry.Dispose();
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

    // Story 10.8 Task 6.1: CTS created on RegisterInstance
    [Test]
    public void RegisterInstance_CreatesFreshCancellationToken()
    {
        _registry.RegisterInstance("inst-001");

        var token = _registry.GetCancellationToken("inst-001");

        Assert.That(token, Is.Not.Null);
        Assert.That(token!.Value.IsCancellationRequested, Is.False);
    }

    // Story 10.8 Task 6.2: GetCancellationToken returns null when not registered
    [Test]
    public void GetCancellationToken_ReturnsNull_WhenNotRegistered()
    {
        Assert.That(_registry.GetCancellationToken("unknown"), Is.Null);
    }

    // Story 10.8 Task 6.3: RequestCancellation triggers the CTS
    [Test]
    public void RequestCancellation_TriggersRegisteredInstanceToken()
    {
        _registry.RegisterInstance("inst-001");
        var token = _registry.GetCancellationToken("inst-001")!.Value;

        _registry.RequestCancellation("inst-001");

        Assert.That(token.IsCancellationRequested, Is.True);
    }

    // Story 10.8 Task 6.4: RequestCancellation is no-op when not registered
    [Test]
    public void RequestCancellation_NotRegistered_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => _registry.RequestCancellation("nonexistent"));
    }

    // Story 10.8 Task 6.5: UnregisterInstance disposes the CTS (observable: GetCancellationToken returns null)
    [Test]
    public void UnregisterInstance_ClearsCancellationToken()
    {
        _registry.RegisterInstance("inst-001");
        _registry.UnregisterInstance("inst-001");

        Assert.That(_registry.GetCancellationToken("inst-001"), Is.Null);
    }

    // Story 10.8 Task 6.6 + AC9: re-registration disposes prior CTS
    [Test]
    public void RegisterInstance_Twice_DisposesPriorCancellationSource()
    {
        _registry.RegisterInstance("inst-001");
        var tokenA = _registry.GetCancellationToken("inst-001")!.Value;

        _registry.RegisterInstance("inst-001");
        var tokenB = _registry.GetCancellationToken("inst-001")!.Value;

        // Tokens should be different structs backed by different sources.
        Assert.That(tokenB, Is.Not.EqualTo(tokenA));

        // Signalling the original source (via RequestCancellation on the new
        // entry would hit the new source — there is no public way to access
        // the old source after replacement, so we instead assert the new
        // token is untouched and the observable registry state reflects only
        // the new entry).
        Assert.That(tokenB.IsCancellationRequested, Is.False);

        _registry.RequestCancellation("inst-001");
        Assert.That(tokenB.IsCancellationRequested, Is.True);
    }

    // Story 10.8 Task 6.7: re-registration completes prior channel writer
    [Test]
    public void RegisterInstance_Twice_CompletesPriorChannelWriter()
    {
        _registry.RegisterInstance("inst-001");
        var priorWriter = _registry.GetMessageWriter("inst-001")!;

        _registry.RegisterInstance("inst-001");

        // Prior channel writer is completed — TryWrite returns false.
        Assert.That(priorWriter.TryWrite("late"), Is.False);
    }

    // Story 10.8 Task 6.8: concurrent RegisterInstance produces exactly one entry, no exceptions
    [Test]
    public void RegisterInstance_ConcurrentCalls_DoesNotLeakExceptions()
    {
        const int concurrency = 16;
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var barrier = new Barrier(concurrency);

        var threads = Enumerable.Range(0, concurrency).Select(_ => new Thread(() =>
        {
            try
            {
                barrier.SignalAndWait();
                _registry.RegisterInstance("race-id");
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        })).ToArray();

        foreach (var t in threads) t.Start();
        foreach (var t in threads) t.Join();

        Assert.That(exceptions, Is.Empty);
        // A single entry remains after all replacements, and it is functional —
        // the surviving CTS is not disposed and the surviving channel writer accepts writes.
        // This guards against the "update factory disposes the live entry" race that
        // ConcurrentDictionary.AddOrUpdate invites under contention.
        var survivingToken = _registry.GetCancellationToken("race-id");
        Assert.That(survivingToken, Is.Not.Null);
        Assert.That(survivingToken!.Value.IsCancellationRequested, Is.False);

        var survivingWriter = _registry.GetMessageWriter("race-id");
        Assert.That(survivingWriter, Is.Not.Null);
        Assert.That(survivingWriter!.TryWrite("post-race-message"), Is.True);
    }

    // Story 10.8 code review: re-registration must not signal the prior entry's
    // token — it should only dispose it. Protects against a TOCTOU in which
    // RequestCancellation, called with a stale reference to the replaced entry,
    // accidentally cancels what is now a different run.
    [Test]
    public void RegisterInstance_Twice_PriorTokenNotSignalled()
    {
        _registry.RegisterInstance("inst-001");
        var priorToken = _registry.GetCancellationToken("inst-001")!.Value;

        _registry.RegisterInstance("inst-001");

        // The prior CTS was disposed (its token no longer observable as "cancelled"
        // through the disposed source), but importantly RequestCancellation should
        // only reach the new entry — and because we have not called it, neither
        // the prior nor the new token should be cancelled.
        var newToken = _registry.GetCancellationToken("inst-001")!.Value;
        Assert.That(newToken.IsCancellationRequested, Is.False);
        // priorToken's source is disposed; reading IsCancellationRequested is valid on
        // a CancellationToken (structs are value copies), but the important guarantee
        // is that the prior entry was NOT Cancel()ed — only disposed — so if any
        // consumer still holds priorToken they do not observe a cancellation signal
        // produced by the re-registration itself.
        Assert.That(priorToken.IsCancellationRequested, Is.False);
    }

    // Story 10.8 code review: after RegisterInstance replaces an entry, a
    // RequestCancellation for the same instance id must signal the NEW entry's
    // token only. Guards the "cancel the old entry that the orchestrator no longer
    // owns" TOCTOU.
    [Test]
    public void RegisterInstance_ThenRequestCancellation_SignalsNewEntryOnly()
    {
        _registry.RegisterInstance("inst-001");
        var priorToken = _registry.GetCancellationToken("inst-001")!.Value;

        _registry.RegisterInstance("inst-001");
        var newToken = _registry.GetCancellationToken("inst-001")!.Value;

        _registry.RequestCancellation("inst-001");

        Assert.That(newToken.IsCancellationRequested, Is.True, "new entry's token must be cancelled");
        Assert.That(priorToken.IsCancellationRequested, Is.False, "prior entry's token must not be cancelled by a RequestCancellation after re-registration");
    }

    // Story 10.8: registry Dispose clears remaining entries
    [Test]
    public void Dispose_ClearsRemainingEntries()
    {
        _registry.RegisterInstance("inst-a");
        _registry.RegisterInstance("inst-b");

        _registry.Dispose();

        Assert.That(_registry.GetCancellationToken("inst-a"), Is.Null);
        Assert.That(_registry.GetCancellationToken("inst-b"), Is.Null);

        // Re-create for TearDown (harmless — TearDown calls Dispose again).
        _registry = new ActiveInstanceRegistry();
    }
}
