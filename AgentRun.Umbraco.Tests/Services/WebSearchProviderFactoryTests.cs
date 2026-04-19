using NSubstitute;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Services;

namespace AgentRun.Umbraco.Tests.Services;

[TestFixture]
public class WebSearchProviderFactoryTests
{
    private static IWebSearchProvider StubProvider(string name)
    {
        var provider = Substitute.For<IWebSearchProvider>();
        provider.Name.Returns(name);
        return provider;
    }

    [Test]
    public void GetAsync_ResolvesByName_WhenRegistered()
    {
        var brave = StubProvider("Brave");
        var tavily = StubProvider("Tavily");
        var factory = new WebSearchProviderFactory(new[] { brave, tavily });

        var result = factory.GetAsync("Brave", CancellationToken.None);

        Assert.That(result, Is.SameAs(brave));
    }

    [Test]
    public void GetAsync_ResolvesByName_CaseInsensitive()
    {
        var brave = StubProvider("Brave");
        var factory = new WebSearchProviderFactory(new[] { brave });

        Assert.That(factory.GetAsync("brave", CancellationToken.None), Is.SameAs(brave));
        Assert.That(factory.GetAsync("BRAVE", CancellationToken.None), Is.SameAs(brave));
    }

    [Test]
    public void GetAsync_UnknownName_Throws_EnumeratingRegisteredProviders()
    {
        var brave = StubProvider("Brave");
        var tavily = StubProvider("Tavily");
        var factory = new WebSearchProviderFactory(new[] { brave, tavily });

        var ex = Assert.Throws<WebSearchException>(() =>
            factory.GetAsync("Nonexistent", CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("Nonexistent"));
        Assert.That(ex.Message, Does.Contain("Brave"));
        Assert.That(ex.Message, Does.Contain("Tavily"));
    }

    [Test]
    public void GetRegisteredProviderNames_PreservesRegistrationOrder()
    {
        var brave = StubProvider("Brave");
        var tavily = StubProvider("Tavily");
        var factory = new WebSearchProviderFactory(new[] { brave, tavily });

        var names = factory.GetRegisteredProviderNames();

        Assert.That(names, Is.EqualTo(new[] { "Brave", "Tavily" }));
    }

    [Test]
    public void GetRegisteredProviderNames_ReversedRegistration_ReflectsOrder()
    {
        var brave = StubProvider("Brave");
        var tavily = StubProvider("Tavily");
        var factory = new WebSearchProviderFactory(new[] { tavily, brave });

        var names = factory.GetRegisteredProviderNames();

        Assert.That(names, Is.EqualTo(new[] { "Tavily", "Brave" }));
    }

    [Test]
    public void Ctor_DuplicateProviderName_Throws()
    {
        var brave1 = StubProvider("Brave");
        var brave2 = StubProvider("Brave");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new WebSearchProviderFactory(new[] { brave1, brave2 }));

        Assert.That(ex!.Message, Does.Contain("Brave"));
        Assert.That(ex.Message, Does.Contain("unique").IgnoreCase.Or.Contain("duplicate").IgnoreCase);
    }

    [Test]
    public void GetAsync_NullName_ThrowsArgumentException()
    {
        var factory = new WebSearchProviderFactory(new[] { StubProvider("Brave") });

        Assert.Throws<ArgumentException>(() =>
            factory.GetAsync(null!, CancellationToken.None));
    }

    [Test]
    public void GetAsync_EmptyName_ThrowsArgumentException()
    {
        var factory = new WebSearchProviderFactory(new[] { StubProvider("Brave") });

        Assert.Throws<ArgumentException>(() =>
            factory.GetAsync("", CancellationToken.None));
    }
}
